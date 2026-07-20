# 节点编辑器系统

三个管线共用同一套节点编辑器 UI 组件。所有组件通过 `ITreeNode` 接口操作节点，通过 `TreeEditorContext` 获取管线特化的序列化器和类型目录。

---

## 目录

1. [三个 Attribute](#1-三个-attribute)
2. [属性编辑器](#2-属性编辑器)
3. [自定义编辑器](#3-自定义编辑器)
4. [容器节点与子列表](#4-容器节点与子列表)
5. [拖拽排序系统](#5-拖拽排序系统)
6. [TreeEditorContext](#6-treeeditorcontext)
7. [序列化](#7-序列化)
8. [样式参考](#8-样式参考)

---

## 1. 三个 Attribute

### NodeInfoAttribute — 类级元数据

```csharp
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public class NodeInfoAttribute : Attribute
{
    public string LabelKey { get; }           // 必填，本地化 Key
    public string Icon { get; init; }         // 默认 "●"
    public string Color { get; init; }        // CSS 颜色值，推荐 CSS 变量
    public string[] CategoryKeys { get; init; } = ["category.general"];  // 斜杠分层导航，默认 "category.general"
    public string? DescriptionKey { get; init; }  // 可选描述
}
```

- `LabelKey`、`DescriptionKey` 均为本地化 Key。
- `CategoryKeys` 支持多级：`["category.flow", "branch"]` → 菜单显示为 `Flow > Branch`。
- `Color` 推荐使用 CSS 变量以自动适配主题：

| 变量 | 语义 | 示例 |
|------|------|------|
| `var(--node-flow)` | 流程控制 | SequenceNode |
| `var(--node-branch)` | 分支/条件 | IfNode |
| `var(--node-fragment)` | 内容片段 | FragmentNode |
| `var(--node-prompt)` | 提示词 | DynPromptNode |
| `var(--node-tool)` | 工具 | ToolInstantiateNode |
| `var(--node-link)` | 链接/引用 | CallNode |
| `var(--node-config)` | 配置/API | APISelectNode |
| `var(--node-debug)` | 调试 | PrintNode |

### NodePropertyAttribute — 属性级元数据

```csharp
[AttributeUsage(AttributeTargets.Property)]
public class NodePropertyAttribute : Attribute
{
    public string LabelKey { get; }       // 必填
    public string? HintKey { get; init; } // Tooltip
    public int Order { get; init; }       // 排序，越小越靠前
    public bool MultiLine { get; init; }  // string 用 textarea
}
```

支持的类型和对应控件：

| 属性类型 | 表单控件 |
|----------|---------|
| `string` | `<input>` 或 `<textarea>` (MultiLine) |
| `int` | `<input type="number">` |
| `float` | `<input type="number" step="0.1">` |
| `bool` | `<input type="checkbox">` |
| `enum` | `<select>` |
| `List<T>` (T : ITreeNode) | ChildListEditor |
| `T` (T : ITreeNode) | SlotEditor |

### NodeEditorAttribute — 自定义编辑器

```csharp
[AttributeUsage(AttributeTargets.Class)]
public class NodeEditorAttribute : Attribute
{
    public Type EditorType { get; }
}
```

指定自定义 Blazor 编辑器组件替代 `GenericNodeEditor`。详见 [第 3 节](#3-自定义编辑器)。

---

## 2. 属性编辑器

### GenericNodeEditor

`GenericNodeEditor<TNode>` 通过反射扫描 `[NodeProperty]` 属性，根据类型自动生成表单。**这是默认行为**——只要节点只包含标量属性 + 子节点列表，无需写任何 UI。

### 编辑器渲染流

```
TreeEditor
  ├── .tn-header (图标 + 标签 + 名称输入框 + 复制/删除按钮)
  └── .tn-body (展开时)
        ├── 有 [NodeEditor] → DynamicComponent 加载自定义编辑器
        └── 无 [NodeEditor] → NodeBodyRenderer → GenericNodeEditor<T>
              ├── [NodeProperty] 标量 → 表单控件
              ├── [NodeProperty] List<T> → ChildListEditor
              │     ├── NodePickerMenu (添加菜单)
              │     ├── DropStrip × (插入点)
              │     └── TreeEditor × (子节点卡片)
              └── [NodeProperty] T → SlotEditor
                    ├── NodePickerMenu (添加菜单)
                    └── TreeEditor (槽位节点)
```

---

## 3. 自定义编辑器

### 何时需要

- 属性需要特殊 UI（搜索、列表选择等）
- 需要访问注入服务（IKVDataService 等）

### 实现步骤

**步骤 1**: 标记节点

```csharp
[NodeInfo("node.my_call", Icon = "⚙", Color = "var(--node-link)")]
[NodeEditor(typeof(CallNodeEditor))]
public class CallNode : IPreGenerationNode { ... }
```

**步骤 2**: 创建编辑器（与节点同程序集）

以下为示意代码，展示自定义编辑器的基本结构。实际 `CallNodeEditor` 使用预设搜索/选择 UI（注入 `IKVDataService`、`IJSRuntime` 等），比此示例更复杂。

```razor
@using ShimmerChatLib.Components

<div class="ge-field">
    <label class="ge-label">Preset</label>
    <div class="ge-value">
        @* 自定义 UI（如预设搜索/选择列表）... *@
    </div>
</div>

@code {
    [Parameter] public CallNode Node { get; set; } = default!;
    [Parameter] public int Depth { get; set; }
    [Parameter] public Action<ITreeNode>? CopyMe { get; set; }
}
```

自定义编辑器**不需要**发出变更通知。所有修改直接作用在 `Node` 上，外部保存时统一序列化。

### 可复用组件

| 组件 | 用途 | 关键参数 |
|------|------|---------|
| `ChildListEditor` | 子列表编辑（添加/删除/排序/拖拽/粘贴） | `ChildNodes`, `Depth`, `CopyMe` |
| `SlotEditor` | 单槽编辑（添加/删除/拖拽/粘贴） | `SlotNode`, `Depth`, `OnSet`, `OnClear`, `CopyMe` |
| `NodePickerMenu` | 添加节点菜单（分类导航 + 搜索） | `OnSelect`, `OnClose` |
| `DropStrip` | 拖拽放置条 | `TargetList`, `InsertIndex` |

### 编辑器参数约定

`TreeEditor` 通过 `DynamicComponent` 传递：

| 参数 | 类型 | 说明 |
|------|------|------|
| `Node` | 具体节点类型 | 自动按编辑器声明的 `[Parameter]` 类型传入 |
| `Depth` | `int` | 缩进层级 |
| `CopyMe` | `Action<ITreeNode>?` | 复制到剪贴板回调 |
| `RemoveMe` | `Action<ITreeNode>?` | 移除节点回调 |

---

## 4. 容器节点与子列表

### 声明子列表

```csharp
[NodeProperty("prop.sequence.children")]
public List<IPreGenerationNode> Children { get; set; } = new();
```

`GenericNodeEditor` 通过 `TreeNodeReflection.IsListOfTreeNode()` 自动检测子列表属性（支持 `List<T>` 和 `IList<T>`，其中 `T : ITreeNode`）。`ChildListEditor` 自动提供：

- 添加按钮 → NodePickerMenu
- 粘贴按钮 → 剪贴板粘贴
- 上下箭头 → 手动排序
- 删除按钮
- 拖拽排序 → DropStrip 插入点

### 单节点槽

```csharp
[NodeProperty("prop.if_node.then")]
public IPreGenerationNode? Then { get; set; }
```

`SlotEditor` 渲染为单槽：有节点时显示 `TreeEditor`，空时显示添加按钮。支持拖放和粘贴。

---

## 5. 拖拽排序系统

### 架构

```
CascadingValue<TreeDragContext>       ← 全局级联
  ├── TreeEditor (.tn-header)         ← 拖拽源 (ParentList / OnDragRemove)
  ├── DropStrip                       ← 放置目标 (TargetList + InsertIndex)
  ├── ChildListEditor                 ← 封装 DropStrip + TreeEditor
  └── SlotEditor                      ← 封装 NodePickerMenu + TreeEditor
```

### 核心组件

| 组件 | 职责 |
|------|------|
| `TreeDragContext` | 级联共享拖拽状态 |
| `TreeEditor` | 节点卡片，声明 ParentList/OnDragRemove 即拖拽 |
| `DropStrip` | 放置条，声明 TargetList + InsertIndex 接受放置 |

### TreeEditor 拖拽参数

| 参数 | 类型 | 说明 |
|------|------|------|
| `ParentList` | `IList?` | 节点所属列表（非泛型，兼容所有 ITreeNode 列表） |
| `OnDragRemove` | `Func<Task>?` | 单槽拖动回调 |
| `OnDragSourceChanged` | `EventCallback` | 拖拽后通知源重渲染 |

规则：`ParentList != null && Depth > 0` 或 `OnDragRemove != null` 时可拖。根节点不可拖。

### 拖拽生命周期

```
DragStart → BeginDrag(node, sourceList, notify)
DragOver  → 放置条展开 (tn-drop-strip-active)
Drop      → CommitDrop(targetList, index)
              → 环路检测 (IsDescendantOf)
              → 移除源 + 插入目标
              → SourceNotify 源重渲染
DragEnd   → EndDrag() 清理
```

### 防护

| 场景 | 处理 |
|------|------|
| 拖入自身/子孙 | `IsDescendantOf()` 递归检查 |
| 同列表无意义移动 | `insertIndex == sourceIndex` 检测 |
| 根节点 | Depth=0 不可拖 |

---

## 6. TreeEditorContext

每个管线页面创建自己的 `TreeEditorContext`，通过 `CascadingValue` 传递：

```csharp
public class TreeEditorContext
{
    public ITreeNodeSerializer Serializer { get; }      // 序列化器
    public INodeTypeCatalog TypeCatalog { get; }         // 类型目录
    public Type NodeInterfaceType { get; }               // 管线接口类型

    public IReadOnlyList<NodeTypeMetadata> GetAvailableNodeTypes();
    public ITreeNode CreateNode(Type type);
    public void Copy(ITreeNode node);                    // 剪贴板
    public ITreeNode? Paste();
    public void ClearClipboard();                         // 清空剪贴板
    public bool HasClipboardContent { get; }
}
```

页面初始化时创建：

```csharp
// Pre-Generation 页面
_editorContext = new TreeEditorContext(serializer, nodeTypeCatalog, typeof(IPreGenerationNode));

// Post-Generation 页面
_editorContext = new TreeEditorContext(serializer, nodeTypeCatalog, typeof(IPostGenerationNode));

// Render Modifier 页面
_editorContext = new TreeEditorContext(serializer, nodeTypeCatalog, typeof(IRenderModifierNode));
```

---

## 7. 序列化

节点树通过 Newtonsoft.Json + `TypeNameHandling.Objects` 序列化。每个管线的 Serializer 实现 `ITreeNodeSerializer`，通过 `SerializationBinder` 白名单机制安全反序列化。

```csharp
public interface ITreeNodeSerializer
{
    string Serialize(ITreeNode root);
    ITreeNode? Deserialize(string json);
    IReadOnlyDictionary<string, Type> GetKnownTypes();
}
```

三个序列化器分别扫描各自的节点接口构建类型白名单：
- `PreGenerationNodeSerializer` → `IPreGenerationNode` 实现
- `PostGenerationNodeSerializerService` → `IPostGenerationNode` 实现
- `RenderModifierNodeSerializer` → `IRenderModifierNode` 实现

**注意**：`Id` 属性用于 Blazor `@key` 和环路检测，由系统管理，不要加 `[NodeProperty]`。

---

## 8. 样式参考

使用 `node-editor.css` 中的全局类名保持风格一致。颜色用 `theme.css` 中的 CSS 变量。

| 类名 | 用途 |
|------|------|
| `.ge-field` | 属性行容器 |
| `.ge-label` | 属性标签 |
| `.ge-value` | 属性值区域 |
| `.ge-input` | 文本输入框 |
| `.ge-textarea` | 多行文本框 |
| `.ge-select` | 下拉框 |
| `.ge-check` | 复选框 |
| `.ge-children` / `.ge-children-header` | 子列表容器/标题 |
| `.tn-child-row` / `.tn-child-actions` / `.tn-child-tree` | 子节点行/按钮/TreeEditor |
| `.tn-drop-strip` / `.tn-drop-strip-active` | 放置条 |
| `.tn-btn-add` / `.tn-btn-del` / `.tn-btn-copy` / `.tn-btn-paste` / `.tn-btn-move` | 按钮 |
| `.tn-add-menu` / `.tn-add-search` / `.tn-add-item` | 添加节点菜单 |
| `.tree-node-card` / `.tn-header` / `.tn-body` | 节点卡片 |
