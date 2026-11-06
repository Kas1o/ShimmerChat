# Post-Generation & Render Modifier 管线

ShimmerChat 2.0 新增两个后处理管线，分别在 LLM 响应后和消息渲染时执行。

---

## Post-Generation 管线

在 LLM 生成响应后执行。它不只是"改一段文本"——后处理树就是**回合控制器**：
节点可以改写本轮响应、直接增删改对话消息（并落库），并决定是否继续下一轮。

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
    // 本轮输出（权威来源：提交阶段会用它覆盖 CurrentMessage 的当前版本）
    public string ResponseText { get; set; }
    public ChatMessage ResponseMessage { get; private set; }
    public void SetResponse(ChatMessage message);              // 整体替换响应（含 toolCalls / thinking）

    // 回合上下文
    public int RoundIndex { get; }                             // 0-based，副作用节点可据此只在首轮执行
    public Message? CurrentMessage { get; }                    // 本轮 assistant 的 Message 活对象
    public IReadOnlyList<IToolV2> Tools { get; }               // 本轮可用工具

    // 直接操作对话
    public Chat Chat { get; }                                  // = Persistent.Chat
    public IMessageStoreService? MessageStore { get; }         // null = 虚拟会话，不落库

    // 循环控制
    public PostLoopDirective LoopDirective { get; set; }       // 默认 Stop
    public bool ToolCallsHandled { get; set; }
    public bool ChatMutated { get; set; }

    public IReadOnlyList<ContextSegment> PreFragments { get; }
    public Dictionary<string, object> SharedState { get; }     // 跨轮复用
    public PersistentEnv Persistent { get; }
    public ITreeNodeSerializer Serializer { get; set; }
}
```

**节点直接操作对话**，没有中间提交通道：

```csharp
var env = context.Env;
var msg = new Message
{
    message = new ChatMessage { Content = "text" },
    sender = Sender.User,
    timestamp = DateTime.Now
};
env.Chat.AddMessage(msg);
if (env.MessageStore is { } store)
    env.Chat.SaveMessage(store, msg);
env.ChatMutated = true;
```

`PostChat` 是同一件事的薄封装（负责落库与顺序号），可以绕过：

```csharp
PostChat.Append(env, new ChatMessage { Content = "text" }, Sender.User);
PostChat.InsertAfter(env, anchor, new ChatMessage { Content = "x" }, Sender.AI);
PostChat.Replace(env, target, new ChatMessage { Content = "y" });
PostChat.Remove(env, target);
```

### 循环控制

整棵树执行完毕后，宿主按 `LoopDirective` 决定是否再生成一轮：

| 指令 | 行为 |
|------|------|
| `Stop`（默认） | 本轮结束，生成完成 |
| `Continue` | 重建前生成环境（读取最新的 Chat.Messages）后再次请求模型 |

**工具调用不会被自动执行。** 它由 `PostToolCallNode` 完成——后处理树里没有这个节点，
本轮的工具调用就不会被执行，宿主会记录告警并强制结束循环（继续循环只会重复拿到同样的调用）。

安全网：`ResponseMessage.toolCalls` 非空且 `ToolCallsHandled == false` → `LogWarning` + `DebugOutput` + 强制 Stop。

### 内置节点

| 节点 | 职责 |
|------|------|
| `PostSequenceNode` | 顺序执行子节点（支持 Repeat） |
| `PostCallNode` | 引用 `PostGenerationPreset` 并内联执行 |
| `PostToolCallNode` | **执行本轮工具调用**，结果写入对话并置 `Continue` |
| `PostLoopDirectiveNode` | 显式设置循环指令 |
| `PostMessageNode` | 追加消息，或覆盖本轮响应文本 |
| `PostIfNode` | 按响应文本 / 工具名 / 共享状态 / KV 条件分支 |
| `PostSubAgentNode` | 用子智能体处理响应文本（内部跑独立的轮次循环） |
| `PostDebugOutputNode` | 将 LLM 响应写入调试输出 |
| `AutoChatNameNode` | 根据对话内容自动命名会话 |

默认预设 `__default__` 为：`PostDebugOutputNode` → `AutoChatNameNode` → `PostToolCallNode`。

### 消息顺序契约

消息在会话内的位置由 `Message.Order` 决定，**`timestamp` 只用于展示，不参与排序**。

- `Chat.AddMessage` 追加时分配 `末条 Order + 1`；
- `Chat.InsertMessage(store, index, msg)` 插入后重编号并全量持久化（顺序权威来自 `Chat.Messages`）；
- store 排序规则：该会话存在任何 `Order < 0` 的消息时整组回退到 `timestamp`（迁移前行为），否则按 `Order`；
- 老数据在 `Chat.LoadAllMessages` 时懒迁移（补号 + 全量回写），只迁移被打开的会话。

节点若绕过 `Chat` API 直接改 `ObservableCollection`，回合末的 `Chat.ValidateOrder()` 会告警；`Chat.NormalizeOrder()` 用于修复，不会自动重排。

### 集成点

`GenerationManagerV2.MainLoopHost.OnAssistantCompleteAsync()` 每轮：

1. 执行后处理树 → `PostPipelineResult`
2. 用 `ResponseMessage` 回写本轮消息，并提交节点追加的消息
3. 顺序校验 + 消息数量与存储一致性校验（只告警，不自动修复）
4. 按 `LoopDirective` 决定重建环境并继续，或结束

### 预设

存储在 KVData `"PostGenerationManager"` / `"post_generation_presets"`，类型为 `PostGenerationPreset`。

> **破坏性变更（2.2）**：工具调用处理从 `ToolCallLoop` 下沉到 `PostToolCallNode`。
> 旧的 Agent 若自定义了后处理树且其中没有工具节点，工具调用将不再执行（宿主会告警并停止循环）。
> 默认预设已包含该节点，未自定义后处理树的 Agent 不受影响。


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
    public ILocService? Loc { get; }                     // 本地化（宿主未提供时为 null，节点需自带回退文案）
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
| `MarkdownRenderNode` | Markdown → HTML 渲染（基于 Markdig，可配置 PipeTables 与数学公式） |
| `CodeHighlightNode` | 给围栏代码块加语法高亮，**必须排在 Markdown 渲染之后**；可配置跳过着色的语言（默认 `svg,mermaid`）与最大块长度 |
| `CodeBlockActionsNode` | 给代码块加语言标签与复制 / 下载按钮（行为在 `wwwroot/js/code-block-actions.js`，事件委托）；与高亮节点先后顺序无关 |
| `RegexReplaceNode` | 正则表达式替换（IgnoreCase/Multiline/Singleline） |
| `RenderCallNode` | 引用 `RenderModifierPreset`，通过 `context.Env.Serializer` + `KVData` 加载 |

### 数学公式（KStyleMath）

`MarkdownRenderNode` 的公式渲染由 [KStyleMath](https://github.com/Kas1o/KStyleMath)（KaTeX 0.19.1 的纯 C# 移植，
仓库内以 git 子模块引入）承担，无 JS 运行时、无浏览器依赖：

| 属性 | 行为 |
|------|------|
| `EnableMath`（默认开） | 识别 `$...$`、`$$...$$`、`\(...\)`、`\[...\]` 并渲染为公式 |
| `MathOutput` | `MathML`（默认）：只输出 MathML，浏览器原生渲染，不需要额外样式；`KatexHtml`：输出与 `katex.renderToString` 一致的 HTML + MathML，需要 `katex.min.css` |

实现要点：

- 公式在 **Markdig 内联解析阶段**替换为 `HtmlInline`，因此 Markdown 不会破坏 LaTeX 里的 `_`、`*`、`\\` 等字符；
  公式节点无需与 Markdown 节点排序配合，单节点即可完成。
- 代码块与代码段由 Markdig 自己的解析器先行接管，其中的 `$` 不参与公式解析；`\$` 由转义解析器处理。
- 行内公式沿用 KaTeX auto-render 的约定：定界符内侧不能是空白，避免把 “$5 and $10” 这类金额当成公式；
  定界符未闭合时整段原样保留（流式输出中途不会出现半截公式）。
- 解析失败按 KaTeX 的约定产出 `katex-error` 标记（红字 + `title` 里的错误详情），不做静默降级。
- 渲染结果按（形态, 行内/块级, LaTeX）缓存，避免消息重渲染时重复解析。

`KatexHtml` 形态的样式：`katex.min.css` 与字体已在 `ShimmerChat/wwwroot/katex/` 下，
在主题的 `CustomCss` 里写一行即可启用（`ThemeCss` 会把自定义 CSS 放进独立的 `<style>`，`@import` 因此生效）：

```css
@import url("/katex/katex.min.css");
```

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
