# 自定义请求体与请求头

> 本文是「高级 → 自定义请求体 / 自定义请求头」的共享契约：哪些服务方支持、
> 客户端预写入什么、自定义值如何覆盖、失败如何暴露。

## 1. 覆盖范围

| 配置类型 | 自定义请求体字段 | 自定义请求头字段 | 预写入项 |
|---|---|---|---|
| `OpenAI` | `OpenAICustomRequestBody` | `OpenAICustomHeaders` | 请求头 `Authorization: Bearer <key>`；请求体 `model` / `messages` / `temperature` / `max_tokens` / `stream` / `tools` |
| `DeepSeek` | `DeepSeekCustomRequestBody` | `DeepSeekCustomHeaders` | 同上，另有请求体 `thinking`，以及选择推理档位时的 `reasoning_effort` |
| `OpenCode Go`（OpenAI 兼容） | `OpenCodeGoCustomRequestBody` | `OpenCodeGoCustomHeaders` | 请求头 `Authorization: Bearer <key>`、`User-Agent` 与 `x-opencode-session`；请求体同 OpenAI |
| `OpenCode Go`（Gemini 原生） | `OpenCodeGoGeminiCustomRequestBody` | `OpenCodeGoGeminiCustomHeaders` | 请求头 `Authorization: Bearer <key>`、`User-Agent`、`x-opencode-session`；请求体 `contents` / `systemInstruction` / `tools` / `generationConfig` 等 |
| `Gemini` | `GeminiCustomRequestBody` | `GeminiCustomHeaders` | 请求头 `x-goog-api-key`（Bearer 模式改为 `Authorization`）；请求体 `contents` / `systemInstruction` / `tools` / `generationConfig` 等 |

两种配置文本按配置分别保存：切换 OpenCode Go 的接口模式不会互相覆盖。

## 2. 合并顺序（自定义值最后写入）

1. 客户端按自身协议构建请求体与默认头（见上表「预写入项」）。
2. 自定义请求体以 **JSON 根级浅合并** 写入：同键整值替换（对象不递归合并）。
3. 自定义请求头逐行写入：**同名头（忽略大小写）先移除默认值再写入**，
   因此可以替换鉴权方案（例如用网关 token 替换 `Bearer`）。
4. 请求头发送时，宿主预写入的头与自定义头共用同一张表，
   自定义头覆盖同名预写入项。

## 3. 失败语义

- 自定义请求体不是合法 JSON：构建客户端时抛 `InvalidOperationException`，
  消息含 `not valid JSON` 与服务方标签。不静默忽略，避免用户以为参数生效。
- 自定义请求头不符合 `Name: Value`（缺少分隔符、名或值为空）：
  构建客户端时抛 `InvalidOperationException`，消息含 `Name: Value` 与服务方标签。
- 自定义头无法写入请求头集合（如 `Content-Type` 这类内容头）：
  `OpenAIChatCompletionClient` / `GeminiChatCompletionClient` 直接抛异常，
  不静默丢弃。
- 「获取模型」与对话请求共用同一份自定义头（`ApiConfig.CreateModelListClient` /
  `CreateGeminiModelListClient`），因此配了网关鉴权头后模型列表请求同样带得上。

## 4. OpenCode Go 的强制身份头

`User-Agent` 与 `x-opencode-session` 的取值、预写入与同名覆盖规则见
[OpenCodeGo.md](OpenCodeGo.md) 的「端点与鉴权」。

## 5. 代码位置

| 职责 | 文件 |
|---|---|
| 字段定义、合并顺序、自定义头解析 | `ShimmerChatBuiltin/ApiConfig.cs` |
| 设置页输入与提示 | `ShimmerChatBuiltin/API/ApiSettingsPage.razor` |
| 请求头落地（默认头 + 自定义头覆盖） | `SharperLLM/API/OpenAIChatCompletionClient.cs`、`SharperLLM/API/GeminiChatCompletionClient.cs` |
| 装配测试 | `ShimmerChatBuiltin.Tests/API/ApiConfigTests.cs`、`ShimmerChatBuiltin.Tests/API/GeminiApiConfigTests.cs` |
