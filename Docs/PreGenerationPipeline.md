# Pre-Generation 管线

Pre-Generation 管线在 LLM 调用之前执行，负责构建 `GenerationEnv`：收集 System Prompt、注入聊天历史、选择 API、注册工具等。

---

## IPreGenerationNode

```csharp
public interface IPreGenerationNode : ITreeNode
{
    Task<NodeResult> ExecuteAsync(PreNodeExecutionContext context);
}
```

### 最小实现

```csharp
[NodeInfo("node.my_fragment", Icon = "✦", Color = "var(--node-fragment)",
    CategoryKeys = ["category.content"])]
public class MyFragmentNode : IPreGenerationNode
{
    public string Id { get; set; } = Guid.NewGuid().ToString();  // 需 { get; set; } 以支持序列化

    public string Name { get; set; } = "My Fragment";

    [NodeProperty("prop.my_fragment.text", Order = 0)]
    public string Text { get; set; } = "";

    public Task<NodeResult> ExecuteAsync(PreNodeExecutionContext context)
    {
        context.Env.Transient.Fragments.Add(new ContextSegment
        {
            Message = new ChatMessage { Content = Text },
            From = PromptBuilder.From.system
        });
        return Task.FromResult(NodeResult.SuccessResult());
    }
}
```

### 自动发现

`PreGenerationNodeSerializer` 通过 `IPluginLoaderService.GetImplementingTypes(typeof(IPreGenerationNode))` 扫描所有程序集。节点实现 `IPreGenerationNode` 并标记 `[NodeInfo]` 即自动出现在编辑器菜单中，无需手动注册。

---

## PreNodeExecutionContext

```csharp
public class PreNodeExecutionContext
{
    public PreGenerationEnv Env { get; }
    public CancellationToken CancellationToken { get; }
}
```

## PreGenerationEnv

```csharp
public class PreGenerationEnv
{
    public TransientEnv Transient { get; set; } = new();   // 每次生成重建
    public PersistentEnv Persistent { get; set; }           // 跨生成持久化
}
```

### TransientEnv — 每次生成重建

| 属性 | 类型 | 说明 |
|------|------|------|
| `Fragments` | `List<ContextSegment>` | 发送给 LLM 的上下文片段 |
| `Tools` | `List<IToolV2>` | 本次生成可用工具 |
| `API` | `APISetting?` | API 配置实例 |
| `SharedState` | `Dictionary<string, object>` | 跨节点共享状态 |

### PersistentEnv — 跨生成持久化

| 属性 | 类型 | 说明 |
|------|------|------|
| `KVData` | `IKVDataService` | 键值存储 |
| `ChatGuid` | `Guid` | 当前对话 ID |
| `AgentGuid` | `Guid` | 当前 Agent ID |
| `ToolRegistry` | `IToolRegistry` | 工具注册表 |
| `Serializer` | `IPreGenerationNodeSerializer` | 节点树序列化器 |
| `LocService` | `ILocService` | 本地化服务 |
| `DebugOutput` | `IDebugOutputService` | 调试输出 |
| `PostGenerationManager` | `IPostGenerationManagerService?` | 后生成管线管理器 |
| `GetChat()` | `Chat` | 按需加载当前对话对象 |
| `GetAgent()` | `Agent` | 按需加载当前 Agent 对象 |

---

## 执行流程

```
Agent.PreGenerationTreeJson
  → PreGenerationNodeSerializer.Deserialize → IPreGenerationNode 树
  → rootNode.ExecuteAsync(context)（GenerationManagerV2.BuildEnvironment 直接调用）
      → 逐节点执行，修改 TransientEnv
      → 返回填充好的 PreGenerationEnv
  → ToolCallLoop (LLM 调用 + 工具执行循环)
```

节点的 `ExecuteAsync` 在 `ToolCallLoop` **之前**运行，用于准备环境。

---

## 预设系统

预生成预设存储在 KVData `"GenerationManager"` / `"generation_presets"` 中：

```csharp
public class PreGenerationPreset
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "New Preset";
    public string RootNodeJson { get; set; } = "{}";
}
```

`CallNode` 通过 `PresetId` 引用预设，执行时加载预设的 `RootNodeJson` 并反序列化执行。

---

## 关键内置节点

| 节点 | 职责 |
|------|------|
| `SequenceNode` | 顺序执行子节点，支持 Repeat |
| `FragmentNode` | 注入自定义片段 (role + content) |
| `AdvancedFragmentNode` | 注入带图像、tool_calls、tool_call_id、thinking 与自定义参数的复杂角色片段 |
| `SpecialRoleImageCarrierNode` | 把特殊角色的图像搬到相邻伪 user 消息，并与原消息成组 |
| `CallNode` | 通过 PresetId 引用预设 |
| `AppendChatMessagesNode` | 加载聊天历史并注入 Fragments |
| `APISelectNode` | 选择 API 配置 |
| `ToolPresetNode` | 按预设名加载工具列表 |
| `ToolInstantiateNode` | 实例化单个工具 |
| `IfNode` / `AdvancedIfNode` | 条件分支 |
| `SubAgentNode` | 嵌套子代理调用 |
| `DynPromptNode` | 动态模板提示 |

### 复杂角色片段 `AdvancedFragmentNode`

`AdvancedFragmentNode` 是 `FragmentNode` 的高级版本，用于手工构造复杂角色负载，也用于在真实管线里复现特殊角色携带图像的场景：

| 属性 | 说明 |
|------|------|
| `From` | 片段角色，包含 `tool_result` |
| `Content` | 文本内容，可为空（构造纯图像消息） |
| `ImagesJson` | 图像数组；编辑器可直接上传，JSON 元素支持 base64、data URL，或 `{ "mimeType": "image/png", "base64": "..." }` / `{ "mimeType": "image/png", "fileName": "..." }` |
| `ToolCallId` | `tool_result` 必填，写入 `ChatMessage.id` |
| `ToolCallsJson` | assistant 的 `tool_calls` 数组；`arguments` 为对象时自动序列化成字符串 |
| `Thinking` | assistant 的 `reasoning_content`，留空表示不携带 |
| `CustomPropertiesJson` | 消息级自定义参数，合并到请求体的 message 对象 |

节点只做解析与构造，不判断角色“是否应该”携带某个字段——不同兼容端点与客户端 `as_is` 模式的接受范围不同；唯一的结构性校验是 `tool_result` 必须带 `ToolCallId`。图像或 JSON 非法时返回 `PARSE_ERROR` / `DATA_MISSING`，不会添加半成品片段。文件引用（`fileName`）在节点执行时通过 `IImageAttachmentService` 解析为 Base64，缺服务或文件缺失都会显式失败（`SERVICE_ERROR` / `DATA_MISSING`）。

OpenAI 客户端对 `tool_result` 消息的图像与其他角色使用同一套逻辑：携带 `Images` 时把 `content` 序列化为内容块数组（`text` + 每个图像一个 `image_url`），纯文本时仍保持字符串 `content`，并始终保留 `tool_call_id`。图像数据缺失会在请求构建阶段显式抛异常，不会被静默丢弃。

是否接受 `tool` 角色携带图像取决于目标端点；端点只允许 `user` 携带 `image_url` 时，仍需加入 `SpecialRoleImageCarrierNode` 把图像搬到伪 `user` 消息。

### 特殊角色图像搬运 `SpecialRoleImageCarrierNode`

OpenAI Chat Completions 规范中只有 `user` 消息允许 `image_url`；`system` / `assistant` / `tool` 携带图像属于部分兼容端点的扩展（SharperLLM 客户端会原样序列化这些角色的图像）。该节点把这些角色消息中的图像拆到一条相邻的伪 `user` 消息里，原消息保留文本、`tool_calls`、`tool_call_id`、`thinking` 与自定义参数，两者通过片段 `Metadata["image_carrier_group"]` 标记为同一组（伪消息另有 `Metadata["image_carrier"] = "synthetic"`）。

拆分位置按协议约束决定：

- **system**：原消息在前、伪 user 在后，保持 system 仍可位于对话开头。
- **tool_result**：整段连续工具结果全部输出后，再追加一条伪 user，图像合并到这一条消息；绝不把 user 消息插进 `assistant(tool_calls)` 与 `tool` 之间。
- **assistant**：伪 user 在前、原 assistant 在后，assistant 带 `tool_calls` 时同样不破坏配对。

节点是显式兼容开关：目标端点本身支持特殊角色图像时不要加入。只有被勾选的角色会被处理，`user` 图像永远不动；图像 Base64 缺失时返回 `DATA_MISSING`，且不会对上下文做部分改写。


### 子智能体工具 `subagent_call` 的会话

`SubAgentToolNode` 把子智能体配置注册进 `subagent_call` 工具。与 `SubAgentNode`
（每次调用都是全新上下文）不同，**工具每次调用都会落成一条持久会话**：

- **入参**：`subagent`（必填）、`task`（必填）、`session_id`（可选，续聊）、
  `title`（可选，新会话标题；缺省取 task 首行前 60 字）。`chat_id` 作为 `session_id` 的别名被接受。
- **出参信封**（始终 JSON）：
  `{"ok":true,"session_id":"…","subagent":"…","status":"completed","message_count":12,"output":"…"}`；
  失败时 `{"ok":false,"session_id":"…","code":"SESSION_NOT_FOUND","error":"…"}`。
  落盘失败不会中断调用，但会带上 `"persisted":false` 与 `persist_error`。
- **上下文**：续聊时载入会话历史 + 本次 `task`，**不注入父级 Fragments**（与工具既有行为一致）；
  父级上下文只在 `SubAgentNode` 里注入。
- **`output` 与 OutputMode**：`LastMessage` = 最终 AI 文本；`FullJson` = 整段交互的 JSON **字符串**，
  即父级 prompt 里看到的是一段被转义的 JSON；`None` = 空字符串（仍返回 `session_id`）。
- **错误码**：`INVALID_ARGUMENTS`、`SUBAGENT_REQUIRED`、`SUBAGENT_NOT_REGISTERED`、`TREE_NOT_FOUND`、
  `INVALID_SESSION_ID`、`SESSION_NOT_FOUND`、`SESSION_OWNER_MISMATCH`、`SESSION_STORE_ERROR`、
  `NO_API`、`TREE_FAILED`、`LOOP_FAILED`。
- **存储**：KVData 空间 `SubAgent`，`sessions` 为索引、`session_{guid:N}` 为单会话全量 JSON；
  不写入 `Chats` / 消息存储。查看、导出、删除见 Settings 的「SubAgent 会话」面板（`SubAgentSessionPanel`）。
- **作用域开关**（`SubAgentToolNode` 属性，默认都继承父级）：

  | 属性 | `true`（默认） | `false` |
  |------|----------------|---------|
  | `SharedAgentGuid` | 子代理继承父级 `AgentGuid` | 用配置 Guid 作独立 Agent 身份（同 `SubAgentNode.SharedGuid=false`） |
  | `SharedChatGuid` | 子代理继承父级 `ChatGuid` | 每个会话用会话 Id 作独立 Chat 作用域（Chat 级变量、记忆按会话隔离） |

  注意 `SubAgentNode` / `PostSubAgentNode` 只有一个 `SharedGuid`，它**只影响 AgentGuid**，
  不涉及 ChatGuid；需要隔离 Chat 作用域时请使用工具节点上的 `SharedChatGuid`。

---

## 错误码

```csharp
public static class NodeErrorCodes
{
    public const string PresetNotFound  = "PRESET_NOT_FOUND";
    public const string ToolNotFound    = "TOOL_NOT_FOUND";
    public const string ApiUnavailable  = "API_UNAVAILABLE";
    public const string DataMissing     = "DATA_MISSING";
    public const string ParseError      = "PARSE_ERROR";
    public const string ServiceError    = "SERVICE_ERROR";
    public const string ConfigNotFound  = "CONFIG_NOT_FOUND";
    public const string Cancelled       = "CANCELLED";
}
```

使用：`NodeResult.Failure(NodeErrorCodes.PresetNotFound, "预设未找到", $"ID: {id}", nodeId, nodeName)`
