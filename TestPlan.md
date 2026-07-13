# ShimmerChat 生成管线测试开发计划

> **范围**: 排除 SharperLLM（submodule），聚焦 ShimmerChat 自身的生成管线
> **核心思路**: 制作可配置伪 API (`ConfigurableChatClient`) 驱动整个管线测试

---

## 当前状态

- **测试总数**: 182 (全部通过) — 88 LibTests + 46 BuiltinTests + 48 HostTests
- **已覆盖**: 数据模型 (Agent, Chat, Message, KVData 存储)、生成管线核心、节点系统
- **未覆盖**: Blazor 页面、插件系统、SharperLLM (排除)

---

## 基础设施: ConfigurableChatClient

当前 `PseudoChatCompletionClient` 只能回显最后一条消息。需要一个可配置的伪 API 来模拟真实的 LLM 行为。

**新增文件**: `ShimmerChatLib.Tests/ConfigurableChatClient.cs`

| 能力 | 说明 |
|------|------|
| `SetResponses(params ResponseEx[])` | 预设响应序列，每次调用依次返回 |
| `SetStreamChunks(params ResponseEx[][])` | 预设每个响应的流式分块 |
| `CallCount` | 记录被调用次数，用于断言 |

---

## Phase 1: 核心执行器

### 1.1 `GenerationTreeExecutor` 测试

**文件**: `ShimmerChatLib/Generation/GenerationTreeExecutor.cs` (48行)
**新增**: `ShimmerChatLib.Tests/GenerationTreeExecutorTests.cs`

| # | 测试用例 | 覆盖场景 |
|---|----------|----------|
| 1 | `ExecuteAsync_NodeReturnsSuccess_ReturnsEnv` | Mock IGenerationNode 返回 Success，验证 env 正确返回 |
| 2 | `ExecuteAsync_NodeReturnsFailure_ThrowsInvalidOperationException` | 失败时抛异常，消息包含 NodeName/NodeId/Code/Message |
| 3 | `ExecuteAsync_NodeReturnsFailure_WithDetails_IncludesDetails` | Details 非空时出现在异常消息中 |
| 4 | `ExecuteAsync_CancellationRequested_Throws` | CancellationToken 已取消时的行为 |

---

## Phase 2: 节点单元测试

> 测试项目: `ShimmerChatBuiltin.Tests` (新建)
> 节点测试统一通过 mock `IKVDataService` + `IToolRegistry` 提供数据

### 2.1 基础节点

| 节点 | 文件 | 行数 | 测试用例数 | 新增文件 |
|------|------|------|-----------|----------|
| `SequenceNode` | `ShimmerChatBuiltin/Generation/Nodes/SequenceNode.cs` | 37 | 5 | `SequenceNodeTests.cs` |
| `FragmentNode` | `ShimmerChatBuiltin/Generation/Nodes/FragmentNode.cs` | 39 | 3 | `FragmentNodeTests.cs` |
| `IfNode` | `ShimmerChatBuiltin/Generation/Nodes/IfNode.cs` | 96 | 9 | `IfNodeTests.cs` |

| 节点 | 用例 |
|------|------|
| **SequenceNode** | `EmptyChildren_ReturnsSuccess`, `SingleChild_Succeeds`, `MultipleChildren_ExecutesAllInOrder`, `ChildFails_StopsAndReturnsFailure`, `Repeat2_RunsTwice` |
| **FragmentNode** | `AddsSegmentToFragments`, `SystemRole_CorrectFrom`, `UserRole_CorrectFrom` |
| **IfNode** | `Condition_Match_Equals_ExecutesThen`, `Condition_NoMatch_Equals_ExecutesElse`, `Condition_Match_NotEquals_ExecutesThen`, `Condition_NoMatch_NotEquals_ExecutesElse`, `Condition_Empty_ReturnsSuccess`, `Condition_NoOperator_ReturnsSuccess`, `Then_Null_ConditionTrue_ReturnsSuccess`, `Else_Null_ConditionFalse_ReturnsSuccess`, `Then_Fails_PropagatesFailure` |

### 2.2 配置/数据节点

| 节点 | 文件 | 行数 | 测试用例数 | 新增文件 |
|------|------|------|-----------|----------|
| `APISelectNode` | `ShimmerChatBuiltin/Generation/Nodes/APISelectNode.cs` | 77 | 6 | `APISelectNodeTests.cs` |
| `AppendChatMessagesNode` | `ShimmerChatBuiltin/Generation/Nodes/AppendChatMessagesNode.cs` | 54 | 5 | `AppendChatMessagesNodeTests.cs` |
| `ToolPresetNode` | `ShimmerChatBuiltin/Generation/Nodes/ToolPresetNode.cs` | 69 | 6 | `ToolPresetNodeTests.cs` |
| `ToolInstantiateNode` | `ShimmerChatBuiltin/Generation/Nodes/ToolInstantiateNode.cs` | 51 | 5 | `ToolInstantiateNodeTests.cs` |
| `CallNode` | `ShimmerChatBuiltin/Generation/Nodes/CallNode.cs` | 65 | 5 | `CallNodeTests.cs` |
| `MergeFragmentsNode` | `ShimmerChatBuiltin/Generation/Nodes/MergeFragmentsNode.cs` | 54 | 3 | `MergeFragmentsNodeTests.cs` |

| 节点 | 用例 |
|------|------|
| **APISelectNode** | `NoSettings_ReturnsFailure`, `APIIndex_Negative1_UsesGlobalSelected`, `APIIndex_Valid_UsesSpecific`, `APIIndex_OutOfRange_FallsBackToZero`, `IsContinuation_OpenAI_SetsPrefix`, `IsContinuation_NonOpenAI_Fails` |
| **AppendChatMessagesNode** | `NoChatMessages_ReturnsSuccess`, `UserMessage_MapsToUser`, `AIMessage_MapsToAssistant`, `RegeneratingMessage_Skipped`, `MultipleMessages_AllAppended` |
| **ToolPresetNode** | `NoPresets_ReturnsFailure`, `EmptyPresetName_UsesDefault`, `NamedPreset_Found_InstantiatesTools`, `NamedPreset_NotFound_ReturnsFailure`, `ToolTypeName_NotFound_Skips`, `EmptyEnabledTools_SuccessEmptyTools` |
| **ToolInstantiateNode** | `EmptyTypeName_ReturnsFailure`, `ValidType_CreatesAndAdds`, `InvalidType_ReturnsFailure`, `CreateThrows_ReturnsFailureWithDetails`, `CreateReturnsNull_ReturnsFailure` |
| **CallNode** | `EmptyPresetId_ReturnsFailure`, `NoPresets_ReturnsFailure`, `PresetNotFound_ReturnsFailure`, `EmptyRootNodeJson_ReturnsFailure`, `ValidPreset_ExecutesChildNode` |
| **MergeFragmentsNode** | `EmptyFragments_ReturnsSuccess`, `MultipleFragments_MergesIntoOne`, `TargetRole_System_UsesSystemFrom` |

### 2.3 其余节点 (后续)

| 节点 | 行数 | 测试重点 |
|------|------|----------|
| `FragmentTrimNode` | ~130 | 裁剪逻辑：保留/删除规则 |
| `MemoryRetrieveNode` | ~120 | Mock 向量检索，验证检索结果追加 |
| `DynPromptNode` | ~60 | 动态 prompt 模板渲染 |
| `PrintNode` / `MessagePrintNode` / `MessagePrintV2Node` / `MessagePrintLatestNode` | 各 ~50-80 | 打印/调试节点 |

---

## Phase 3: ToolCallLoop 测试

**文件**: `ShimmerChatLib/Generation/ToolCallLoop.cs` (92行)
**新增**: `ShimmerChatLib.Tests/ToolCallLoopTests.cs` — 使用 ConfigurableChatClient

| # | 测试用例 | 覆盖场景 |
|---|----------|----------|
| 1 | `RunAsync_NoToolCall_Completes` | LLM 返回 FinishReason.Stop，直接结束 |
| 2 | `RunAsync_SingleToolCall_ExecutesAndCompletes` | 一轮 tool call 后 LLM 正常结束 |
| 3 | `RunAsync_MultipleToolCallRounds` | 多轮 tool call (工具→LLM→工具→LLM→结束) |
| 4 | `RunAsync_MultipleToolsSameRound` | 同一轮多个 tool calls 全部执行 |
| 5 | `RunAsync_MaxRoundsExhausted` | 达到 maxRounds 停止 |
| 6 | `RunAsync_HostOnAssistantComplete_ReturnsFalse_Stops` | host 返回 false 提前终止 |
| 7 | `RunAsync_StreamAccumulation_CorrectAccumulation` | 验证流式内容正确累积 |
| 8 | `RunAsync_ToolError_ContinueOnError` | continueOnToolError=true 时捕获错误继续 |
| 9 | `RunAsync_ToolError_Propagate` | continueOnToolError=false 时异常向上传播 |
| 10 | `RunAsync_Cancellation_Throws` | CancellationToken 触发时终止 |

---

## Phase 4: BuildEnvironment 集成测试

**新增**: `ShimmerChat.Tests/BuildEnvironmentTests.cs`

| # | 测试用例 | 覆盖场景 |
|---|----------|----------|
| 1 | `BuildEnv_DefaultTree_Completes` | 使用默认 modifier tree 构建完整 env |
| 2 | `BuildEnv_CustomTree_ExecutesCorrectly` | 自定义节点树的执行 |
| 3 | `BuildEnv_FallbackTree_Created` | Agent 无 ModifierTreeJson 时使用 fallback |
| 4 | `BuildEnv_NodeFailure_Throws` | 节点失败时 BuildEnvironment 抛出 |

---

## Phase 5: 完整管线集成测试

**新增**: `ShimmerChat.Tests/GenerationPipelineTests.cs` — 使用 ConfigurableChatClient

| # | 测试用例 | 覆盖场景 |
|---|----------|----------|
| 1 | `Pipeline_NoToolCall_SingleResponse` | 无工具调用的简单对话 |
| 2 | `Pipeline_WithToolCall_Complete` | 含工具调用的完整流程 |
| 3 | `Pipeline_ToolCallMultiRound` | 多轮工具调用 |
| 4 | `Pipeline_StreamingDeltas_Accumulated` | 流式分块累积 |
| 5 | `Pipeline_NoAPI_Throws` | 无 API 配置时抛异常 |

---

## Phase 6: GenerationNodeSerializer 测试

**文件**: `ShimmerChat/Singletons/GenerationNodeSerializer.cs` (73行)
**新增**: `ShimmerChat.Tests/GenerationNodeSerializerTests.cs`

| # | 测试用例 |
|---|----------|
| 1 | `Deserialize_ValidJson_ReturnsNode` |
| 2 | `Deserialize_EmptyString_ReturnsNull` |
| 3 | `Serialize_Roundtrip_PreservesTree` |
| 4 | `Deserialize_NestedSequenceNode_CorrectStructure` |
| 5 | `Deserialize_UnknownType_Throws` |

---

## 汇总

| Phase | 内容 | 新增用例 | 新增文件数 |
|-------|------|----------|-----------|
| 0 | ConfigurableChatClient 基础设施 | — | 1 |
| 1 | GenerationTreeExecutor | 4 | 1 |
| 2.1 | 基础节点 (Sequence, Fragment, If) | 17 | 3 |
| 2.2 | 配置/数据节点 (6个) | 30 | 6 |
| 2.3 | 其余节点 | ~15 | 按节点拆分 |
| 3 | ToolCallLoop | 10 | 1 |
| 4 | BuildEnvironment 集成 | 4 | 1 |
| 5 | 完整管线集成 | 5 | 1 |
| 6 | GenerationNodeSerializer | 5 | 1 |
| **总计** | | **~90** | **15+** |

---

## 关键技术决策

1. **伪 API (`ConfigurableChatClient`)** 需支持：预设响应序列、流式分块模拟、FunctionCall 模拟
2. **节点测试**放在 `ShimmerChatBuiltin.Tests` (新建项目)，mock `IKVDataService` + `IToolRegistry`
3. **集成/管线测试**放在 `ShimmerChat.Tests`，mock `IKVDataService`、`IMessageStoreService`
4. **生成管线**的核心外部依赖是 `ILLMAPI` → 用 `ConfigurableChatClient` 替代

---

## 实施记录

### 已完成 (2025-07-15)

| Phase | 内容 | 用例数 | 状态 |
|-------|------|--------|------|
| 0 | ConfigurableChatClient + StubToolCallLoopHost | — | ✅ |
| 1 | GenerationTreeExecutor | 4 | ✅ |
| 2.1 | 基础节点 (SequenceNode, FragmentNode, IfNode) | 17 | ✅ |
| 2.2 | 配置/数据节点 (APISelect, AppendChatMsg, ToolPreset, ToolInstantiate, Call, Merge) | 29 | ✅ |
| 3 | ToolCallLoop (ConfigurableChatClient) | 10 | ✅ |
| **合计** | | **60** | |

**新增文件**:
- `ShimmerChatLib.Tests/ConfigurableChatClient.cs`
- `ShimmerChatLib.Tests/StubToolCallLoopHost.cs`
- `ShimmerChatLib.Tests/GenerationTreeExecutorTests.cs`
- `ShimmerChatLib.Tests/ToolCallLoopTests.cs`
- `ShimmerChatBuiltin.Tests/` (新测试项目)
- `ShimmerChatBuiltin.Tests/Generation/Nodes/NodeTestBase.cs`
- `ShimmerChatBuiltin.Tests/Generation/Nodes/StubAutoCreateToolV2.cs`
- `ShimmerChatBuiltin.Tests/Generation/Nodes/StubToolRegistry.cs`
- `ShimmerChatBuiltin.Tests/Generation/Nodes/SequenceNodeTests.cs`
- `ShimmerChatBuiltin.Tests/Generation/Nodes/FragmentNodeTests.cs`
- `ShimmerChatBuiltin.Tests/Generation/Nodes/IfNodeTests.cs`
- `ShimmerChatBuiltin.Tests/Generation/Nodes/APISelectNodeTests.cs`
- `ShimmerChatBuiltin.Tests/Generation/Nodes/AppendChatMessagesNodeTests.cs`
- `ShimmerChatBuiltin.Tests/Generation/Nodes/ToolPresetNodeTests.cs`
- `ShimmerChatBuiltin.Tests/Generation/Nodes/ToolInstantiateNodeTests.cs`
- `ShimmerChatBuiltin.Tests/Generation/Nodes/CallNodeTests.cs`
- `ShimmerChatBuiltin.Tests/Generation/Nodes/MergeFragmentsNodeTests.cs`

### 待完成

| Phase | 内容 | 预计用例 |
|-------|------|----------|
| 4 | BuildEnvironment 集成测试 | 4 |
| 5 | 完整管线集成测试 | 5 |
| 6 | GenerationNodeSerializer | 5 |
| 2.3 | 其余节点 (FragmentTrim, MemoryRetrieve, DynPrompt, Print 等) | ~15 |
