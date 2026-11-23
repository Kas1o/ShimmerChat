# OpenCode Go 集成

> 本文是 `ApiConfigType.OpenCodeGo` 的行为契约：两种接口模式、鉴权与会话头、
> 模型列表形状、Gemini 模式的请求语义与失败策略。

## 1. 配置项

| 字段 | 含义 |
|---|---|
| `OpenCodeGoMode` | 接口模式：`OpenAICompatible`（默认）或 `Gemini`。枚举按序号持久化，只能追加新值。 |
| `OpenCodeGoUrl` | OpenAI 兼容对话前缀，客户端追加 `/chat/completions`。 |
| `OpenCodeGoGeminiUrl` | Gemini 原生前缀，客户端追加 `/models/{model}:generateContent`（流式加 `?alt=sse`）。 |
| `OpenCodeGoModelsUrl` | 模型列表端点，OpenAI 形状，两种模式共用。 |
| `OpenCodeGoApiKey` | Console 个人 API Key；空值在模型列表请求时由上游返回 401，构建对话客户端时 `null` 显式失败。 |
| `OpenCodeGoModel` | 模型 ID；空值时 OpenAI 模式回退 `glm-5.3-flash`，Gemini 模式回退 `gemini-4-argon`。 |
| `OpenCodeGoStream` | 是否流式。 |
| `OpenCodeGoCtx` | 输出上限（OpenAI 的 `max_tokens` / Gemini 的 `maxOutputTokens`）。 |
| `OpenCodeGoAsIs` | 仅 OpenAI 兼容模式生效；Gemini 模式没有等价开关。 |
| `OpenCodeGoCustomRequestBody` | OpenAI 兼容模式请求体根级浅合并 JSON（同键覆盖客户端预写入项）。 |
| `OpenCodeGoCustomHeaders` | OpenAI 兼容模式自定义请求头（每行 `Name: Value`），同名头覆盖预写入的 `User-Agent` / `x-opencode-session`。 |
| `OpenCodeGoGeminiThinking` | Gemini 模式思考档位，字面直译为 `thinkingLevel`。 |
| `OpenCodeGoGeminiIncludeThoughts` | Gemini 模式是否返回推理摘要（`thinkingConfig.includeThoughts`），默认开。 |
| `OpenCodeGoGeminiMidSystemTemplate` | Gemini 模式中部 system 替换模板（含 `{0}`）。 |
| `OpenCodeGoGeminiCustomRequestBody` | Gemini 模式请求体根级浅合并 JSON。 |
| `OpenCodeGoGeminiCustomHeaders` | Gemini 模式自定义请求头，语义与 `OpenCodeGoCustomHeaders` 相同。 |

两种模式的地址分别保存：切换模式只改变协议路径，不会改写已填的地址或模型。
唯一的例外是设置页上的默认模型：模型仍是另一模式的出厂默认值
（`glm-5.3-flash` / `gemini-4-argon`）时，切换模式会把它换成当前模式的默认值，
且输入框会立刻显示新值；用户显式选择过的其他模型不会被改写。

## 2. 端点与鉴权

| 模式 | 请求 | 鉴权 |
|---|---|---|
| OpenAI 兼容 | `POST {OpenCodeGoUrl}/chat/completions` | `Authorization: Bearer <key>` |
| Gemini 原生 | `POST {OpenCodeGoGeminiUrl}/models/{model}:generateContent` | `Authorization: Bearer <key>` |

两种模式都强制附带：

- `User-Agent: ShimmerChat/2.0`（OpenCode Go 要求客户端标识自身，而不是通用 HTTP 库名）
- `x-opencode-session: <当前对话 Guid>`（会话路由与 prompt 缓存；无对话 Guid 时省略）

这两项是 OpenCode Go 对「应用身份 / 对话身份」的强制要求，由宿主预写入，
用户无需填写。设置页「高级 → 自定义请求头」允许用同名头覆盖它们：这是显式能力
（例如代理要求改写应用标识），覆盖后由用户自行保证网关仍能识别应用与对话；
恒显提示会说明这一点。完整的覆盖契约见 [RequestOverrides.md](RequestOverrides.md)。

Gemini 原生端点**不接受** Google 的 `x-goog-api-key` 作为鉴权方式，因此
`GeminiChatCompletionClient.UseBearerAuth = true` 时只发送 `Authorization: Bearer`，
不再发送 `x-goog-api-key`。这与原生 `ApiConfigType.Gemini` 的默认行为不同，
避免网关同时看到两种互相冲突的鉴权头。

## 3. 模型列表

`GET {OpenCodeGoModelsUrl}` 返回 OpenAI 形状的列表（`{"object":"list","data":[...]}`），
**两种模式共用**。Gemini 模式不能改用 Gemini 原生的 `GET {url}/models`：
OpenCode Go 的 Gemini 前缀下没有该原生列表端点，且 Go 目录里的模型元数据
（`api.generate-content`、思考能力等）都来自这份 OpenAI 形状列表。

Gemini 模式下列表仍返回 Go 目录的全部模型。网关会把任意 Go 模型翻译到
所请求的 API 家族，因此这里不做过滤；但原生 Gemini 特性（思考签名、
`thinkingLevel` 的精确认知）只在模型本身由 Google 提供时才有保证。
设置页的模型提示会显式说明这一点。

模型列表请求与对话请求共用 `User-Agent` 与自定义请求头（`ApiConfig.CreateModelListClient`）；
获取模型时没有对话上下文，因此不发送 `x-opencode-session`。

## 4. Gemini 模式语义

Gemini 模式复用 `SharperLLM.API.GeminiChatCompletionClient`，因此
[GeminiAPI.md](GeminiAPI.md) 中的角色映射、`functionResponse` 形状、
`finishReason` 四桶、思维链签名与「不发送 temperature / top_p / top_k」
全部适用。差异只在传输层：

- 鉴权头改为 `Authorization: Bearer`（见上）。
- 默认 URL 指向 `https://opencode.ai/inference/go/google/v1beta`。
- 额外携带 `User-Agent` 与 `x-opencode-session`。
- 自定义请求头（`OpenCodeGoGeminiCustomHeaders`）最后合并，同名头覆盖预写入项。

Gemini 模式特有的配置：

- **思考档位**：`Default` 不发送档位，其余字面直译为
  `generationConfig.thinkingConfig.thinkingLevel`。不做模型名探测；
  非法组合由 OpenCode Go / Gemini 显式报错。
- **推理摘要**：默认开。摘要走 `ChatMessage.thinking`，与 DeepSeek 的
  `reasoning_content` 共用展示链路；thinking 绝不回传。
- **中部 system**：留空时构建请求显式失败；配置含 `{0}` 的模板后降级为
  user turn，每次转换写入调试面板。
- **自定义请求体**：JSON 对象根级浅合并，同键整对象替换；非法 JSON 在
  `ToAPISetting` 装配时显式失败。
- **自定义请求头**：每行 `Name: Value`，同名头覆盖预写入的 `User-Agent` /
  `x-opencode-session`；非法行在装配时显式失败。
- **思考签名**：经 `ChatMessage.ProviderData["gemini.thoughtSignature"]` 回传；
  历史缺签名时垫官方 dummy 值并写调试面板（事件归类在 `Gemini` 日志通道）。

OpenAI 兼容模式的自定义请求体同样是根级浅合并，语义见
[RequestOverrides.md](RequestOverrides.md)。该模式没有独立的思考档位开关：
需要精调时用 `thinking` / `reasoning_effort` 等字段覆盖客户端预写入项。

## 5. 失败与降级

| 场景 | 行为 |
|---|---|
| `OpenCodeGoApiKey` 为 `null` | 构建对话客户端时抛 `InvalidOperationException`（`API key is required`）。 |
| 模型为空 | 按模式回退默认模型，不写入用户配置。 |
| 自定义请求体非法 JSON（两种模式） | 装配客户端时抛 `InvalidOperationException`，消息含 `not valid JSON` 与服务方标签。 |
| 自定义请求头非法行（两种模式） | 装配客户端时抛 `InvalidOperationException`，消息含 `Name: Value` 与服务方标签。 |
| 自定义头覆盖 `User-Agent` / `x-opencode-session` | 允许：同名头覆盖预写入值，设置页恒显提示要求用户自行保证网关仍能识别应用与对话。 |
| Gemini 模式 + As-is | 开关隐藏且不生效；切回 OpenAI 模式后保留原值。 |
| 中部 system 无模板 | Gemini 客户端构建请求时显式失败（不静默丢弃片段）。 |
| 前缀续写 | `APISelectNode` 对 OpenCode Go 返回 `node_err.api_no_continuation`；Gemini 原生没有 assistant prefill。 |
| 旧配置没有 `OpenCodeGoMode` | 反序列化为 `OpenAICompatible`（枚举序号 0），行为与旧版一致。 |

## 6. 代码位置

| 职责 | 文件 |
|---|---|
| 配置字段、模式枚举、客户端装配 | `ShimmerChatBuiltin/ApiConfig.cs` |
| 设置页模式切换与 Gemini 控件 | `ShimmerChatBuiltin/API/ApiSettingsPage.razor` |
| 会话头注入（对话 Guid） | `ShimmerChatBuiltin/Misc/Node/PreGeneration/APISelectNode.cs` |
| Gemini 协议实现 | `SharperLLM/API/GeminiChatCompletionClient.cs` |
| 配置装配测试 | `ShimmerChatBuiltin.Tests/API/ApiConfigTests.cs` |
