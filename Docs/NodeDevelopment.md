# 生成节点与编辑器开发指南

本文档覆盖 `IGenerationNode` 的完整开发流程，包括节点实现、属性编辑器、自定义编辑器、容器子列表、以及拖拽排序系统的集成。

> 插件项目搭建、本地化、部署等基础内容见 [插件开发指南](PluginDevelopment.md)。

---

## 目录

1. [节点基础](#1-节点基础)
2. [三个 Attribute](#2-三个-attribute)
3. [属性编辑器](#3-属性编辑器)
4. [自定义编辑器](#4-自定义编辑器)
5. [容器节点与子列表](#5-容器节点与子列表)
6. [拖拽排序系统](#6-拖拽排序系统)
7. [关键上下文对象](#7-关键上下文对象)
8. [错误码参考](#8-错误码参考)

---

## 1. 节点基础

### 接口

```csharp
using ShimmerChatLib.Generation;

public interface IGenerationNode
{
    string Id { get; }               // 唯一标识，建议 = Guid.NewGuid().ToString()
    string Name { get; set; }        // 用户自定义显示名称
    Task<NodeResult> ExecuteAsync(NodeExecutionContext context);
}
```

### 最小实现

```csharp
[NodeInfo("node.my_fragment", Icon = "✦", Color = "#9060e0",
    CategoryKeys = ["category.content"])]
public class MyFragmentNode : IGenerationNode
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "My Fragment";

    [NodeProperty("prop.my_fragment.text", HintKey = "prop.my_fragment.text.hint", Order = 0)]
    public string Text { get; set; } = "";

    public Task<NodeResult> ExecuteAsync(NodeExecutionContext context)
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

`GenerationNodeSerializer` 通过 `IPluginLoaderService.GetImplementingTypes(typeof(IGenerationNode))` 扫描所有程序集（Lib + Builtin + 宿主 + 插件）中的 `IGenerationNode` 实现。`INodeTypeCatalog` 在此基础上构建节点类型目录（`NodeTypeMetadata`），供添加节点菜单使用。**只要实现了接口并标记了 `[NodeInfo]`，节点就会自动出现在编辑器的添加菜单中，无需额外注册。**

### 序列化

节点树通过 Newtonsoft.Json + `TypeNameHandling.Objects` 序列化。`Id` 属性用于 Blazor `@key` 和环路检测；`Name` 是用户可编辑的显示名。**不要给 `Id` 加 `[NodeProperty]`** —— 它由系统自动管理。

---

## 2. 三个 Attribute

### NodeInfoAttribute — 类级元数据

```csharp
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public class NodeInfoAttribute : Attribute
{
    public string LabelKey { get; }          // 必填，本地化 Key
    public string Icon { get; init; }        // 默认 "●"
    public string Color { get; init; }       // CSS 颜色值，如 "#9060e0"
    public string[] CategoryKeys { get; init; }  // 斜杠分层，如 ["category.flow", "sub.branch"]
    public string? DescriptionKey { get; init; }  // 可选描述
}
```

- `LabelKey`、`DescriptionKey` 均为本地化 Key，UI 通过 `ILocService` 翻译。
- `CategoryKeys` 支持多级分类：`["category.flow", "branch"]` → 节点选择器中显示为 `Flow > Branch`。
- `Color` 控制节点卡片左侧色条。

### NodePropertyAttribute — 属性级元数据

```csharp
[AttributeUsage(AttributeTargets.Property)]
public class NodePropertyAttribute : Attribute
{
    public string LabelKey { get; }       // 必填，本地化 Key
    public string? HintKey { get; init; } // Tooltip 本地化 Key
    public int Order { get; init; }       // 排序，越小越靠前
}
```

用于 `GenericNodeEditor` 自动生成编辑表单。**所有需要在编辑器中显示的属性都必须标记此 Attribute**，包括标量类型、`List<IGenerationNode>` 和 `IGenerationNode`。

支持的类型：

| 属性类型 | 表单控件 |
|----------|---------|
| `string` | `<input type="text">` |
| `int` | `<input type="number">` |
| `float` | `<input type="number" step="0.1">` |
| `bool` | `<input type="checkbox">` |
| `enum` | `<select>` 下拉框 |
| `List<IGenerationNode>` | 子节点列表 + DropStrip |
| `IGenerationNode` | 单节点槽 |

### NodeEditorAttribute — 自定义编辑器

```csharp
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public class NodeEditorAttribute : Attribute
{
    public Type EditorType { get; }
}
```

指定自定义 Blazor 编辑器组件。详见 [第 4 节](#4-自定义编辑器)。

---

## 3. 属性编辑器

### 自动编辑器 (GenericNodeEditor)

不需要自定义编辑器时，`GenericNodeEditor<T>` 通过反射自动生成表单：扫描所有 `[NodeProperty]` 标记的属性，根据类型渲染对应控件（文本输入、数字、复选框、下拉框、子节点列表、单节点槽）。

**这是默认行为。** 只要你的节点只包含标量属性 + 子节点列表，直接用自动编辑器即可，无需写任何 UI 代码。

### 编辑器渲染流程

```
TreeEditor
  ├── .tn-header (标题栏: 图标 + 标签 + 名称输入框 + 操作按钮，点击折叠/展开)
  └── .tn-body (展开时)
        ├── 有 [NodeEditor] → DynamicComponent 加载自定义编辑器
        └── 无 [NodeEditor] → NodeBodyRenderer → GenericNodeEditor<T>
              ├── [NodeProperty] 标量属性 → 表单控件
              ├── [NodeProperty] List<IGenerationNode> → ChildListEditor
              │     ├── NodePickerMenu (添加节点菜单)
              │     ├── DropStrip × (插入点)
              │     └── TreeEditor × (子节点卡片，可拖拽排序)
              └── [NodeProperty] IGenerationNode → SlotEditor
                    ├── NodePickerMenu (添加节点菜单)
                    └── TreeEditor (槽位节点，可拖拽移出)
```

所有节点（无论是否包含子节点列表）都可以通过点击 header 折叠/展开。

---

## 4. 自定义编辑器

### 何时需要

- 属性需要特殊 UI（搜索、列表选择、文件路径选择等）
- 需要访问 `IKVDataService`、`IToolRegistry` 等服务
- 属性的编辑逻辑超出简单 `<input>` 范围

### 实现步骤

**步骤 1**: 标记节点

```csharp
[NodeInfo("node.my_complex", Icon = "⚙", Color = "#d0a040")]
[NodeEditor(typeof(MyComplexNodeEditor))]   // ← 指定编辑器
public class MyComplexNode : IGenerationNode
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "My Complex";

    [NodeProperty("prop.my_complex.url", Order = 0)]
    public string Url { get; set; } = "";

    [NodeProperty("prop.my_complex.children")]
    public List<IGenerationNode> Nodes { get; set; } = new();

    public Task<NodeResult> ExecuteAsync(NodeExecutionContext context) { ... }
}
```

**步骤 2**: 创建编辑器组件（与节点类放在同一程序集中）

```razor
@* MyComplexNodeEditor.razor *@
@using ShimmerChatLib.Components

<div class="my-editor">
    <div class="ge-field">
        <label class="ge-label">URL</label>
        <div class="ge-value">
            <input class="ge-input" @bind="Node.Url" @bind:event="oninput" />
        </div>
    </div>

    @* 子节点列表：使用 ChildListEditor，自动获得添加/删除/排序/拖拽/粘贴 *@
    <ChildListEditor Label="Children"
                     ChildNodes="Node.Nodes"
                     Depth="Depth"
                     CopyMe="CopyMe" />
</div>

@code {
    [Parameter] public MyComplexNode Node { get; set; } = default!;
    [Parameter] public int Depth { get; set; }
    [Parameter] public Action<IGenerationNode>? CopyMe { get; set; }
}
```

自定义编辑器**不需要**发出任何变更通知。所有修改直接作用在 `Node` 对象上，父级在保存时统一序列化。

### 复用组件

自定义编辑器可以直接使用以下预置组件，避免手写子列表和拖拽逻辑：

| 组件 | 用途 | 关键参数 |
|------|------|---------|
| `ChildListEditor` | 子节点列表编辑（添加/删除/排序/拖拽/粘贴） | `ChildNodes`, `Depth`, `CopyMe` |
| `SlotEditor` | 单节点槽编辑（添加/删除/拖拽/粘贴） | `SlotNode`, `Depth`, `OnSet`, `OnClear`, `CopyMe` |
| `NodePickerMenu` | 添加节点菜单（分类导航 + 搜索） | `OnSelect`, `OnClose` |
| `DropStrip` | 拖拽放置条 | `TargetList`, `InsertIndex` |

通常你只需要 `ChildListEditor` 和 `SlotEditor`——它们内部已集成 `NodePickerMenu` 和 `DropStrip`。仅在需要完全自定义拖拽布局时才直接使用 `DropStrip`。

### 编辑器参数约定

`TreeEditor` 通过 `DynamicComponent` 传递以下参数给自定义编辑器：

| 参数名 | 类型 | 是否自动注入 | 说明 |
|--------|------|:---:|------|
| `Node` | 具体节点类型 | 是 | 由 `DynamicComponent` 按编辑器声明的 `[Parameter]` 类型传入 |
| `Depth` | `int` | 是 | 当前缩进层级 |
| `CopyMe` | `Action<IGenerationNode>?` | 是 | 复制到剪贴板回调 |

编辑器只需声明自己需要的参数。Blazor 的 `DynamicComponent` 会按名称匹配传入，未声明的参数会被忽略。修改直接作用在 `Node` 对象上，持久化由外部在保存时统一完成。拖拽的跨组件通信通过 `CascadingValue` 共享 `TreeDragContext` 实例实现，无需逐层传递回调。

### 样式参考

自定义编辑器应使用 `node-editor.css` 中定义的全局类名保持风格一致：

| 类名 | 用途 |
|------|------|
| `.ge-field` | 属性行容器 (flex) |
| `.ge-label` | 属性标签 |
| `.ge-value` | 属性值区域 |
| `.ge-input` | 文本输入框 |
| `.ge-select` | 下拉框 |
| `.ge-check` | 复选框 |
| `.ge-children` | 子节点列表容器 |
| `.ge-children-header` | 子节点列表标题 |
| `.tn-child-row` | 子节点行 |
| `.tn-child-actions` | 子节点操作按钮列 |
| `.tn-child-tree` | 子节点 TreeEditor 区域 |
| `.tn-drop-strip` / `.tn-drop-strip-active` | 放置条（由 `DropStrip` 组件管理） |
| `.tn-btn-*` | 按钮系列 (add, del, copy, paste, move) |

---

## 5. 容器节点与子列表

### 声明子节点列表

```csharp
[NodeInfo("node.my_sequence", Icon = "☰", Color = "#60c060",
    CategoryKeys = ["category.flow"])]
public class MySequenceNode : IGenerationNode
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "My Sequence";

    [NodeProperty("prop.my_sequence.nodes")]
    public List<IGenerationNode> Nodes { get; set; } = new();

    public async Task<NodeResult> ExecuteAsync(NodeExecutionContext context)
    {
        foreach (var child in Nodes)
        {
            var result = await child.ExecuteAsync(context);
            if (!result.Success) return result;
        }
        return NodeResult.SuccessResult();
    }
}
```

只要声明了 `[NodeProperty]` 标记的 `List<IGenerationNode>` 属性，`GenericNodeEditor` 通过 `ChildListEditor` 自动提供：

- 子节点列表渲染（递归 `TreeEditor`）
- **添加按钮** (+)：弹出 `NodePickerMenu`（分类导航 + 搜索）
- **粘贴按钮** (📋)：粘贴剪贴板中的节点
- **上下箭头** (⇧⇩)：手动重排序
- **删除按钮** (✕)：移除子节点
- **拖拽排序**：通过 `DropStrip` 插入点（见 [第 6 节](#6-拖拽排序系统)）

自定义编辑器中也可直接使用 `ChildListEditor`，无需手写上述逻辑。

### 单节点槽

```csharp
[NodeProperty("prop.if_node.then")]
public IGenerationNode? Then { get; set; }

[NodeProperty("prop.if_node.else")]
public IGenerationNode? Else { get; set; }
```

`[NodeProperty]` 标记的 `IGenerationNode` 属性通过 `SlotEditor` 渲染为"单节点槽"：有节点时显示 `TreeEditor`，无节点时显示添加按钮。槽支持拖放和粘贴（见下节）。

自定义编辑器中也可直接使用 `SlotEditor`，通过 `OnSet` / `OnClear` 回调绑定属性写入。

---

## 6. 拖拽排序系统

### 架构概览

```
CascadingValue<TreeDragContext>          ← 全局级联，任何组件都能收到
  ├── TreeEditor (.tn-header)            ← 拖拽源
  │     ParentList / OnDragRemove         ← 拖动时移除节点的依据
  ├── DropStrip                          ← 放置目标（通用插入点）
  │     TargetList / InsertIndex          ← 插入目标列表和位置
  ├── ChildListEditor                    ← 子列表编辑器，内部组合 DropStrip + TreeEditor
  ├── SlotEditor                          ← 单槽位编辑器，内部组合 NodePickerMenu + TreeEditor
  └── 自定义编辑器                        ← 使用 ChildListEditor / SlotEditor / DropStrip
```

**核心类**:

| 类 | 位置 | 职责 |
|----|------|------|
| `TreeDragContext` | `ShimmerChatLib.Components` | 级联共享的拖拽状态（拖了谁、从哪拖、往哪放） |
| `TreeEditor` | `ShimmerChatLib.Components` | 节点卡片，声明 `ParentList` / `OnDragRemove` 即可拖拽 |
| `DropStrip` | `ShimmerChatLib.Components` | 可复用的放置条，声明 `TargetList` + `InsertIndex` 即可接受放置 |
| `ChildListEditor` | `ShimmerChatLib.Components` | 子列表编辑器，封装添加/删除/排序/拖拽/粘贴 |
| `SlotEditor` | `ShimmerChatLib.Components` | 单槽位编辑器，封装添加/删除/拖拽/粘贴 |

### TreeEditor 参数（拖拽相关）

| 参数 | 类型 | 说明 |
|------|------|------|
| `ParentList` | `IList<IGenerationNode>?` | 节点所属的父列表。提供后节点变为可拖拽，`CommitDrop` 从这里移除 |
| `OnDragRemove` | `Func<Task>?` | 用于单节点槽。拖动时调用此回调移除节点（将属性设为 null） |
| `OnDragSourceChanged` | `EventCallback` | 拖拽后通知源组件重渲染。`ChildListEditor` / `SlotEditor` 内部自动传递 |
| `DragContext` | `TreeDragContext?` | 通过 `[CascadingParameter]` 自动接收，无需手动传递 |

**规则**: `ParentList != null && Depth > 0` 或 `OnDragRemove != null` 时节点可拖拽。根节点 (Depth=0) 即使有 ParentList 也不可拖。

### DropStrip 组件

```razor
<DropStrip TargetList="@_children" InsertIndex="0" />
```

| 参数 | 类型 | 说明 |
|------|------|------|
| `TargetList` | `IList<IGenerationNode>` (必填) | 放置时插入的目标列表 |
| `InsertIndex` | `int` | 插入位置 (0 = 开头，Count = 末尾) |

`DropStrip` 自动从 `[CascadingParameter]` 接收 `DragContext`，处理 `ondragover`/`ondrop` 并调用 `DragContext.CommitDrop()`。放置成功后目标组件通过 Blazor 事件处理器自动重渲染。

**视觉**: 平时 4px 高不可见，拖过时展开至 16px 带蓝色虚线框。

### 在自定义编辑器中集成拖拽

**推荐做法：直接使用 `ChildListEditor` 或 `SlotEditor`。**

这些组件已内置 `DropStrip`、`NodePickerMenu`、排序按钮等完整交互逻辑，无需手动拼装。

```razor
@* 子节点列表 — ChildListEditor 自带拖拽、排序、添加、删除 *@
<ChildListEditor Label="Children"
                 ChildNodes="Node.Nodes"
                 Depth="Depth"
                 CopyMe="CopyMe" />

@* 单节点槽 — SlotEditor 自带添加、删除、拖拽 *@
<SlotEditor Label="Then"
            SlotNode="Node.Then"
            Depth="Depth"
            OnSet="v => Node.Then = v"
            OnClear="() => Node.Then = null"
            CopyMe="CopyMe" />
```

**高级：直接使用 `DropStrip`。**

仅当你的拖拽布局无法用 `ChildListEditor` / `SlotEditor` 表达时（例如非标准的网格排列），才需要手动拼装 `DropStrip` + `TreeEditor`：

```razor
<div class="my-editor">
    @* 1. 列表开头放置条 *@
    <DropStrip TargetList="@_childNodes" InsertIndex="0" />

    @for (int i = 0; i < _childNodes.Count; i++)
    {
        var child = _childNodes[i];

        @* 2. 子节点 TreeEditor — 提供 ParentList 使其可拖拽 *@
        <TreeEditor Node="@child" Depth="@(Depth + 1)"
                    ParentList="@_childNodes"
                    OnDragSourceChanged="HandleDragSourceChanged" />

        @* 3. 子节点之间的放置条 *@
        <DropStrip TargetList="@_childNodes" InsertIndex="@(i + 1)" />
    }
</div>
```

`TreeDragContext` 通过级联值自动流入，无需手动传递。

### 拖拽流（生命周期）

```
DragStart (TreeEditor.OnDragStart)
  → DragContext.BeginDrag(node, sourceList, notify)
  → 节点变半透明 (tn-dragging)

DragOver (DropStrip.OnDragOver)
  → 放置条展开 (tn-drop-strip-active)

Drop (DropStrip.OnDrop / SlotEditor.OnSlotDrop)
  → DragContext.CommitDrop(targetList, insertIndex)  /  RemoveFromSource
      → 同列表检查 (ReferenceEquals 判重)
      → 环路检测 (IsDescendantOf 遍历)
      → 索引修正 (同列表移除后偏移)
      → 移除源 + 插入目标
      → SourceNotify → TreeEditor.OnDragSourceChanged → 源列表所属组件重渲染
  → 目标组件通过 Blazor 事件处理器自动重渲染

DragEnd (TreeEditor.OnDragEnd)
  → DragContext.EndDrag() 清理状态
```

### 防护机制

| 场景 | 处理 |
|------|------|
| 拖入自身 | `OnDragOverCard` 检测并拒绝 |
| 拖入子孙节点（环路） | `IsDescendantOf()` 递归反射检查 |
| 同列表无意义移动 | `CommitDrop` 检测 `insertIndex == sourceIndex \|\| insertIndex == sourceIndex + 1` |
| 根节点拖动 | `Depth == 0` 时 `ParentList` 无效，不可拖 |
| 单槽 → 列表 | `RemoveFromSource()` 先移除再插入 |
| 列表 → 单槽 | `OnSlotDrop` 先设属性再从源移除 |

---

## 7. 关键上下文对象

### NodeExecutionContext

```csharp
public class NodeExecutionContext
{
    public GenerationEnv Env { get; }
    public CancellationToken CancellationToken { get; }
}
```

### GenerationEnv

```csharp
public class GenerationEnv
{
    public TransientEnv Transient { get; }    // 每次生成重建
    public PersistentEnv Persistent { get; }  // 跨生成持久化
}
```

### TransientEnv — 每次生成重建

| 属性 | 类型 | 说明 |
|------|------|------|
| `Fragments` | `List<ContextSegment>` | 发送给 LLM 的上下文片段，节点通过 `Add` 注入 |
| `Tools` | `List<IToolV2>` | 本次生成可用工具，节点通过 `AddRange` 注入 |
| `API` | `APISetting?` | API 配置实例，由 `APISelectNode` 设置 |
| `SharedState` | `Dictionary<string, object>` | 跨节点共享状态，用于节点间通信 |

### PersistentEnv — 跨生成持久化

| 属性 | 类型 | 说明 |
|------|------|------|
| `KVData` | `IKVDataService` | 键值存储（LiteDB 或 LocalFile，取决于配置） |
| `ChatGuid` | `Guid` | 当前对话 ID |
| `AgentGuid` | `Guid` | 当前 Agent ID |
| `ToolRegistry` | `IToolRegistry` | 工具注册表，可查询已注册工具 |
| `Serializer` | `IGenerationNodeSerializer` | 节点树序列化/反序列化 |

### 执行流程

```
Agent.ModifierTreeJson
  → GenerationNodeSerializer.Deserialize → IGenerationNode 树
  → GenerationTreeExecutor.ExecuteAsync(root, persistentEnv)
      → 逐节点执行，修改 TransientEnv
      → 返回填充好的 GenerationEnv
  → ToolCallLoop (LLM 调用 + 工具执行循环)
```

节点的 `ExecuteAsync` 在 `ToolCallLoop` **之前**运行，用于准备环境（注入 fragments、tools、选择 API 等）。

---

## 8. 错误码参考

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

使用方式：

```csharp
return Task.FromResult(NodeResult.Failure(
    NodeErrorCodes.PresetNotFound,
    "预设未找到",
    $"ID: {presetId}",
    Id, Name));
```
