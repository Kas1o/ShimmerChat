# Post-Generation & Render Modifier 管线

ShimmerChat 2.0 新增两个后处理管线，分别在 LLM 响应后和消息渲染时执行。

---

## Post-Generation 管线

在 LLM 生成响应后执行，对原始响应文本进行过滤、转换、富化等处理。

### IPostGenerationNode

```csharp
public interface IPostGenerationNode : ITreeNode
{
    Task<PostNodeResult> ExecuteAsync(PostNodeExecutionContext context);
}
```

### PostNodeExecutionContext

```csharp
public class PostNodeExecutionContext
{
    public PostGenerationEnv Env { get; }
    public CancellationToken CancellationToken { get; }
}
```

### PostGenerationEnv

```csharp
public class PostGenerationEnv
{
    public string ResponseText { get; set; }                   // LLM 原始响应（节点可修改）
    public IReadOnlyList<ContextSegment> PreFragments { get; } // 前生成 Fragments（只读）
    public Dictionary<string, object> SharedState { get; }     // 节点间共享状态
    public PersistentEnv Persistent { get; }                   // 持久化服务
    public ITreeNodeSerializer Serializer { get; set; }        // 序列化器（CallNode 加载预设用）
}
```

### 内置节点

| 节点 | 职责 |
|------|------|
| `PostSequenceNode` | 顺序执行子节点 |
| `PostCallNode` | 引用 `PostGenerationPreset` 并内联执行 |

### 集成点

`GenerationManagerV2.PostProcessAsync()` 在 LLM 生成完成后调用 `IPostGenerationManager.ExecuteAsync()`，执行 Agent 的 `PostGenerationTreeJson` 树。

### 预设

存储在 KVData `"PostGenerationManager"` / `"post_generation_presets"`，类型为 `PostGenerationPreset`。

---

## Render Modifier 管线

在消息渲染时执行，将原始文本通过 Markdown 渲染、正则替换、样式注入等步骤转换为最终 HTML。

### IRenderModifierNode

```csharp
public interface IRenderModifierNode : ITreeNode
{
    Task<RenderNodeResult> ExecuteAsync(RenderNodeExecutionContext context);
}
```

对标 `IPreGenerationNode`，通过 context 携带所有依赖，返回 `RenderNodeResult`。

### RenderNodeExecutionContext

```csharp
public class RenderNodeExecutionContext
{
    public RenderEnv Env { get; }
}
```

### RenderEnv — 渲染管线环境

```csharp
public class RenderEnv
{
    public string GetContent();                          // 获取当前内容
    public void UpdateContent(string newContent,         // 更新内容，自动记录变更
        string nodeName, string nodeType);

    public List<RenderChangeRecord> ChangeLog { get; }   // 完整变更记录
    public ITreeNodeSerializer Serializer { get; }       // 序列化器
    public IKVDataService KVData { get; }                // KV 存储
    public Chat? Chat { get; }
    public Agent? Agent { get; }
}
```

**关键设计**：节点必须通过 `GetContent()` / `UpdateContent()` 读写内容。`UpdateContent` 自动将变更（节点名、类型、改前、改后）追加到 `ChangeLog`，确保不会遗漏记录。

### RenderNodeResult

```csharp
public class RenderNodeResult
{
    public bool Success { get; set; }
    public string? Code { get; set; }
    public string? Message { get; set; }
    public string? NodeId { get; set; }
    public string? NodeName { get; set; }
    public string Content { get; set; }

    public static RenderNodeResult SuccessResult(string content);
    public static RenderNodeResult Failure(string code, string message, ...);
}
```

对标 `NodeResult`。成功时包含处理后的 `Content`；失败时通过 `Code`/`Message` 描述错误。

### 内置节点

| 节点 | 职责 |
|------|------|
| `RenderSequenceNode` | 顺序管道子节点链，每个子节点执行后调用 `UpdateContent` 记录变更 |
| `MarkdownRenderNode` | Markdown → HTML 渲染（基于 Markdig，可配置 PipeTables） |
| `RegexReplaceNode` | 正则表达式替换（IgnoreCase/Multiline/Singleline） |
| `RenderCallNode` | 引用 `RenderModifierPreset`，通过 `context.Env.Serializer` + `KVData` 加载 |

### 执行示例

```csharp
[NodeInfo("node.my_render", Icon = "⬇", Color = "var(--node-tool)",
    CategoryKeys = ["category.render"])]
public class MyRenderNode : IRenderModifierNode
{
    public string Id { get; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "My Render";

    public Task<RenderNodeResult> ExecuteAsync(RenderNodeExecutionContext context)
    {
        var content = context.Env.GetContent();
        var result = DoSomething(content);
        // 不需要手动记录 — RenderSequenceNode 会在子节点返回后调用 UpdateContent
        return Task.FromResult(RenderNodeResult.SuccessResult(result));
    }
}
```

### 集成点

`MessageDisplayServiceV1.RenderWithDetails()` 调用 `IRenderModifierManager.RenderWithLog()`，获取 `(RenderNodeResult, List<RenderChangeRecord>)`。

- **成功**：直接使用 `result.Content` 作为 HTML
- **失败**：调用 `BuildErrorHtml()` 输出完整 `ChangeLog` + 错误信息

不再有 fallback 到旧 Markdig 管线的逻辑——有问题直接带完整记录报错。

### 预设

存储在 KVData `"RenderModifierManager"` / `"render_modifier_presets"`，类型为 `RenderModifierPreset`。

---

## 自定义节点开发

两种管线的新节点开发流程与 Pre-Generation 相同：

1. 实现 `IPostGenerationNode` 或 `IRenderModifierNode`
2. 标记 `[NodeInfo]` + 属性标记 `[NodeProperty]`
3. 节点自动出现在对应管线的编辑器菜单中

详见 [节点编辑器系统](NodeEditorSystem.md)。

---

## 与旧系统的关系

Render Modifier 管线取代了旧的 `IMessageRenderModifier` 系统：
- 旧的 `MessageDisplayServiceV1.ActivatedModifiers` 列表不再用于渲染
- 旧的 `RegexRenderModifier` 功能由 `RegexReplaceNode` 继承
- 旧的 `AddMessageRenderModifierPopup` / `EditMessageRenderModifierPopup` 已被移除
- 调试模式 (`DebugModeEnabled`) 已被移除——变更记录始终收集，失败时通过 `BuildErrorHtml` 展示
