# Gemini API 集成

> 本文是 Gemini 原生 API 接入的行为契约：角色映射、functionResponse 形状、
> finishReason 分类、思维链签名与思考档位。UI 层的恒显提示文案与本页对应。

内置 `ApiConfigType.Gemini` 走 **原生 REST 客户端**
`SharperLLM.API.GeminiChatCompletionClient`（`generateContent` / `streamGenerateContent?alt=sse`），
**不使用** Google 的 OpenAI 兼容层——该兼容层在流式工具调用上有上游缺陷（`finish_reason` 错误、
delta 缺 `index`），且 `extra_content.google.thought_signature` 无法经现有 `ToolCall` 模型传递。
OpenCode Go 的 **Gemini 模式**复用同一个客户端（鉴权改为 Bearer、端点与其会话头不同），
见 [OpenCodeGo.md](OpenCodeGo.md)。

---

## 1. 消息角色映射

| 片段（From） | Gemini 去向 | 失败策略 |
|---|---|---|
| `PromptBuilder.System` + 置顶 system 片段（第一个非 system 片段之前） | 合并进 `systemInstruction.parts` | 支持图像（inline_data） |
| `user` | `contents[].role=user`，文本 + 每图一个 `inline_data` | 缺 Base64 显式失败 |
| `assistant` | `contents[].role=model`；`toolCalls` 回放为 `functionCall` parts；**thinking 绝不回传** | 图像显式失败（无合法位置） |
| `assistant.toolCalls` 参数 | 解析回 JSON 对象进 `args` | 非法 JSON 显式失败 |
| `tool_result`（连续段） | **合并进同一个** `role=user` 的 `functionResponse` parts | 配对失败显式失败 |

**非置顶 system 片段**：Gemini 没有原生位置。默认构建请求时显式失败；
配置了「中部 System 替换模板」（含 `{0}`）则降级映射为 user turn，每次转换写调试面板。

## 2. functionResponse 形状

- 函数名反查：向前找最近 assistant 片段的 `toolCalls`，按 `ChatMessage.id`（tool_call id）精确匹配；
  无 id 按顺序回退（与 `ToolCallPairing` 同款规则）。找不到即历史损坏，显式失败。
- `response` 统一 `{"output": 文本}`；内容本身就是 JSON 对象（悬空占位注入的 `no_result`）原样透传。
- 工具结果图像走 `functionResponse.parts[].inline_data`（归属精确）；
  需要搬运到伪 user 消息时在树里挂 `SpecialRoleImageCarrierNode`（Gemini 原生支持时官方建议不挂）。
- `EmitSender`（PostToolCallNode）应保持默认 `ToolResult`，否则配对机制会注入占位、语义降级。

## 3. 思维链签名（thought signatures）

Gemini 3/4 对**当前 turn** 的工具调用强制校验签名，缺失即 400。签名经
`ChatMessage.ProviderData["gemini.thoughtSignature"]` 传递（该字段为所有 provider 的
服务端私有数据通道：不解析、不渲染、仅同名 provider 回传、其他 provider 整体忽略）。

- 并行调用只有**首个** functionCall part 携带签名（官方协议）；交错排列 FC/FR 会 400——
  连续 tool_result 合并进同一 user content 即为正确形态。
- 历史缺签名（跨模型迁移、旧数据）：垫官方 dummy 值 `skip_thought_signature_validator`，
  事件经 `ToAPISetting` 的 `log` 参数写调试面板（`APISelectNode` 已接线）。
- 流式时签名可能藏在与 finishReason 同块或更早的**空文本 part**里，客户端读满整流。
- 官方校验只覆盖当前 turn；更早的 turn 不需要处理。

## 4. finishReason 四桶

单一映射函数，流式与非流式共用（`GeminiChatCompletionClient.MapFinishReason`）：

| 桶 | 值 | 归宿 |
|---|---|---|
| 正常 | `STOP`（有工具调用时合成 `FunctionCall`）、`MAX_TOKENS`、`CONTINUATION` | Stop / Length |
| 降级 | `SAFETY`、`RECITATION`、`BLOCKLIST`、`PROHIBITED_CONTENT`、`SPII`、`LANGUAGE`、`ESCALATION`、`IMAGE_SAFETY` 族 | ContentFilter，原始值留 `ProviderData["gemini.finishReason"]` |
| 委托 | `UNEXPECTED_TOOL_CALL`、`TOO_MANY_TOOL_CALLS` | FunctionCall → 宿主现有 ToolNotFound / MaxCallsPerRound 降级机器 |
| 显式失败 | `MALFORMED_FUNCTION_CALL`、`MISSING_THOUGHT_SIGNATURE`、`MALFORMED_RESPONSE`、`OTHER`、`PUP_LIMITED_DISABLED`、未知值 | 抛 `InvalidDataException`，绝不静默吞成空回复 |

无 `candidates` 且带 `promptFeedback.blockReason` → 空 Content + ContentFilter（对齐 OpenAI
客户端对 `content_filter` 的行为）。流式只有 `usageMetadata` 的块直接跳过。
SSE 无 `[DONE]` 标记：以 finishReason 出现为终点。

## 5. 生成参数

- **temperature / top_p / top_k 恒不发送**：Gemini 4 与 3.6+ 忽略并将返回 400。UI 有恒显警告。
- 思考档位下拉**字面直译**为 `generationConfig.thinkingConfig.thinkingLevel`
  （Default = 不发送档位）。**不做模型名探测**：非法组合由 API 显式报错，
  恒显提示负责事先告知（Gemini 3/4 无法关闭思考；2.5 系的 thinkingBudget 精控走自定义请求体）。
- **推理摘要默认开**（设置页「返回推理摘要」开关，字面直译为 `thinkingConfig.includeThoughts`）：
  摘要 parts（`thought: true`）进 `ChatMessage.thinking`，与 DeepSeek 的 `reasoning_content`
  走同一条展示链路；thinking 仍然绝不回传。摘要计入输出 token，但开关不改变思考本身
  （Gemini 3/4 无法关闭思考，关掉摘要只是让思考不可见）。两个开关互不联动：
  档位默认且摘要关闭时 thinkingConfig 整体不发送。
- 自定义请求体：JSON 根级**浅合并**（同键整对象替换），非法 JSON 显式失败。
- 自定义请求头：每行 `Name: Value`，同名覆盖默认 `x-goog-api-key`（网关/代理场景），
  非法行显式失败。
- 网关只接受 Bearer 时置 `UseBearerAuth = true`：改用
  `Authorization: Bearer <apiKey>`，且不再发送 `x-goog-api-key`
  （OpenCode Go Gemini 路径即此形态）；自定义头仍可随后覆盖。
- 模型列表：`GET {url}/models`（原生形状），只保留支持 `generateContent` 的模型。
  OpenCode Go Gemini 模式例外：其模型列表是 OpenAI 形状的 Go 目录端点，不走本客户端。

## 6. 续写

Gemini 没有 assistant prefill：`IsContinuation` 对 Gemini 类型显式返回
`NodeResult.Failure`（`node_err.api_no_continuation`），不静默跳过。

## 7. 接入实测清单（代码之外）

首次接入 Gemini 真实端点时验证以下项，未验证前保持默认行为：

- [ ] `functionResponse.parts` 的 inline_data 在 v1beta 的实际接受度（含 `response` 并存）
- [ ] `CONTINUATION` finishReason 的 `continuation_token` 挂载位置与回传字段（v1 已映射 Length）
- [ ] 流式 SSE 实测：空文本 part 携带签名、SAFETY 空内容、多候选防御
- [ ] 区域可用性：Gemini API 不对中国大陆/香港提供服务，网关场景配合自定义 URL + 请求头
- [ ] OpenCode Go Gemini 路径：Bearer 鉴权与 `?alt=sse` 在网关侧的实际接受度（详见
      [OpenCodeGo.md](OpenCodeGo.md) 的实测清单）

## 8. 与 OpenAI 行为差异（对应设置页恒显提示）

| 差异 | 说明 |
|---|---|
| 思考不可关 | Gemini 3/4 恒开思考，且计入 maxOutputTokens |
| 采样参数弃用 | temperature / top_p / top_k 不发送 |
| 安全过滤更严 | ContentFilter 桶 + promptFeedback 级拦截，原始原因留 ProviderData |
| 无前缀续写 | IsContinuation 显式失败 |
| 签名回传 | 工具循环历史必须携带 thoughtSignature（缺失自动垫 dummy 并留痕） |

## 9. 已知隐患（待研究）

**thinking 字段无来源标记**：Gemini 思维链摘要与 DeepSeek `reasoning_content` 共用
`ChatMessage.thinking` 存储。对话中途切换到 OpenAI / DeepSeek 类配置后，
历史里的 Gemini 摘要会被当作本家 `reasoning_content` 回传（`ReasoningEchoMode` 默认 Full，
DeepSeek 对该字段语义敏感，严格网关可能拒收未知字段）。文本补全路径不受影响
（拼接只用 Content）。处理方向见 `OpenAIChatCompletionClient` 的
`TODO(thinking-provenance)`：来源标记 / 回传过滤 / 跨模型历史策略。
