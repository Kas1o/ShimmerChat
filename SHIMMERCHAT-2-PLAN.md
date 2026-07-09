# ShimmerChat 2.0 迁移计划

不兼容更新。旧代码直接删除。新功能在旧功能迁移完成后再做。

## 分层

```
ShimmerChatLib      → 接口、数据模型、执行引擎（插件开发引用）
ShimmerChatBuiltin  → 具体节点类型、内置 Tool 实现
ShimmerChat         → 序列化、应用层服务、UI
```

---

## 阶段 1：核心抽象 (ShimmerChatLib)

新增文件，不修改现有代码。

### 生成环境

```
ShimmerChatLib/Generation/
├── TransientEnv.cs        // Fragments, Tools, API, SharedState
├── PersistentEnv.cs       // KVData, ChatGuid, AgentGuid
└── GenerationEnv.cs       // TransientEnv + PersistentEnv
```

### 节点接口

```csharp
public interface IGenerationNode
{
    string Id { get; }
    string Name { get; set; }
    Task ExecuteAsync(NodeExecutionContext context);
}

public class NodeExecutionContext
{
    public GenerationEnv Env { get; }
    public CancellationToken CancellationToken { get; }
}
```

Lib 不包含任何具体节点类型。

### 执行引擎

```csharp
public class GenerationTreeExecutor
{
    public async Task<GenerationEnv> ExecuteAsync(
        IGenerationNode rootNode, PersistentEnv env, CancellationToken ct);
}
```

引擎只调 `IGenerationNode.ExecuteAsync`。遍历逻辑由各节点内部控制。

### Tool 抽象

```csharp
public interface IToolV2
{
    string Name { get; }
    string Description { get; }
    SharperLLM.FunctionCalling.Tool GetDefinition();
    Task<string> ExecuteAsync(string input);
}
```

依赖通过构造函数注入。无参构造的工具可被自动发现和批量开关；有参构造的工具通过专门的节点手动实例化并提供依赖。

### 其他

- `GenerationPreset` — Id, Name, RootNodeJson (string)
- `NodeEditorAttribute` — 标记节点对应的 Blazor 编辑器组件
- `ContextSegment` 从 `Context/` 移入 `Generation/`

---

## 阶段 2：具体节点 (ShimmerChatBuiltin)

新增文件，不修改现有代码。

```
ShimmerChatBuiltin/Generation/Nodes/
├── SequenceNode.cs            // 顺序执行子节点，Repeat=N
├── IfNode.cs                  // Condition + Then/Else
├── CallNode.cs                // PresetId → 加载并内联执行
├── FragmentNode.cs            // 向 Fragments 注入 ContextSegment
├── ToolInstantiateNode.cs     // 无参构造的 Tool → Activator 创建 → 加入 Tools 列表
├── MemoryToolNode.cs          // 构造 MemoryTool，注入 Qdrant 配置 + SharedState
├── VariableToolNode.cs        // 构造 VariableTool，注入 ChatGuid + AgentGuid
├── SetChatNameNode.cs         // 构造 SetChatNameTool，注入 Chat
├── APISelectNode.cs           // 设置 TransientEnv.API
├── FragmentTrimNode.cs        // Token 裁剪（替代 TokenLimit + LatestN）
├── MemoryRetrieveNode.cs      // Qdrant 检索注入（替代 MemoryInject）
└── AgentRootNode.cs           // Agent 树边界
```

`ToolInstantiateNode` 只处理无参构造的 Tool（Activator.CreateInstance）。有依赖的 Tool 用专用节点：节点在 ExecuteAsync 中手动 `new` 并传入依赖，然后加入 `TransientEnv.Tools`。

每个节点带 `[NodeEditor(typeof(XxxEditor))]`。编辑器组件放在 `Generation/Editors/`。

IfNode.Condition 此阶段仅支持 `SharedState['key'] == "value"`。完整表达式引擎留到阶段 B。

---

## 阶段 3：序列化 (ShimmerChat)

新增文件，不修改现有代码。

- `GenerationNodeSerializer` — 节点树 ↔ JSON，扫描 IGenerationNode 实现作为类型池

---

## 阶段 4：Tool 重写

全部改为 `IToolV2`。

**无参构造（ToolInstantiateNode 自动管理）：**

| Tool | 说明 |
|------|------|
| `FileSystemReadTool` | 无依赖 |
| `FileSystemWriteTool` | 无依赖 |
| `FileSystemEditTool` | 无依赖 |
| `FileSystemBrowseTool` | 无依赖 |
| `FileSystemOverviewTool` | 无依赖 |
| `InvokeCSharp` | 无依赖 |

**有参构造（专用节点手动实例化）：**

| Tool | 依赖 | 专用节点 |
|------|------|----------|
| `MemoryTool` | Qdrant 配置、SharedState | `MemoryToolNode` |
| `VariableTool` | ChatGuid、AgentGuid | `VariableToolNode` |
| `SetChatNameTool` | Chat 引用 | `SetChatNameNode` |

删除：
```
ShimmerChatLib/Tool/ITool.cs
ShimmerChat/Singletons/ToolServiceV1.cs
```

---

## 阶段 5：ContextModifier 重写为节点

| 旧 | 替换 |
|----|------|
| TokenLimit | FragmentTrimNode |
| LatestN | 并入 FragmentTrimNode |
| VariableInject | FragmentNode |
| MemoryInject | MemoryRetrieveNode |
| DynPrompt | IfNode + FragmentNode |
| SubAgentGeneration / BackgroundGeneration / CollectResults | 阶段 6 |

删除：
```
ShimmerChatLib/Context/IContextModifier.cs
ShimmerChatLib/Context/ModifierConfig.cs
ShimmerChatLib/Context/LegacyModifierConfig.cs
ShimmerChatLib/Context/ContextDocument.cs
ShimmerChatLib/Context/IContextModifierConfigUI.cs
ShimmerChatLib/Context/UiHintAttribute.cs
ShimmerChatLib/Interface/IContextModifierService.cs
ShimmerChat/Singletons/ContextModifierServiceV1.cs
ShimmerChat/Singletons/ContextBuilderServiceV1.cs
ShimmerChatBuiltin/Misc/TokenLimit.cs
ShimmerChatBuiltin/Misc/LatestN.cs
ShimmerChatBuiltin/Variable/VariableInject.cs
ShimmerChatBuiltin/Memory/MemoryInject.cs
ShimmerChatBuiltin/DynPrompt/DynPrompt.cs
```

`DynPromptParser` 保留，阶段 B 复用。

---

## 阶段 6：SubAgent

- `SubAgentNode` — 修改器阶段调用，替代 SubAgentGeneration + BackgroundGeneration + CollectResults
- `SubAgentTool` — IToolV2，工具调用模式
- SubAgentRunner 改为使用修改器树

删除：
```
ShimmerChatBuiltin/SubAgent/SubAgentGeneration.cs
ShimmerChatBuiltin/SubAgent/BackgroundGeneration.cs
ShimmerChatBuiltin/SubAgent/CollectResults.cs
```

---

## 阶段 7：Agent 迁移

Agent.cs 新增 `ModifierTreeJson : string`。`CustomToolNames` 和 `Description` 暂保留。

新增 `AgentMigrationService`（ShimmerChat 应用层）：
- 遍历 Agent，无 ModifierTreeJson 的执行迁移
- Description → AgentRootNode → FragmentNode（system 片段）
- CustomToolNames → ToolInstantiateNode 列表

---

## 阶段 8：生成管道重写

`GenerationManagerV2` 替代 `AIGenerationServiceV1`：
1. 反序列化 Agent.ModifierTreeJson → 执行树
2. Fragments → PromptBuilder
3. Tools → function calling
4. API → LLM 生成
5. Tool Call 循环

删除：
```
ShimmerChat/Singletons/AIGenerationServiceV1.cs
ShimmerChat/Singletons/CompletionServiceV1.cs
```

Agent.cs 删除 `CustomToolNames`。Description 不再自动注入为 SystemPrompt。

---

## 阶段 9：死代码清理

```
SharperLLM/Managers/ConversationManager.cs
SharperLLM/Agents/InMemoryPromptContext.cs
ShimmerChatLib/Interface/ICompletionService.cs
ShimmerChatLib/Interface/IContextBuilderService.cs
```

---

## 阶段 10：UI

- `GenerationManagerPage.razor` — 树编辑器 + Preset 管理 + 工具预设面板
- 删除 `ContextManagerPage.razor`、`ToolManager.razor`

工具预设面板：扫描所有无参 `IToolV2` 实现，以开关列表呈现。开启 → 向树中追加 `ToolInstantiateNode`；关闭 → 移除。有参工具不出现于此面板，通过树编辑器手动添加对应专用节点。

面板重组：
```
主页 → 生成管理器
智能体 → Misc
Representation → 消息渲染、主题管理
PluginPanels → API设置、ToolManager、其余 Panel
```

---

## 阶段 11：验证

- Agent 数据自动迁移
- 聊天生成全流程
- Tool Call 循环 + SharedState
- Preset 引用与复用
- 流式响应
- 单元测试更新

---

## 阶段 B：新功能

- **ProbeNode** — 探针：dump / 断点 / 计时
- **条件表达式引擎** — 完整求值器，复用 DynPromptParser
- **模拟预览** — 虚拟执行，不调 API
- **Preset 导入导出**

---

## 执行顺序

```
阶段 1 → 阶段 2 → 阶段 3
                      │
阶段 4 (删 ITool)
阶段 5 (删 IContextModifier)
阶段 6 (删旧 SubAgent Modifier)
阶段 7 (Agent 加字段)
阶段 8 (删旧管道 + Agent.CustomToolNames)
阶段 9 (死代码)
阶段 10 (UI)
阶段 11 (验证)
阶段 B (新功能)
```
