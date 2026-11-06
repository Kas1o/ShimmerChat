# 代码高亮方案调研（.NET / Blazor Server）

> 目标：给 AI 聊天消息里的围栏代码块（```lang）加语法高亮。
> 落地形态：**独立渲染节点**（不侵入 `MarkdownRenderNode`）。
> 调研时间：2026-11。版本号以调研时 NuGet 上的为准，落地前请再确认一次。

---

## 1. 现状与约束

| 事项 | 现状 |
|------|------|
| Markdown 渲染 | 服务端 Markdig，在 `MarkdownRenderNode` 里 `Markdown.ToHtml()`，管线按配置缓存 |
| 代码块样式 | `ChatViewShell.razor` 内联 `<style>`：`.chat-msg-body pre` 只有背景 + 滚动，**没有任何着色** |
| HTML 注入方式 | `ChatMessageBubble.razor` 的 `MarkupString`，即整段 HTML 直接塞进 DOM，没有 post-render 钩子 |
| 渲染调用频率 | `MessageDisplayServiceV1.RenderWithDetails` **无缓存**；流式期间每来一段增量就整条消息重渲染一次 |
| 主题 | 全部是 `:root` 上的 `--su-*` CSS 变量，切主题只换变量值（`ThemeCss.razor`） |
| 目标框架 | net10.0；Markdig 固定在 **0.41.0**（`ShimmerChat.csproj`、`ShimmerChatBuiltin.csproj`） |
| 部署形态 | Blazor Server + Tauri 桌面壳，要求离线可用、跨平台（`src-tauri`） |

由「整段 HTML 用 `MarkupString` 注入」这一条，基本可以判定：**服务端出高亮 HTML 最省事**，客户端方案需要额外的 DOM 钩子（详见 §5）。

---

## 2. 结论

**推荐：服务端高亮，用 `Meziantou.Framework.SyntaxHighlighting`，做成独立渲染节点（不侵入 `MarkdownRenderNode`，见 §6）。**

- 100+ 语言，覆盖 AI 聊天实际会出现的全部语言（rust / go / bash / yaml / dockerfile / ts / sql / kt …）；
- 零依赖、net10.0 原生，**完全不动 Markdig 版本**；
- 输出 `hljs-*` class，可以直接套现成的 highlight.js 主题，也能映射到 `--su-*` 变量；
- 对不可信输入有 `MatchTimeout` 兜底（AI 输出 = 不可信输入，这点很重要）。

**备选**：`Markdown.ColorCode`（三行接入，但语言只有 24 种，且要求 Markdig ≥0.41.3）；**保真度优先**：`TextMateSharp`（VS Code 级语法，但要自己写 HTML renderer 且带原生依赖）。

---

## 3. 候选对比

| 方案 | 形态 | 语言数 | 输出 | 依赖 | 维护状态 | 适配本项目 |
|------|------|--------|------|------|----------|------------|
| **Meziantou.Framework.SyntaxHighlighting** 1.0.6 | 服务端库，自己接渲染器 | 100+ | `<span class="hljs-*">` | 无 | 很新（1.5K 下载，v1 首版 2026-05） | ★★★★★ |
| **Markdown.ColorCode** 3.0.1 | Markdig 扩展，`.UseColorCode()` | ~24 | `<pre><code>` + span | ColorCode.Core/HTML 2.0.15、Markdig ≥0.41.3 | 活跃（300K 下载，2025-08） | ★★★☆☆ |
| **ColorCode.HTML** 2.0.15（裸用） | 服务端库 | ~24 | 内联样式或 class | 无 | 停更（2023-07） | ★★★☆☆ |
| **TextMateSharp** 2.0.5 / 3.0-preview | 服务端库 + 语法包 | 50+ | 需要自己拼 HTML | Onigwrap（2.x 原生）/ GregsEx（3.0 托管） | 活跃（1.8M 下载） | ★★☆☆☆（工作量大） |
| **highlight.js** 11.12.0（客户端） | JS | 193 | 客户端改 DOM | 无 | 事实标准 | ★★☆☆☆（要 JS 互操作） |
| **Roslyn Classifier** | 服务端库 | 只有 C# | 自定义 | `Microsoft.CodeAnalysis.CSharp`（**项目已引用**） | 官方 | ★★☆☆☆（仅 C#，可作补充） |
| PrismSharp 1.0.0-beta | 服务端库 | 多 | 自定义 | — | beta 且停滞 | ✗ |
| MarkdigExtensions.SyntaxHighlighting / Nodsoft.Markdig.SyntaxHighlighting | Markdig 扩展 | ~24 | span | ColorCode | 低活跃 | ★★☆☆☆ |

---

## 4. 各方案细节

### 4.1 Meziantou.Framework.SyntaxHighlighting（推荐）

```csharp
var html = SyntaxHighlighter.Highlight("public class MyClass { }", "csharp");
// <span class="hljs-keyword">public</span> <span class="hljs-keyword">class</span> ...
```

- 契约：`SyntaxHighlighter.Highlight` / `TryHighlight(code, lang, out html)` / `IsSupported` / `GetSupportedLanguages()`。
- 选项：`ClassPrefix`（默认 `hljs-`）、`IgnoreIllegals`、`MatchTimeout`。
- 语言标识与 highlight.js 对齐，围栏写 `cs` / `c#` / `csharp` / `js` / `py` / `sh` / `yml` 都能识别；未知语言 `TryHighlight` 返回 false，**由调用方降级为纯文本**（不抛异常，正好符合"围栏语言来自模型输出"的不可信场景）。
- 安全：非高亮部分全部 HTML 转义；`MatchTimeout` 防止恶意输入触发正则灾难性回溯（开启后匹配约慢一倍，按需启用）。
- 性能：语法在首次使用时编译并缓存；常驻进程可开 `Meziantou.Framework.SyntaxHighlighting.UseCompiledRegex`，扫描快数倍、启动慢一些。
- 风险：**太新**（总下载 1.5K，v1.0.0 才 2026-05）。作者 meziantou 是 .NET 圈知名维护者（Meziantou.Analyzer 等），代码质量可信，但"出问题没人踩过坑"是真实风险。缓解：把它包在一个薄接口后面（见 §6），将来换库只改一处。

### 4.2 Markdown.ColorCode（最省事的 Markdig 路线，但语言不够）

```csharp
var pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().UseColorCode().Build();
```

- 直接挂在现有 pipeline 上，和 `UsePipeTables` / `UseKatexMath` 并列，改动最小。
- 可选 `HtmlFormatterType.Style`（内联样式）或 `Class`（CSS class）、自定义样式字典、追加语言、默认语言。
- **硬伤：语言覆盖**。ColorCode 的 `LanguageId` 只有 24 种：
  `asax, ashx, aspx, aspx(c#), aspx(vb.net), c#, cpp, css, f#, html, java, javascript, json, typescript, php, powershell, sql, vb.net, xml, koka, haskell, markdown, fortran, python, matlab`
  —— **没有 Rust / Go / Ruby / Bash / YAML / TOML / Dockerfile / Kotlin / Swift / Scala / C**。AI 聊天里这几类代码出现频率很高，会有一大片"语言不支持 → 退回纯文本"。
- 另外一个摩擦点：要求 Markdig ≥ 0.41.3，项目当前是 0.41.0（补丁级升级，风险低，但总不能为了高亮去动版本）。

### 4.3 TextMateSharp（保真度天花板）

- VS Code 同款 TextMate 语法，50+ 语言、22 套主题，AvaloniaEdit / Terminal.Gui 在用。
- 但它只给 **token + scope + 主题颜色**，HTML 要自己拼（遍历 `TokenizeLine` 结果写 span）。也就是说高亮器只完成了一半工作。
- 2.x 依赖 Onigwrap（Oniguruma 原生包装）→ Tauri 三平台要各自带 native 库；3.0.0-preview.1 换成托管的 GregsEx，但**还是 preview**。
- 结论：除非要求"和 VS Code 一模一样的配色"，否则性价比低。

### 4.4 highlight.js（客户端）

- 193 语言、516 主题、自动语言检测，`hljs.highlightAll()` 一句话。
- 但本项目 HTML 是 `MarkupString` 整段注入，**没有 post-render 钩子**。可行做法有两条，都不便宜：
  1. 每条消息渲染完回调 `IJSObjectReference` 扫描 DOM——流式期间每个增量都要重扫，还要处理 Blazor 的 DOM diff 把已高亮节点替换掉的问题（需要按内容哈希跳过已处理块）；
  2. 用 `MutationObserver` 兜底——时序不可控，容易和滚动跟随、气泡动画打架。
- 好处是语言和主题最全、服务端零 CPU。若将来消息量大到服务端扛不住，这是退路。

### 4.5 Roslyn（顺手可用的补充）

`ShimmerChatBuiltin` **已经引用** `Microsoft.CodeAnalysis.CSharp 5.0.0-2.final`，`Classifier.GetClassifiedSpansAsync` 能给出语义级精确的 C# 分类。只适合作为"cs 代码块走 Roslyn、其余走通用高亮"的增强，单独用不够。

---

## 5. 为什么倾向服务端

1. **贴合现有架构**：渲染就是"文本进、HTML 出"的节点管线，高亮天然是其中一个节点。
2. **离线与跨平台**：不引 JS 包，Tauri 桌面壳不用改资源打包，也不受 CSP 影响。
3. **确定性**：同样的输入永远同样的 HTML，便于 `RenderWithDetails` 的调试视图和单元测试。
4. **主题**：`hljs-*` class 映射到 `--su-*` 变量后，切主题自动跟随，不用维护两套配色。

代价是服务端 CPU。注意现有 `RenderWithDetails` 没有缓存，流式时整条消息会反复重渲染 —— 高亮必须配合 §6.6 的策略，否则会明显拖慢流式。

---

## 6. 落地设计：独立渲染节点

### 6.1 形态与代价

`CodeHighlightNode : IRenderModifierNode`，在渲染树里排在 `MarkdownRenderNode` **之后**：输入是 HTML，输出是同一段 HTML，只把 `<code>` 内部文本换成带 `hljs-*` 的 span。

与内嵌方案的差别：

| | 内嵌 MarkdownRenderNode | 独立节点 |
|---|---|---|
| 生效范围 | 所有已有 Agent 立即生效 | 要手动加到渲染树，或改默认预设 |
| 顺序约束 | 无 | 必须在 Markdown 之后（放前面则拿不到 `<pre>`，空转） |
| 可组合性 | 只有一个开关 | 可单独启停、可放两个不同配置、可换实现 |
| HTML 处理 | 不需要，Markdig AST 直接给代码文本 | 需要扫描 HTML |
| 职责边界 | Markdown 节点干两件事 | 各管各的 |
| 性能 | 略好 | 多一次全文扫描（块级缓存可抵消） |

必须接受的两点代价：

1. **默认不生效。** `RenderModifierManager.EnsureDefaultRenderPreset()` 只生成一个 `MarkdownRenderNode`，而已有 Agent 的 `RenderModifierTreeJson` 是持久化的旧树。新增节点不会追溯已存在的会话——用户要在「渲染配置」里自己添加，或者同时把默认预设改成 `MarkdownRenderNode → CodeHighlightNode`（只影响新装/重建预设的 Agent）。
2. **顺序靠人。** 放错位置不会报错（没有 `<pre>` 就是空转），表现为"静默无效果"。不建议为此抛异常——"这条消息没有代码块"是常态，无法与"顺序放错"区分。

### 6.2 节点定义

```csharp
[NodeInfo("node.code_highlight",
    Icon = "🎨",
    Color = "var(--node-config)",
    CategoryKeys = ["category.render"],
    DescriptionKey = "node.code_highlight.desc")]
public class CodeHighlightNode : IRenderModifierNode
{
    public string Id { get; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "Code Highlight";

    /// 不参与着色的语言（逗号/分号/空白分隔，大小写不敏感）
    [NodeProperty("prop.code_highlight.skip_languages", HintKey = "...", Order = 10)]
    public string SkipLanguages { get; set; } = "svg,mermaid";

    /// 单个代码块超过此字符数就不高亮（防超大块拖慢渲染），0 = 不限制
    [NodeProperty("prop.code_highlight.max_chars", HintKey = "...", Order = 20)]
    public int MaxBlockChars { get; set; } = 20_000;

    /// 正则匹配超时（毫秒），0 = 不启用。AI 输出属于不可信输入
    [NodeProperty("prop.code_highlight.match_timeout", HintKey = "...", Order = 30)]
    public int MatchTimeoutMs { get; set; } = 1000;

    /// 没有语言标识的代码块按此语言高亮；空 = 不高亮
    [NodeProperty("prop.code_highlight.default_language", HintKey = "...", Order = 40)]
    public string DefaultLanguage { get; set; } = "";

    public void Execute(RenderNodeExecutionContext context) { /* 见 §6.3 */ }
}
```

`Execute` 只做三件事：扫块 → 逐块高亮 → 有变化时**一次性** `env.UpdateContent(...)`（ChangeLog 只留一条记录，而不是每块一条）。

**跳过列表的语义**（`SkipLanguages`）：

- 命中的块**一个字节都不改**——`<pre><code class="language-svg">` 连同 class 原样保留，后续的 SVG / Mermaid 节点仍能按 class 找到并接管；
- 按**围栏语言名**做大小写不敏感的**精确**匹配，不展开别名（写 `svg` 不会顺带跳过 `xml`）；
- 默认 `svg,mermaid`：`svg` 本身是 Meziantou 支持的别名（走 XML 语法），不跳过就会被当成 XML 染色——这正是"应该跳"的典型场景；
- 空字符串 = 不跳过任何语言。

### 6.3 HTML 扫描

**不引入 AngleSharp / HtmlAgilityPack**，理由：

- 输入就是 Markdig 的输出，结构可预测；代码正文里的 `<` 已被转义成 `&lt;`，所以 `</code>`、`</pre>` 不可能出现在代码内容里——这里的扫描是**确定性的**，不是启发式猜测；
- AngleSharp 会把整篇 HTML 重新序列化（属性引号、空白、自闭合写法都可能变），既让 ChangeLog 的 diff 失真，也可能破坏下游按字符串匹配的节点（比如 `RegexReplaceNode`）。

扫描规则：

1. 找 `<pre`（大小写不敏感，且前面不是字母，避免误匹配 `<prefix`），配对 `</pre>`；
2. 其内找 `<code ...>` 与 `</code>`；没有 `<code>` 的 `<pre>` 原样跳过（保证不破坏别的 HTML 结构）；
3. 从 `<code>` 的 `class` 取 `language-xxx` / `lang-xxx`，按空白截断取第一个（` ```csharp ` 之外再多写东西也不怕）；
4. `WebUtility.HtmlDecode` 解码内文 → `TryHighlight` → 用返回值替换内文；**`<pre>` 与 `<code>` 开标签原样保留**，只替换 `<code>` 与 `</code>` 之间的文本；
5. 内文里已出现 `hljs-` 就跳过该块——节点被加两次也不会重复包裹（幂等）。

实现上用一个 `StringBuilder` 单次扫描重建即可，不要用正则去匹配整段 `<pre>...</pre>`（嵌套/属性/大小写都是坑，而且回溯风险更大）。

### 6.4 安全与错误策略

- **语言未知或为空**：保留原文（已转义），不高亮。这不是错误，是"无需处理"。
- **高亮超时**：库按文档返回转义后的纯文本，该块保持无色，不抛异常。
- **其他异常不吞**：直接向上抛，`RenderModifierManager` 包成 `RenderNodeException(ServiceError)`，`MessageDisplayServiceV1` 会渲染错误面板并带上完整 ChangeLog。符合项目「严格报错」的约定。
- **绝不复用解码后的原文**：输出只来自库的转义结果，避免二次注入。

### 6.5 样式

节点只产出 `hljs-*` class，颜色必须另有 CSS。`ShimmerChatBuiltin` 没有 manifest、也没有 static 目录（`/pluginstatic/...` 是外部插件的能力，见 `Docs/PluginDevelopment.md`），所以样式放宿主：新增 `ShimmerChat/wwwroot/code-highlight.css` 并在 `App.razor` 里 `<link>`（和 `katex.min.css` 同一个位置）。

```css
.chat-msg-body pre code .hljs-keyword,
.chat-msg-body pre code .hljs-built_in { color: var(--su-accent); }
.chat-msg-body pre code .hljs-string,
.chat-msg-body pre code .hljs-attr   { color: var(--su-success); }
.chat-msg-body pre code .hljs-comment{ color: var(--su-text-2); font-style: italic; }
.chat-msg-body pre code .hljs-number,
.chat-msg-body pre code .hljs-literal{ color: var(--su-warning); }
.chat-msg-body pre code .hljs-title,
.chat-msg-body pre code .hljs-function { color: var(--su-info); }
.chat-msg-body pre code .hljs-variable,
.chat-msg-body pre code .hljs-params { color: var(--su-text-0); }
```

顺手修一个现存的小毛病：`.chat-msg-body code` 的 `background` / `padding` 会作用到 `pre > code` 上，导致围栏代码块出现"内层再套一层底色和边距"。改成 `.chat-msg-body :not(pre) > code`。

若希望配色更讲究，可在 `Theme` 模型里加 8 个左右的 `syntax*` 字段（带默认值，老主题 JSON 反序列化仍可用），但那是二期。

### 6.6 性能与流式

1. **块级缓存**：静态 `ConcurrentDictionary<(string Lang, string Code), string>`，设上限（如 512 条，超出清空）。流式时除最后一块外都能命中，成本从"整条消息"降到"正在增长的那一块"。
2. **拿不到流式状态**：`RenderEnv` 只有 `Chat` / `Agent`，没有"当前渲染的是哪条消息、是否正在生成"。所以独立节点**做不到**"生成中先纯文本、完成后才着色"这种降级，只能靠 `MaxBlockChars` + 缓存兜住。这是外置方案的已知取舍（内嵌方案可以顺手做到）。
3. 全文扫描是 O(n) 的 `IndexOf`，相对高亮本身可忽略。

### 6.7 测试

- 有语言 → 输出含 `hljs-keyword`（含"Markdown 渲染 → 高亮"的端到端）；
- 无语言 / 未知语言 → 原文不变且保持转义；
- 跳过列表命中（含大小写、空格/分号分隔）→ 原样不动；清空跳过列表则正常着色；
- 一个块被跳过不影响其他块；
- 代码里含 `<script>`、`&` → 不逃逸；
- 同一段 HTML 过两遍 → 结果不变（幂等）；
- `<pre>` 内没有 `<code>` / `<pre>` 里有前后缀文本 → 结构原样保留；
- 超过 `MaxBlockChars` → 原样不动，0 表示不限制；
- 文档里没有代码块 → 不产生 ChangeLog 记录。

### 6.8 代码块工具条（CodeBlockActionsNode）

再加一个独立节点，给每个代码块补上语言标签与「复制 / 下载」。它和 `CodeHighlightNode` **互不依赖**：一个改 `<pre>` 外层，一个只改 `<code>` 内文，谁先谁后都对。

产物结构：

```html
<div class="code-block">
  <div class="code-block-head">
    <span class="code-block-lang">csharp</span>
    <span class="code-block-tools">
      <button type="button" class="code-block-btn" data-code-action="copy"    data-code-copied="已复制">…复制</button>
      <button type="button" class="code-block-btn" data-code-action="download" data-code-filename="snippet.cs">…下载</button>
    </span>
  </div>
  <pre data-code-block="1"><code class="language-csharp">…</code></pre>
</div>
```

三个关键决定：

1. **节点只产出结构，行为交给宿主脚本**。Blazor 用 `MarkupString` 注入的 HTML 挂不了 `@onclick`，所以 `wwwroot/js/code-block-actions.js` 用**事件委托**监听 `document`——流式生成时 DOM 反复被替换也不受影响。本节点因此不依赖任何 JS 互操作代码。
2. **`data-code-block="1"` 写在 `<pre>` 上**，作为幂等标记：节点被加两次也不会套两层。其余属性原样保留（`<pre class="extra">` → `<pre data-code-block="1" class="extra">`）。
3. **文案走 `RenderEnv.Loc`**。为此给 `RenderEnv` 加了可选的 `ILocService? Loc`（`RenderModifierManager` 注入后传入），节点取不到时回退英文 `Copy` / `Download` / `Copied`。这也让后续渲染节点有了本地化能力。

复制实现两条路：`navigator.clipboard`（需要安全上下文）失败时退回 `execCommand('copy')`——局域网 http 访问时前者直接不可用，这个回退是必需的。下载走 Blob + `URL.createObjectURL`，文件名按语言推断扩展名，未知语言 `.txt`。

### 6.9 落地状态

| 文件 | 变更 |
|------|------|
| `ShimmerChatBuiltin/Misc/Node/Render/CodeBlockHtmlScanner.cs` | 两个节点共用的 `<pre><code>` 扫描器 |
| `ShimmerChatBuiltin/Misc/Node/Render/CodeHighlightNode.cs` | 高亮节点 |
| `ShimmerChatBuiltin/Misc/Node/Render/CodeBlockActionsNode.cs` | 工具条节点 |
| `ShimmerChatBuiltin/ShimmerChatBuiltin.csproj` | 加 `Meziantou.Framework.SyntaxHighlighting` 1.0.6 |
| `ShimmerChatBuiltin/Locales/{zh-CN,en-US}.json` | 节点、属性与按钮文案 |
| `ShimmerChatLib/Generation/IRenderModifierNode.cs` | `RenderEnv.Loc`（可选，向后兼容） |
| `ShimmerChat/Singletons/RenderModifierManager.cs` | 注入 `ILocService` 并传入 `RenderEnv` |
| `ShimmerChat/wwwroot/code-highlight.css` / `code-block-actions.css` + `App.razor` | 配色与工具条样式 |
| `ShimmerChat/wwwroot/js/code-block-actions.js` | 复制 / 下载（事件委托） |
| `ShimmerChatLib/Components/ChatViewShell.razor` | `code` 规则改为 `:not(pre) > code`，修掉围栏块双层底色 |
| `ShimmerChatBuiltin.Tests/Render/{CodeHighlightNodeTests,CodeBlockActionsNodeTests}.cs` | 16 + 14 条测试 |

**默认预设未改动**：`EnsureDefaultRenderPreset()` 仍然只生成 `MarkdownRenderNode`，两个节点都需要用户自己加到渲染树。若要默认启用，改成 `MarkdownRenderNode → CodeHighlightNode → CodeBlockActionsNode` 即可（只影响新建/重建预设的 Agent）。

---

## 7. 风险清单

| 风险 | 影响 | 处理 |
|------|------|------|
| Meziantou 包太新 | 出现未知 bug 无人踩坑 | 包薄接口（`ICodeHighlighter`），随时可换 ColorCode/TextMateSharp |
| 正则灾难性回溯 | 恶意/畸形代码拖死请求 | 开 `MatchTimeout`（1s），超时返回纯文本 |
| 流式重渲染开销 | 输入卡顿、CPU 飙高 | 块级缓存 + `MaxBlockChars`（独立节点拿不到流式状态，无法做"生成中降级"） |
| 节点放错位置 | 静默无效果 | 文档标注"必须在 Markdown 之后"；节点编辑器可考虑顺序提示 |
| 默认预设不含新节点 | 老 Agent 看不到效果 | 用户手动添加，或更新 `EnsureDefaultRenderPreset` |
| 未知语言围栏 | 无高亮 | 明确降级为纯文本，不吞异常 |
| 主题可读性 | 暗色主题下对比度不足 | 用 `--su-*` 变量映射；不合意时允许 `CustomCss` 覆盖 |

---

## 8. 参考

- [Meziantou.Framework.SyntaxHighlighting（NuGet）](https://www.nuget.org/packages/Meziantou.Framework.SyntaxHighlighting) / [文档](https://github.com/meziantou/Meziantou.Framework/tree/main/docs/SyntaxHighlighting)
- [Markdown.ColorCode（NuGet）](https://www.nuget.org/packages/Markdown.ColorCode) / [仓库](https://github.com/wbaldoumas/markdown-colorcode)
- [ColorCode-Universal（NuGet: ColorCode.Core / ColorCode.HTML）](https://www.nuget.org/packages/ColorCode.Core)
- [TextMateSharp（NuGet）](https://www.nuget.org/packages/TextMateSharp) / [仓库](https://github.com/danipen/TextMateSharp)
- [highlight.js](https://highlightjs.org/)
- [Roslyn Classifier.GetClassifiedSpansAsync](https://learn.microsoft.com/dotnet/api/microsoft.codeanalysis.classification.classifier.getclassifiedspansasync)
