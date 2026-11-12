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
- **日志级别**：`LogLevel` 四档（`关闭` / `仅错误` / `信息` / `详细`，级别从低到高 Off &lt; Error &lt; Info &lt; Debug），
  控制该端点在调试面板的日志量与 STDIO 服务端 stderr 转发；默认 `信息`。

存储位置：`KVData` 空间 `Mcp`，键 `servers`，内容为 `McpEndpointConfig[]` 的 JSON。
读取失败（JSON 损坏 / `null` / 目录含 `null` 条目）不会静默降级，而是抛出 `McpConfigException` 并由调用方呈现；
旧版/手改数据中显式为 `null` 的集合字段（`Arguments`/`Environment`/`Headers`/`Tools`/`Resources`）会在读取时归一化为空集合，避免触发 `NullReferenceException`。

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
| `FailurePolicy` | 连接/枚举失败时的处理：`Fail`（默认，中止本轮）或 `Skip`（跳过该端点继续，并写告警日志） |

执行流程：

1. 读取端点目录 → 未配置 / 未找到 / 已禁用 → 返回带错误码的 `NodeResult.Failure`（不静默跳过）；
2. 建立（或复用）会话；连接失败（进程起不来、握手失败、超时）→ 按 `FailurePolicy` 决定 `SERVICE_ERROR` 或跳过该端点；
3. `tools/list` → 按暴露策略注册 `IToolV3` 桥接（传递 `CancellationToken`，遵守取消契约）；失败 → 同上；
4. `resources/read` → 按注入策略写入 system 片段并带上预算与截断标注；失败 → 同上；
5. 按端点 `LogLevel` 写入调试输出（工具数 / 片段数 / 注入模式）。

### 连接生命周期：`McpRegistry`

MCP 连接由 **`McpRegistry`（进程级静态单例，完全位于内置插件内）** 统一持有，
聊天面板与前生成节点共用同一个连接池。

- **租约模型**：`AcquireAsync(config)` 增加引用计数并返回 `McpEndpointLease`；
  释放租约只减计数，计数归零后进入 `IdleTimeout`（2 分钟）保活，超时才真正关闭连接。
  因此同一对话的连续多轮生成、面板重载、面板与生成并发，**都不会重复拉起进程/重复握手**。
- **配置驱动重建**：连接指纹只包含传输字段（命令/参数/环境/工作目录/URL/HTTP 模式/请求头/握手超时）。
  暴露策略（工具勾选、资源注入、日志级别）变化**不会**触发重连；传输字段变化则自动丢弃旧连接重建。
- **失败负缓存**：连接失败后进入 `FailureBackoff`（30 秒）窗口，窗口内直接回报同一次失败，
  不再让每个调用方各自等满握手超时。配置非法（本地可判定）**不计入**负缓存。
- **失效**：设置页保存后调用 `InvalidateAll()`；仍被租约占用的连接不立即打断，
  会在租约全部释放后自然淘汰。
- **进程退出**：通过 `AppDomain.ProcessExit` 尽力关闭全部连接，避免留下孤儿子进程。
- **能力缓存**：`tools/resources/prompts` 缓存挂在会话实例上；连接复用时能力清单自动复用，
  需要强制刷新时调用 `McpEndpointSession.InvalidateCapabilities()`。

> 不依赖 `PersistentEnv.Resources`：连接不再随一次生成的生命周期被创建/销毁，
> 而是跟着进程存活。这使 MCP 完全不需共享库或主项目提供任何专属机制。

---

## 5. 聊天面板：MCP 对话

`McpChatPanel` 是 `PanelDisplayPlace.Chat` 面板，出现在聊天页侧栏（与变量面板同级）。

- **不阻容渲染**：面板挂载/切对话时，生命周期方法**立即返回**，连接在后台进行。
  MCP 握手（含失败的端点要等满超时）若在 `OnParametersSetAsync` 中 await，会让整个对话界面在连接完成前不可交互。
- **并行连接**：全部端点先以「连接中」列出，再**并行**取租约（不同端点互不阻塞；同一端点并发取用会合并）。
  串行会让总耗时随端点数线性叠加（N × 握手超时）。
- **重载不重连**：点「重新加载」只重新取一次租约并刷新能力清单，连接仍由 `McpRegistry` 复用。
- **自动重载开关**：勾选（默认）时进入面板即后台加载；取消勾选则不主动建连，只在点「重新加载」时连。
  切对话时仍会释放上一对话的连接，避免残留子进程。
- **失败只在面板内提示**：连接失败、枚举失败、资源读取失败、发送失败都渲染为面板内的错误块，
  **全程不弹窗**；加载中显示进度，全部失败时给出汇总提示。
- **渲染快照**：后台任务只改写状态集合的**结构外**内容，渲染遍历只读快照，避免「集合已被修改」异常。
- **交互超时**：提示词渲染与资源读取各自带独立超时，不借用重载用的取消令牌，避免按钮永久卡在「处理中」。
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
短名也存在 sanitize 碰撞的可能（如 `a/b` 与 `a_b` 都净化为 `a_b`）：添加工具时若检测到暴露名已被占用，
会用原始工具名的短哈希追加后缀（`McpToolBridge.BuildUniqueExposedName`）并写调试日志，绝不静默丢弃。

`inputSchema`（JSON Schema）→ SharperLLM 的扁平 `parameters`：

- 标量类型直接映射（`number`/`integer` → Number，`boolean` → Boolean，`array` → Array，`object` → Object，其余 String）；
- `anyOf`（常见于可空字段）取第一个非 `null` 的具体类型；
- `required` 数组决定参数的必填标记；`enum` 映射为参数枚举；
- Object / Array 参数会把原始 schema 片段写进参数描述，让模型知道如何构造合法 JSON。

工具返回内容：`text` 原样拼接；`resource_link` 转为链接标注；`image` / `audio` / 二进制内嵌资源
按「类型 + 字节数」标注省略（不入上下文）；`structuredContent` 以 JSON 追加；
`isError=true` 时在开头标注 `[MCP tool '...' reported an error]`。
工具执行走 `IToolV3` 结构化接口：参数 JSON 非法返回 `ToolResult.Fail(TOOL_INVALID_ARGUMENTS)`，
协议/连接/服务端拒绝等预期内失败返回 `ToolResult.Fail(TOOL_SERVICE_ERROR)`（附技术详情，由执行节点写进 DebugOutput）；
`CancellationToken` 贯穿到 SDK 调用，用户/宿主取消时抛 `OperationCanceledException` 而不是伪造失败。
是否继续由 `ToolCallLoop.continueOnToolError` 决定。

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
- **面板与节点共享连接**：`McpChatPanel` 与 `McpPreGenerationNode` 都通过 `McpRegistry` 取租约，
  因此浏览面板期间触发生成不会为同一端点多拉一个子进程。
- **静态单例而非 DI 服务**：插件服务由宿主按需实例化（无单例注册通道），
  连接池必须跨组件存活，因此由 `McpRegistry.Instance` 静态持有；
  代价是单元测试需显式 `ResetForTests()` 隔离。
