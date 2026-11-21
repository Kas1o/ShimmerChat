# Pre-Generation 管线

Pre-Generation 管线在 LLM 调用之前执行，负责构建 `GenerationEnv`：收集 System Prompt、注入聊天历史、选择 API、注册工具等。

---

## IPreGenerationNode

```csharp
public interface IPreGenerationNode
{
    Task<NodeResult> ExecuteAsync(PreNodeExecutionContext context);
}
```

> 树中的 Id、显示名、展开状态与旁路状态由 `PreGenerationNodeWrapper` 持有；业务节点不再声明 `Id`/`Name`。
> 执行失败时包装器会自动补齐 `NodeResult.NodeId` / `NodeName`。
> 包装器旁路（`IsBypassed`，编辑器卡片头部的 ⊘ 按钮）：开启后该节点及整棵子树执行时被跳过并返回成功，
> 配置原样保留、随树持久化；损坏节点（插件卸载后负载缺失）也可先旁路止血。

### 最小实现

```csharp
[NodeInfo("node.my_fragment", Icon = "✦", Color = "var(--node-fragment)",
    CategoryKeys = ["category.content"])]
public class MyFragmentNode : IPreGenerationNode
{
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

新节点由编辑器用 `new PreGenerationNodeWrapper(new MyFragmentNode(), "显示名")` 包装；默认显示名取 `[NodeInfo]` 的 `LabelKey`。

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
| `AppendChatMessagesNode` | 加载聊天历史并注入 Fragments；图像附件解析点，可选预算处理 |
| `APISelectNode` | 选择 API 配置 |
| `ToolPresetNode` | 按预设名加载工具列表 |
| `ToolInstantiateNode` | 实例化单个工具 |
| `CodeActNode` | 加入 CodeAct C# 工具，脚本可调用本轮全部工具（见 [CodeAct.md](CodeAct.md)） |
| `IfNode` / `AdvancedIfNode` | 条件分支 |
| `SubAgentNode` | 嵌套子代理调用 |
| `DynPromptNode` | 动态模板提示 |
| `DynPromptSlotNode` | 预留 DynPrompt 注入槽（空消息 + 元数据标记） |

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


### 加载聊天历史 `AppendChatMessagesNode`

从 `PersistentEnv.Chat.Messages` 实时读取对话消息追加到 Fragments（执行期间对 `chat.Messages` 的修改立即可见），同时是**图像附件的解析点**与**工具调用完整性**的唯一检查点（悬空的 assistant 工具调用默认注入占位 tool_result 并留痕，`FailFast` 改为直接失败，错误码 `PARSE_ERROR`）。

| 属性 | 说明 |
|------|------|
| `DanglingToolCalls` | 悬空工具调用的处理策略：`InjectPlaceholder`（默认）/ `FailFast` |
| `ImageProcessing` | 历史图像进入请求前的处理策略：`AsStored`（默认，按存储原样发送）/ `Budgeted`（超预算先缩放压缩） |
| `ImagePixelBudget` | 单张图像 `宽 × 高` 乘积上限，超出即等比缩小；默认 2048×2048 ≈ 419 万像素，取值范围与默认值见 `ImageAttachmentRules` |
| `ImageCompression` | 超预算图像缩放后的重编码算法：`WebP`（默认，体积最小、保留透明）/ `Jpeg`（兼容性最好，透明填白）/ `None`（仅缩放，保持原格式） |
| `ImageQuality` | 有损编码质量（1-100，默认 85） |

历史图像每轮都会重发给模型，超预算的大图会持续放大请求体积，直到被端点的 payload / token 上限拒绝，**对话从此无法继续**。`Budgeted` 在解析为请求内容块时把超预算图像压回预算内——只作用于本次请求的内存副本，消息持久化数据与 `UserUploadImage` 文件都不被修改；越界的节点属性值在消费前收敛到合法区间并写入 DebugOutput。图像数据无法解码、引用的文件缺失、图像服务不可用都显式失败（`DATA_MISSING` / `SERVICE_ERROR`），绝不静默丢图。详见 [ImageAttachments.md](ImageAttachments.md)。

旧节点树反序列化后 `ImageProcessing` 落到默认值 `AsStored`，行为与旧版完全一致；实现 `IImageAttachmentService` 的旧插件通过接口默认实现自动忽略预算参数（原样解析）。


### DynPrompt 注入槽 `DynPromptSlotNode`

`DynPromptSlotNode` 在 Fragments 中插入一条**空内容消息**，并写入元数据标记
`Metadata["dynprompt_slot"] = true`；消息角色由节点的 `From` 属性选择（`tool_result` 会被显式拒绝，
因为没有可回答的工具调用 id）。节点只负责标出位置，不提供文本。

- 必须排在 `DynPromptNode` 之前：`DynPromptTermInjectionMode.InjectionSlot` 的词条匹配后，
  会寻找带标记的片段并把内容追加进去（多个词条按匹配顺序以换行连接），角色沿用注入槽节点。
- 整棵前生成树只能有一个注入槽：找不到返回 `INJECTION_SLOT_MISSING`，找到多个返回
  `INJECTION_SLOT_AMBIGUOUS`；校验发生在写入任何词条之前，不会静默选择其中一个位置。
- `BeforeSystem` / `AfterSystem` 依赖“第一个 system 片段”或固定插入位置，位置不够灵活，
  仅为兼容旧配置保留，不推荐使用；配置面板会提示新词条默认使用注入槽。
- 若没有任何注入槽词条匹配，注入槽仍是一条空内容消息；只在确实配置了注入槽词条时使用该节点。


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
    public const string ParameterError  = "PARAM_ERROR";

    /// <summary>需要注入槽的节点找不到带标记的注入槽。</summary>
    public const string InjectionSlotMissing   = "INJECTION_SLOT_MISSING";

    /// <summary>找到多个带标记的注入槽，注入位置不唯一。</summary>
    public const string InjectionSlotAmbiguous = "INJECTION_SLOT_AMBIGUOUS";
}
```

使用：`NodeResult.Failure(NodeErrorCodes.PresetNotFound, "预设未找到", $"ID: {id}")`。
`NodeId` / `NodeName` 由执行它的 `PreGenerationNodeWrapper` 自动补齐，业务节点不需要再传。
