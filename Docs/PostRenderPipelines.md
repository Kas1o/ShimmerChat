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
    public string ResponseText { get; set; }                   // LLM 原始响应文本（节点可修改）
    public ChatMessage ResponseMessage { get; }                // LLM 完整响应消息（含 toolCalls、thinking）
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
| `PostDebugOutputNode` | 将 LLM 响应写入调试输出 |

### 集成点

`GenerationManagerV2.PostProcessAsync()` 在 LLM 生成完成后调用 `IPostGenerationManagerService.ExecuteAsync()`，执行 Agent 的 `PostGenerationTreeJson` 树。

### 预设

存储在 KVData `"PostGenerationManager"` / `"post_generation_presets"`，类型为 `PostGenerationPreset`。

---

## Render Modifier 管线

在消息渲染时执行，将原始文本通过 Markdown 渲染、正则替换、样式注入等步骤转换为最终 HTML。

### IRenderModifierNode

```csharp
public interface IRenderModifierNode : ITreeNode
{
    void Execute(RenderNodeExecutionContext context);
}
```

注意：`IRenderModifierNode.Execute` 是**同步 void 方法**，与前两个管线的 `async Task<NodeResult>` 模式不同。失败时通过抛出 `RenderNodeException` 来报告错误。

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

### RenderNodeException — 错误处理

失败时节点抛出 `RenderNodeException`（而非返回结果对象）。管线层会捕获该异常并提取 `Code`、`NodeId`、`NodeName` 用于错误展示。

```csharp
public class RenderNodeException : Exception
{
    public string Code { get; }
    public string? NodeId { get; }
    public string? NodeName { get; }
}
```

### 内置节点

| 节点 | 职责 |
|------|------|
| `RenderSequenceNode` | 顺序执行子节点，各子节点通过 `GetContent`/`UpdateContent` 读写内容 |
| `MarkdownRenderNode` | Markdown → HTML 渲染（基于 Markdig，可配置 PipeTables） |
| `RegexReplaceNode` | 正则表达式替换（IgnoreCase/Multiline/Singleline） |
| `RenderCallNode` | 引用 `RenderModifierPreset`，通过 `context.Env.Serializer` + `KVData` 加载 |

### 执行示例

```csharp
[NodeInfo("node.my_render", Icon = "⬇", Color = "var(--node-tool)",
    CategoryKeys = ["category.render"])]
public class MyRenderNode : IRenderModifierNode
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "My Render";

    public void Execute(RenderNodeExecutionContext context)
    {
        var content = context.Env.GetContent();
        var result = DoSomething(content);
        context.Env.UpdateContent(result, Name, GetType().Name);
    }
}
```

### 集成点

`MessageDisplayServiceV1.RenderWithDetails()` 调用 `IRenderModifierManager.RenderWithLog()`，返回 `(string Content, List<RenderChangeRecord> ChangeLog)` 元组。

- **成功**：直接使用 `Content` 作为 HTML
- **失败**：调用 `BuildErrorHtml()`（私有方法）输出完整 `ChangeLog` + 错误信息

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
- 变更记录始终收集，失败时通过 `BuildErrorHtml` 展示完整变更链
