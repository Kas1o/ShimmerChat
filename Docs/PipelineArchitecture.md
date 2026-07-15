# 管线架构总览

ShimmerChat 2.0 采用**三管线节点树架构**，将 AI 聊天的生成流程拆分为三个独立的、用户可编辑的节点管线。

---

## 核心概念

每个管线由一棵**节点树**（node tree）定义，存储在 Agent 的三个 JSON 属性中：

| 管线 | Agent 属性 | 节点接口 | 执行时机 |
|------|-----------|---------|---------|
| **Pre-Generation** | `PreGenerationTreeJson` | `IPreGenerationNode` | LLM 调用前 |
| **Post-Generation** | `PostGenerationTreeJson` | `IPostGenerationNode` | LLM 响应后 |
| **Render Modifier** | `RenderModifierTreeJson` | `IRenderModifierNode` | 消息渲染时 |

### 执行流

```
用户发送消息
  │
  ▼
Pre-Generation Tree ──→ 构建 GenerationEnv (Fragments, Tools, API)
  │
  ▼
ToolCallLoop ──→ LLM 流式生成 + 工具调用
  │
  ▼
Post-Generation Tree ──→ 过滤/转换/富化原始响应
  │
  ▼
Render Modifier Tree ──→ GetContent/UpdateContent 链式处理 ──→ 最终 HTML
```

---

## ITreeNode — 最小节点接口

所有管线节点共享基础接口：

```csharp
public interface ITreeNode
{
    string Id { get; }          // 唯一标识 (GUID)
    string Name { get; set; }   // 用户可编辑名称
}
```

```csharp
// 三个管线接口
IPreGenerationNode  : ITreeNode   // + ExecuteAsync(PreNodeExecutionContext)
IPostGenerationNode : ITreeNode   // + ExecuteAsync(PostNodeExecutionContext)
IRenderModifierNode : ITreeNode   // + ExecuteAsync(RenderNodeExecutionContext)
```

---

## 共享编辑器基础设施

三个管线共用同一套节点编辑器 UI 组件。通过 `TreeEditorContext` 区分管线：

```csharp
public class TreeEditorContext
{
    ITreeNodeSerializer Serializer;    // 当前管线的序列化器
    INodeTypeCatalog TypeCatalog;      // 节点类型目录（按接口过滤）
    Type NodeInterfaceType;            // typeof(IPreGenerationNode) 等
}
```

每个管线页面创建自己的 `TreeEditorContext`，通过 `CascadingValue` 向下传递。详见 [节点编辑器系统](NodeEditorSystem.md)。

---

## Agent 模型

```csharp
public class Agent
{
    // 2.0 三棵树
    public string? PreGenerationTreeJson { get; set; }
    public string? PostGenerationTreeJson { get; set; }
    public string? RenderModifierTreeJson { get; set; }
}
```

每个树独立序列化/反序列化，互不干扰。Agent 级别（`?agent={guid}`）的编辑覆盖全局预设。

---

## 节点类型目录

`INodeTypeCatalog` 扫描所有 `ITreeNode` 实现，通过 `GetNodeTypes(Type)` 按管线接口过滤。标记了 `[NodeInfo]` 的节点自动出现在对应管线的添加菜单中。

---

## 页面路由

| 路由 | 管线 | 功能 |
|------|------|------|
| `/generationmanager` | Pre-Generation | 预设管理 + Agent 私有树编辑 |
| `/postgeneration` | Post-Generation | 预设管理 + Agent 私有树编辑 |
| `/messagerender` | Render Modifier | 预设管理 + Agent 私有树编辑 |
| `/agent/{guid}` | — | 显示三个管线的私有树状态，链接到各自编辑页 |
