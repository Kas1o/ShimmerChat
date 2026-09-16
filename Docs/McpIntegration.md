# MCP 集成（Model Context Protocol）

ShimmerChat 通过官方 **MCP C# SDK**（`ModelContextProtocol` / `ModelContextProtocol.Core` 2.2.x）接入 MCP，
并提供两块界面 + 一个前生成节点：

| 组成 | 位置 | 职责 |
|------|------|------|
| `McpToolsetPage` | 全局设置面板 `panel.mcp_toolset` | 管理 MCP 服务器端点、工具暴露范围、资源上下文注入 |
| `McpChatPanel` | 聊天页侧栏 `panel.mcp_chat` | 在当前对话中异步加载端点、应用 PROMPTS 模板、面板内直接发送 |
| `McpPreGenerationNode` | 前生成管线节点 `node.mcp_toolset` | 应用某个已配置端点：注册工具 + 注入资源上下文 |

---

## 1. 为什么使用官方 SDK

MCP 规范仍在快速演进（已发布 `2024-11-05` / `2025-03-26` / `2025-06-18` / `2025-11-25` / `2026-07-28`），
其中 `2026-07-28` 移除了 `initialize` 握手与会话，`2025-11-25` 是最后一个「握手时代」版本。
官方 SDK 内部实现了**双时代（dual-era）探测**与版本协商，并同时提供 STDIO 与 HTTP 两种传输，
因此本项目把协议层与传输层完全交给 SDK，自己只实现「配置 / 暴露策略 / UI / 节点」。

> 版本说明与逐版本字段差异见 `Docs/Research/MCP-Client-Reference.md`（调研归档，非产品文档）。

---

## 2. 传输模式

端点的 `Transport` 字段决定传输方式。

### 2.1 STDIO（本地进程）

使用 SDK `StdioClientTransport`：启动子进程，通过 stdin/stdout 交换**换行分隔**的 JSON-RPC 消息。
stderr 仅作为日志通道（转发到 `ILogger`，绝不会被当作协议消息）。

| 配置字段 | 说明 |
|----------|------|
| `Command` | 可执行文件或命令名（如 `npx`、`uvx`、`node`） |
| `Arguments` | 命令行参数，设置页中每行一个 |
| `Environment` | 附加环境变量，每行 `KEY=VALUE` |
| `InheritEnvironment` | 是否继承宿主进程环境变量（默认开启） |
| `WorkingDirectory` | 工作目录，留空使用宿主目录 |
| `InitializationTimeoutSeconds` | 握手超时，默认 30 秒 |

配置示例（`Arguments` 文本域内容）：

```
-y
@modelcontextprotocol/server-filesystem
D:\data
```

### 2.2 Streamable HTTP（首选）→ SSE（回退）

使用 SDK `HttpClientTransport`，`HttpMode` 映射到 `HttpTransportMode`：

| `HttpMode` | SDK 值 | 行为 |
|------------|--------|------|
| `AutoDetect`（默认） | `HttpTransportMode.AutoDetect` | **先尝试 Streamable HTTP，服务端不支持时自动回退到 2024-11-05 的 HTTP+SSE** |
| `StreamableHttp` | `HttpTransportMode.StreamableHttp` | 仅 Streamable HTTP |
| `Sse` | `HttpTransportMode.Sse` | 仅旧版 HTTP+SSE |

设置页与测试连接结果都会提示当前是否处于 SSE 回退状态。

`Headers` 用于需要鉴权的服务端，每行 `名称: 值`：

```
Authorization: Bearer <token>
```

---

## 3. 设置页：MCP 工具集

路径：设置面板 → **MCP 工具集**（插件面板页 `/pluginpanel/panel.mcp_toolset`）。

- **端点 CRUD**：添加 / 删除 / 放弃修改 / 保存全部。端点标识 `Id` 由名称派生（小写字母数字与下划线），
  同时作为工具名前缀与节点引用键，创建后不建议修改。
- **保存前的本地校验**：名称为空、标识非法、命令/URL 缺失、URL 非 http(s)、环境变量与请求头格式错误、
  标识重复等都会在页面顶部列出，校验不通过不会写入 KVData。
- **测试连接**：建立一次真实连接并枚举工具 / 资源 / 提示词模板，结果（含服务端名称、协商协议版本、
  SSE 回退提示、服务端 instructions）只显示在面板内，**不弹窗**。
- **工具暴露范围**：`ExposeTools` 总开关 + 逐工具勾选。未在清单中显式配置的工具按
  `ToolsEnabledByDefault` 处理；测试连接后会为新发现的工具补齐当前默认状态，不留下「隐式条目」。
  只读 / 破坏性标注（`annotations.readOnlyHint` / `destructiveHint`）会以徽标提示，仅作 UX 参考，**不是安全边界**。
- **资源上下文**：`ResourceInjection` 三态
  - `不注入`：不读取任何资源；
  - `注入勾选项`：只读取逐条勾选的资源；
  - `注入全部`：读取端点列出的全部资源，但显式取消勾选的条目仍排除。
  - `ResourceCharBudget` 为总字符预算，超出时保留靠前的块并在末尾标注被省略的 URI；单个资源另有
    `PerResourceCharLimit`（16000 字符）上限并标注截断。
- **按需资源工具**：`ExposeResourceReadTool` 会额外注入一个 `read_mcp_resource` 工具，
  模型传入 `uri` 读取资源，传空则返回该端点的资源清单。

存储位置：`KVData` 空间 `Mcp`，键 `servers`，内容为 `McpEndpointConfig[]` 的 JSON。
读取失败（JSON 损坏 / `null`）不会静默降级，而是抛出 `McpConfigException` 并由调用方呈现。

---

## 4. 前生成节点：MCP 工具集

`McpPreGenerationNode` 位于 Pre-Generation 管线，把某个端点应用到本次生成。

| 属性 | 说明 |
|------|------|
| `EndpointId` | 端点标识（设置页中显示的 `Id`） |
| `ExposeTools` | 是否把该端点的工具注册进 `TransientEnv.Tools` |
| `ToolsSelectedOnly` | 只暴露设置页中显式勾选的工具 |
| `ResourceInjection` | 资源注入模式：`沿用端点配置` / `不注入` / `按勾选` / `全部` |
| `ExposeResourceReadTool` | 额外注入按需读取资源的工具 |
| `InjectServerInstructions` | 把服务端自报的 `instructions` 写成 system 片段 |

执行流程：

1. 读取端点目录 → 未配置 / 未找到 / 已禁用 → 返回带错误码的 `NodeResult.Failure`（不静默跳过）；
2. 建立（或复用）会话；连接失败（进程起不来、握手失败、超时）→ `SERVICE_ERROR` + 原始异常详情；
3. `tools/list` → 按暴露策略注册 `IToolV2` 桥接；失败 → `SERVICE_ERROR`；
4. `resources/read` → 按注入策略写入 system 片段并带上预算与截断标注；失败 → `SERVICE_ERROR`；
5. 写入调试输出（工具数 / 片段数 / 注入模式）。

### 会话生命周期

一次生成内的所有 MCP 节点共享同一个 `McpSessionScope`：

- 作用域存放在 `PersistentEnv.Resources`（`GenerationResourceBag`）。
  Tool Call 循环每轮都会重建 `TransientEnv`，但 `PersistentEnv` 不变，因此**同一端点不会重复拉起进程/重复握手**；
- 生成结束时由 `GenerationManagerV2` 统一释放（`DisposeGenerationResourcesAsync`）。
  SubAgent 的独立生成环境在 `SubAgentNode` / `PostSubAgentNode` / `SubAgentToolV2` 中各自释放；
- 释放失败不会中断已完成的生成，但会写入 `IDebugOutputService`（不静默吞掉）。

---

## 5. 聊天面板：MCP 对话

`McpChatPanel` 是 `PanelDisplayPlace.Chat` 面板，出现在聊天页侧栏（与变量面板同级）。

- **异步加载**：面板挂载后逐个端点建立连接并枚举能力，边连边渲染；`ChatGuid` 变化时自动重载。
- **失败只在面板内提示**：连接失败、枚举失败、资源读取失败、发送失败都渲染为面板内的错误块，
  **全程不弹窗**；加载中显示进度，全部失败时给出汇总提示。
- **PROMPTS 模板应用**：
  1. 展开某个提示词模板，按 `prompts/list` 返回的 `arguments` 生成参数表单（必填项标注 `*`）；
  2. 参数缺失时在面板内提示并拒绝调用，不做隐式补默认值；
  3. 调用 `prompts/get` 得到多条消息，压平为可直接发送的文本（多消息时插入 `<!-- role -->` 注释）；
  4. 可**替换输入区**、**追加到输入区**，或直接**发送**。
- **面板内直接发送**：面板自带输入区（草稿由 `IPanelDraftStore` 按对话缓存，折叠或切回不丢失），
  通过 `ChatPanelContext.SendAsync` 把消息作为当前对话的用户消息入队并启动生成，**不经过主输入框**。
- **工具 / 资源视图**：只读展示哪些工具会被暴露给模型、哪些资源会进入上下文（与节点策略同源计算）。

### 插件面板的宿主契约

聊天面板不再直接接收页面实例，而是接收 `ChatPanelContext`：

```csharp
[Parameter] public ChatPanelContext? PanelContext { get; set; }
```

它提供：

| 成员 | 说明 |
|------|------|
| `Chat` / `Agent` | 与页面**同一实例**的活对象（不是快照），读写即时可见，避免同步问题 |
| `MessageStore` | 面板自行增删消息时使用 |
| `Draft` / `DraftKey` / `DraftStore` | 由宿主缓存的输入草稿 |
| `IsGenerating()` | 页面是否存在活跃生成 |
| `RequestRefreshAsync()` | 请求宿主重绘 |
| `SendUserMessageAsync(text)` | 面板内直接发送并启动生成 |
| `InsertIntoInputAsync(text, append)` | 写入主输入框（不发送） |
| `RegisterEventHandler(handler)` | 等价 `EventHandlerReg`，接收对话事件 |

页面只向内暴露这一份契约，**不会把页面对象引用交给插件**。
旧参数 `ChatGuid` / `AgentGuid` / `EventHandlerReg` 继续注入，既有面板无需改动。

---

## 6. 工具命名与参数映射

MCP 工具名允许 `.`、`/` 等字符，而 OpenAI 的 `function.name` 只接受 `[A-Za-z0-9_-]` 且不超过 64 字符。
暴露给模型的工具名统一为：

```
mcp_{端点标识}_{净化后的工具名}
```

净化会把非法字符替换为 `_`，超长时保留前缀并追加 6 位短哈希以保证唯一（`McpToolBridge.BuildExposedName`）。

`inputSchema`（JSON Schema）→ SharperLLM 的扁平 `parameters`：

- 标量类型直接映射（`number`/`integer` → Number，`boolean` → Boolean，`array` → Array，`object` → Object，其余 String）；
- `anyOf`（常见于可空字段）取第一个非 `null` 的具体类型；
- `required` 数组决定参数的必填标记；`enum` 映射为参数枚举；
- Object / Array 参数会把原始 schema 片段写进参数描述，让模型知道如何构造合法 JSON。

工具返回内容：`text` 原样拼接；`resource_link` 转为链接标注；`image` / `audio` / 二进制内嵌资源
按「类型 + 字节数」标注省略（不入上下文）；`structuredContent` 以 JSON 追加；
`isError=true` 时在开头标注 `[MCP tool '...' reported an error]`。
JSON-RPC 层错误（协议错误、未知工具）由 `McpToolBridge` 捕获为 `[MCP error] ...` 文本并保留原始信息，
是否上抛仍由 `ToolCallLoop.continueOnToolError` 决定。

---

## 7. 本地化 Key

| 前缀 | 用途 |
|------|------|
| `panel.mcp_toolset*` / `panel_mcp.*` | 设置页 |
| `panel.mcp_chat*` / `panel_mcp_chat.*` | 聊天面板 |
| `node.mcp_toolset*` / `prop.mcp_toolset.*` | 前生成节点 |
| `node_err.mcp_*` | 节点错误信息 |
| `mcp_err.*` | 配置校验错误 |

---

## 8. 已知取舍

- **不做 resources/subscribe 与 prompts 变更订阅**：面板按需重载（`Reload` / `Auto reload`），
  避免为长连接引入额外生命周期管理。
- **不做 OAuth**：HTTP 端点通过自定义 `Headers` 提供静态凭据；需要交互式 OAuth 的服务端暂不支持。
- **二进制内容不入上下文**：图片/音频只标注省略，避免把 base64 灌进 prompt。
- **`McpChatPanel` 与 `McpPreGenerationNode` 各自独立建连**：前者服务于 UI 浏览，
  后者服务于生成；两者生命周期不同（面板随组件释放，节点随生成释放），互不共享连接。
