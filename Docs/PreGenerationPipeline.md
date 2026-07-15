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
    public string Id { get; } = Guid.NewGuid().ToString();

    [NodeProperty("prop.node.name", Order = -100)]
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
    public TransientEnv Transient { get; }    // 每次生成重建
    public PersistentEnv Persistent { get; }  // 跨生成持久化
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

---

## 执行流程

```
Agent.PreGenerationTreeJson
  → PreGenerationNodeSerializer.Deserialize → IPreGenerationNode 树
  → GenerationTreeExecutor.ExecuteAsync(root, persistentEnv)
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
| `CallNode` | 通过 PresetId 引用预设 |
| `AppendChatMessagesNode` | 加载聊天历史并注入 Fragments |
| `APISelectNode` | 选择 API 配置 |
| `ToolPresetNode` | 按预设名加载工具列表 |
| `ToolInstantiateNode` | 实例化单个工具 |
| `IfNode` / `AdvancedIfNode` | 条件分支 |
| `SubAgentNode` | 嵌套子代理调用 |
| `DynPromptNode` | 动态模板提示 |

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
