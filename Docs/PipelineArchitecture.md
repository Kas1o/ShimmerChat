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
Pre-Generation Tree ──→ 构建 PreGenerationEnv（含 Transient.Fragments、Transient.Tools、Transient.API）
  │
  ▼
┌─ 轮次循环（ToolCallLoop）──────────────────────────────────┐
│  LLM 流式生成                                              │
│      │                                                     │
│      ▼                                                     │
│  Post-Generation Tree ──→ 改写响应 / 增删改对话消息         │
│                      ──→ PostToolCallNode 执行工具调用      │
│                      ──→ 设置 LoopDirective                 │
│      │                                                     │
│      ├ Continue ──→ 重建前生成环境 ──→ 再生成一轮           │
│      └ Stop     ──→ 结束                                    │
└────────────────────────────────────────────────────────────┘
  │
  ▼
Render Modifier Tree ──→ GetContent/UpdateContent 链式处理 ──→ 最终 HTML
```

后处理树负责回合控制：工具调用处理与循环继续/停止都在里面完成，
因此"按工具名停止""注入提醒再续轮""压缩历史后继续"这类规则可以用节点表达。

---

## ITreeNode — 树包装器

树中的节点是包装器，持有树结构状态；业务节点只实现管线接口：

```csharp
public interface ITreeNode
{
    string Id { get; set; }          // 唯一标识 (GUID)
    string Name { get; set; }        // 用户可编辑名称
    bool IsExpanded { get; set; }    // 展开状态，随树持久化
    bool IsBypassed { get; set; }    // 旁路：执行时跳过该节点及整棵子树，随树持久化
    object? Payload { get; }         // 业务节点实例（可能为 null，表示数据缺失）
    Type PayloadType { get; }
}
```

```csharp
public class PreGenerationNodeWrapper : NodeWrapper<IPreGenerationNode> { … }
public class PostGenerationNodeWrapper : NodeWrapper<IPostGenerationNode> { … }
public class RenderModifierNodeWrapper : NodeWrapper<IRenderModifierNode> { … }
```

```csharp
// 三个管线接口（不再继承 ITreeNode）
IPreGenerationNode   // ExecuteAsync(PreNodeExecutionContext)
IPostGenerationNode  // ExecuteAsync(PostNodeExecutionContext)
IRenderModifierNode  // Execute(RenderNodeExecutionContext)
```

包装器负责补齐执行错误的 `NodeId`/`NodeName`、维护展开状态，并在反序列化失败时把缺失负载暴露为可见错误。
旁路（`IsBypassed`）也由包装器实施：三个管线的 `Execute` 入口统一短路，开启时负载与整棵子树不执行、直接返回成功；
根包装器旁路 = 整条管线空跑。详见 [节点编辑器系统](NodeEditorSystem.md#旁路isbypassed)。

---

## 共享编辑器基础设施

三个管线共用同一套节点编辑器 UI 组件。通过 `TreeEditorContext` 区分管线：

```csharp
public class TreeEditorContext
{
    ITreeNodeSerializer Serializer;    // 当前管线的序列化器
    INodeTypeCatalog TypeCatalog;      // 节点类型目录（按接口过滤）
    Type NodeInterfaceType;            // typeof(IPreGenerationNode) 等
    Type WrapperType;                  // typeof(PreGenerationNodeWrapper) 等
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

`INodeTypeCatalog` 扫描实现三个管线接口的业务节点类型，通过 `GetNodeTypes(Type)` 按管线接口过滤。标记了 `[NodeInfo]` 的节点自动出现在对应管线的添加菜单中。

---

## 页面路由

| 路由 | 管线 | 功能 |
|------|------|------|
| `/generationmanager` | Pre-Generation | 预设管理 + Agent 私有树编辑 |
| `/postgeneration` | Post-Generation | 预设管理 + Agent 私有树编辑 |
| `/messagerender` | Render Modifier | 预设管理 + Agent 私有树编辑 |
| `/agent/{agentguid}` | — | 显示三个管线的私有树状态，链接到各自编辑页 |
