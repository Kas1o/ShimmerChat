# ShimmerChat — Agent Guide

## Assembly Dependency Direction

```
ShimmerChat → ShimmerChatBuiltin → ShimmerChatLib → SharperLLM
     ↑              ↑                    ↑
  Blazor 宿主,   GenerationNode 实现,   接口/抽象/模型/序列化器
  Pages,        ToolV2 实现            无 UI 依赖
  Singletons   面板组件
```

- **ShimmerChatLib** 不引用任何 Blazor UI 程序集，纯逻辑层。
- **ShimmerChatBuiltin** 是唯一放 `IGenerationNode` 实现和 `IToolV2` 实现的地方。
- **SharperLLM** 是 git submodule，不应该在 ShimmerChat 仓库内修改。
- 测试项目：`ShimmerChatLib.Tests`（引用 ShimmerChatLib）、`ShimmerChat.Tests`（引用 ShimmerChat 宿主）。

---

## 生成管道 (Generation Pipeline)

这是整个应用最核心的路径，需要同时读 `GenerationManagerV2`、`GenerationTreeExecutor`、`IGenerationNode`、`GenerationEnv` 多个文件才能理解。

### 两个环境

| 环境 | 生命周期 | 内容 |
|------|---------|------|
| `PersistentEnv` | 跨生成 | `IKVDataService`, `ChatGuid`, `AgentGuid`, `IToolRegistry` |
| `TransientEnv` | 每次生成重建 | `List<ContextSegment> Fragments`（要发给 LLM 的上下文片段）, `List<IToolV2> Tools`, `ILLMAPI? API`, `Dictionary<string,object> SharedState` |

`GenerationEnv = TransientEnv + PersistentEnv`，节点通过 `NodeExecutionContext.Env` 拿到两者。

### 执行流程

```
Agent.ModifierTreeJson (JSON)
  → GenerationNodeSerializer.Deserialize → IGenerationNode 树
  → GenerationTreeExecutor.ExecuteAsync(root, persistentEnv)
      → 逐节点执行，每个节点修改 TransientEnv:
          APISelectNode       → TransientEnv.API = ...
          FragmentNode        → TransientEnv.Fragments.Add(...)
          ToolPresetNode      → TransientEnv.Tools.AddRange(...)
          MemoryRetrieveNode  → TransientEnv.Fragments.Add(检索结果)
          FragmentTrimNode    → 裁剪 Fragments
      → 返回填充好的 GenerationEnv
  → ToolCallLoop:
      PromptBuilder(fragments) + Tools → ILLMAPI.GenerateChatExStream
      → 检测 tool_calls → IToolV2.ExecuteAsync → 结果追加到 fragments → 循环
```

关键点：
- 树的遍历逻辑在节点内部控制（例如 `SequenceNode` 遍历 `Nodes` 列表），`GenerationTreeExecutor` 只负责启动根节点。
- 每个节点返回 `NodeResult`（`Success`/`Code`/`Message`/`Details`），失败会终止整个管道。
- Agent 的 `ModifierTreeJson` 为空时会自动创建默认树：FragmentNode(system prompt) → AppendChatMessages → APISelect → ToolPreset。

---

## 节点系统约定

节点文件在 `ShimmerChatBuiltin/Generation/Nodes/`，需遵守以下约定：

- 实现 `IGenerationNode`（`Id`, `Name`, `ExecuteAsync(NodeExecutionContext)`）
- 类上标记 `[NodeInfo]` — LabelKey、Icon、Color、CategoryKeys（UI 自动发现用）
- 属性上标记 `[NodeProperty]` — LabelKey、HintKey、Order（自动生成属性编辑器）
- 需要自定义编辑器时加 `[NodeEditor(typeof(MyEditor))]`，编辑器放在同目录

`GenerationNodeSerializer` 通过反射扫描所有 `IGenerationNode` 实现来构建反序列化类型表，新增节点无需额外注册。

---

## 工具系统约定

- `IToolV2` — 工具执行接口：`GetDefinition()` 返回 `SharperLLM.FunctionCalling.Tool`，`ExecuteAsync(string jsonArgs)` 返回执行结果字符串。
- `IAutoCreateToolV2` — 在 `IToolV2` 基础上加 `static abstract` 工厂方法，`ToolRegistry` 启动时自动扫描所有实现。
- 无法用无参构造的工具（需要注入 KVData/AgentGuid 等），通过 constructor-injected 的 Node 来创建，不走 `IAutoCreateToolV2`。

---

## 存储系统

两个后端实现，通过 `appsettings.json` 中的 `KVDataStorage` 配置节切换：

| 接口 | LiteDB 实现 | LocalFile 实现 |
|------|------------|----------------|
| `IKVDataService` | `LiteDBKVData` | `LocalFileStorageKVData` |
| `IMessageStoreService` | `LiteDBMessageStoreService` | `FileMessageStoreService` |

启动时会自动迁移（`AutoMigrateOnStartup`），迁移完成后写标记防止重复执行。`Message` 独立于 `Chat` 存储——Chat 对象只存 `MessageCount`/`LastMessagePreview`，实际消息通过 `IMessageStoreService` 分页加载。

---

## 测试

两个测试项目都使用 xUnit + FluentAssertions + Moq。

```powershell
dotnet test --filter "FullyQualifiedName~AgentTests"
```

- 数据层测试用 `Mock<IKVDataService>` + `Mock<IMessageStoreService>` 隔离。
- LiteDB 集成测试用 `new LiteDatabase(":memory:")` 创建临时实例，配合 `IDisposable` 清理。
- `ShimmerChat.Tests` 引用 `ShimmerChat.csproj`，可以测 Program.cs 中的服务实现。
