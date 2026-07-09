# ShimmerChat 2.0 — Implementation Status

## Completed

### Core Architecture
- [x] `ShimmerChatLib/Generation/` — TransientEnv, PersistentEnv, GenerationEnv, IGenerationNode, GenerationTreeExecutor
- [x] `IToolV2` — new tool abstraction (no Chat/Agent dependency), no-arg and constructor-injected
- [x] `GenerationNodeSerializer` — JSON polymorphic serialization (moved to ShimmerChatLib)
- [x] `NodeInfoAttribute` — per-node metadata (label, icon, color, category path)
- [x] `NodePropertyAttribute` — per-property metadata (label, hint, order)
- [x] `NodeEditorAttribute` — string-based editor registration (avoids circular dependency)
- [x] `ToolEnvironment` — static KVData access for no-arg tools

### Nodes (15)
- [x] SequenceNode, IfNode, CallNode, FragmentNode, AgentRootNode
- [x] ToolInstantiateNode, MemoryToolNode, VariableToolNode, SetChatNameNode, SubAgentToolNode, ToolPresetNode
- [x] APISelectNode, FragmentTrimNode, MemoryRetrieveNode, SubAgentNode

### Tool V2 Implementations (10)
- [x] FileSystemReadToolV2, WriteToolV2, EditToolV2, BrowseToolV2, OverviewToolV2 (no-arg, via ToolEnvironment)
- [x] InvokeCSharpV2 (no-arg)
- [x] MemoryToolV2, VariableToolV2, SetChatNameToolV2, SubAgentToolV2 (constructor-injected, via dedicated nodes)

### Generation Pipeline
- [x] `GenerationManagerV2` — replaces AIGenerationServiceV1, wired into AgentChatPage
- [x] `GenerateContinuationStreamAsync` — prefix continuation support
- [x] `ForwardAccumulated` — single-pass stream accumulation + tool call detection
- [x] CallNode reads presets from KVData directly (no external delegates)
- [x] ToolPresetNode reads tool presets from KVData + auto-scans assemblies

### UI
- [x] `GenerationManagerPage.razor` — tree editor with data-driven nodes
- [x] `TreeEditor.razor` — recursive node rendering via [NodeInfo] metadata
- [x] `GenericNodeEditor.razor` — auto-generated property forms via [NodeProperty]
- [x] `NodeBodyRenderer.cs` — non-generic bridge for DynamicComponent
- [x] `APISelectNodeEditor.razor` — custom editor example (API dropdown)
- [x] NavMenu restructured: Home → Generation Manager → Agents → Misc → Representation → Plugin Panels
- [x] PluginPanelSelectPage deleted; panels listed directly in NavMenu
- [x] ContextManagerPage → redirects to GenerationManagerPage

### Agent Changes
- [x] `Agent.ModifierTreeJson` — private modifier tree, replaces Description-as-system-prompt
- [x] `Agent.CustomToolNames` — removed
- [x] `AgentMigrationService` — auto-migrates old agents on startup
- [x] AgentPage — shows Generation Config section with preset/private tree editing
- [x] CreatePrivateTree → SequenceNode with CallNode("__default__")

### Panel System
- [x] ApiSettingsPage, ToolManager — attributed as PluginPanel, scanned by PluginPanelServiceV1
- [x] ToolManager — rewritten to IToolV2 dispaly + preset save/load

### Cleanup
- [x] Removed ~37 old files (V1 services, interfaces, Tool implementations, ContextModifiers, etc.)
- [x] Program.cs registration simplified to only new services
- [x] Agent.CustomToolNames deleted from model and tests

---

## Incomplete / Known Issues

### ToolPresetNode startup cost
`ToolPresetNode.ResolveToolType` iterates all assemblies and creates an instance for each candidate. This is O(n × m) per lookup. Should cache results in a static dictionary.

### SubAgentConfigurationPanel
Old panel was partially migrated. Independent modifier editing removed (placeholder message). Full redesign pending.

### SubAgentNode output modes
Currently only returns `LastMessage`. `FullJson` and `None` modes from old system not implemented.

### GenerationManagerPage — tree editor UX
- Node deletion only works at top-level via TreeEditor. No "remove" button on leaf nodes shown at Depth 0.
- No drag-and-drop reorder.
- No copy/paste between trees.

### Streaming UX
`ForwardAccumulated` mutates `ResponseEx` in-place. Tool call arguments are accumulated correctly, but thinking content deduplication is naive (string concat).

### Continuation
`GenerateContinuationStreamAsync` sets `prefix:true` on the last assistant fragment. May not work correctly with all API backends.

### FragmentTrimNode token counting
Relies on static `TokenizerFactory` delegate — currently unset, so token-based trimming is disabled by default. Falls back to message-count trimming.

### APISelectNode
When `APIIndex = -1` (global default), reads from KVData at execution time. If global API settings change mid-generation, the tree reflects them immediately — arguably correct but may surprise.

### IfNode conditions
Only supports `SharedState['key'] == "value"` comparison. No expression parser (deferred to Stage B).

### FileSystem tools
ToolEnvironment.KVData is set at startup. If KVData becomes unavailable at runtime (e.g., after storage migration), tools will fail silently.

### Missing Stage B features
- ProbeNode (dump/breakpoint/timing)
- Expression engine (full condition evaluator)
- Simulation preview (virtual execution, no API call)
- Preset import/export

### Tests
- `ShimmerChatLib.Tests/AgentTests.cs` — updated (CustomToolNames removed)
- `ShimmerChat.Tests/ContextBuilderServiceV1Tests.cs` — deleted (service no longer exists)
- No new tests written for GenerationManagerV2, nodes, or serialization

---

## Architecture Notes

### Dependency direction
```
ShimmerChat → ShimmerChatBuiltin → ShimmerChatLib → SharperLLM
     ↑              ↑                    ↑
  Pages,       Concrete nodes,      Interfaces,
  Services     Tool V2 impls        Models, Serializer
```

### Node execution flow
```
GenerateStreamAsync
  → BuildEnvironment
    → Agent.ModifierTreeJson ?? CreateFallbackRoot
    → GenerationTreeExecutor.ExecuteAsync(root, persistent)
      → APISelectNode → sets TransientEnv.API
      → ToolPresetNode → loads Tools from ToolManager preset
      → FragmentNode → injects system prompt
      → MemoryRetrieveNode → Qdrant memory search
      → FragmentTrimNode → token/message trimming
    → AppendChatHistory
  → RunToolCallLoop
    → BuildPromptBuilder → PromptBuilder + available tools
    → ForwardAccumulated → single-pass stream + accumulation
    → Tool call detection + execution loop
```

### Tool preset flow
```
ToolManager.razor → save enabled tools → KVData("ToolPresets", name)
                                              ↓
GenerationManager → user adds ToolPresetNode(presetName: "xxx")
                                              ↓
ToolPresetNode.ExecuteAsync → KVData.Read("ToolPresets", "xxx")
  → ResolveToolType → scan assemblies → Activator.CreateInstance
  → TransientEnv.Tools.Add(...)
```
