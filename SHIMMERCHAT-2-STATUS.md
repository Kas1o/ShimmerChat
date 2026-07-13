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
- [x] `NodeResult` — unified error structure (Success / Code / Message / Details / NodeId / NodeName)
- [x] `NodeErrorCodes` — predefined error codes (PRESET_NOT_FOUND, TOOL_NOT_FOUND, API_UNAVAILABLE, etc.)
- [x] `NodeClipboard` — static Copy/Paste for node trees (JSON serialization with ID regeneration)

### Nodes (18+)
- [x] SequenceNode, IfNode, CallNode, FragmentNode
- [x] ToolInstantiateNode, ToolPresetNode, SubAgentToolNode
- [x] APISelectNode, FragmentTrimNode, MemoryRetrieveNode, SubAgentNode
- [x] AppendChatMessagesNode, DynPromptNode, MergeFragmentsNode
- [x] MessagePrintNode, MessagePrintLatestNode, MessagePrintV2Node, PrintNode
- [x] STStyleMacroNode, TransientProbeNode

### Tool V2 Implementations (10)
- [x] FileSystemReadToolV2, WriteToolV2, EditToolV2, BrowseToolV2, OverviewToolV2 (no-arg, via ToolEnvironment)
- [x] InvokeCSharpV2 (no-arg)
- [x] MemoryToolV2, VariableToolV2, SetChatNameToolV2, SubAgentToolV2 (constructor-injected, via dedicated nodes)

### Generation Pipeline
- [x] `GenerationManagerV2` — replaces AIGenerationServiceV1, wired into AgentChatPage
- [x] `GenerateContinuationStreamAsync` — prefix continuation support
- [x] `ToolCallLoop.Accumulate` — single-pass stream accumulation + tool call detection
- [x] CallNode reads presets from KVData directly (no external delegates)
- [x] ToolPresetNode reads tool presets from KVData + delegates to ToolRegistry (which has Lazy caching)
- [x] `ToolRegistry` — unified tool scanning with `Lazy<IReadOnlyList<ToolMetadata>>` cache

### Error Handling (P0-1)
- [x] `NodeResult` with Success/Code/Message/Details/NodeId/NodeName
- [x] `NodeErrorCodes` — 8 predefined error codes
- [x] Nodes use `NodeResult.Failure(...)` instead of throwing / silent-fail (SubAgentNode, ToolPresetNode, etc.)
- [x] UI displays errors via PopupService in AgentChatPage catch block

### SubAgent (P1-2)
- [x] SubAgentNode — all three output modes: `LastMessage`, `FullJson`, `None`
- [x] `SubAgentFormatter.Format()` — switch on mode: LastMessage returns text, FullJson returns JSON array, None returns ""
- [x] SubAgentConfigurationPanel — functional UI with create/select/edit/delete, output mode dropdown, preset/private tree toggle, inline TreeEditor
- [x] SubAgentToolV2 — sub-agent callable as LLM tool

### UI
- [x] `GenerationManagerPage.razor` — tree editor with data-driven nodes
- [x] `TreeEditor.razor` — recursive node rendering via [NodeInfo] metadata
- [x] `GenericNodeEditor.razor` — auto-generated property forms via [NodeProperty]
- [x] `NodeBodyRenderer.cs` — non-generic bridge for DynamicComponent
- [x] `APISelectNodeEditor.razor` — custom editor example (API dropdown)
- [x] `ToolInstantiateNodeEditor.razor`, `SubAgentToolNodeEditor.razor` — custom editors
- [x] NavMenu restructured: Home → Generation Manager → Agents → Misc → Representation → Plugin Panels
- [x] PluginPanelSelectPage deleted; panels listed directly in NavMenu
- [x] ContextManagerPage → redirects to GenerationManagerPage
- [x] `NodeClipboard` — cross-tree copy/paste via JSON serialization
- [x] `GenericNodeEditor` — move up / move down buttons for child node reordering

### Agent Changes
- [x] `Agent.ModifierTreeJson` — private modifier tree, replaces Description-as-system-prompt
- [x] `Agent.CustomToolNames` — removed
- [x] `AgentMigrationService` — auto-migrates old agents on startup
- [x] AgentPage — shows Generation Config section with preset/private tree editing
- [x] CreatePrivateTree → SequenceNode with CallNode("__default__")

### Panel System
- [x] ApiSettingsPage, ToolManager — attributed as PluginPanel, scanned by PluginPanelServiceV1
- [x] ToolManager — rewritten to IToolV2 display + preset save/load
- [x] ToolManager `_default_` preset — rebuilt each startup with all available tools, non-deletable (P2-2)

### Cleanup
- [x] Removed ~37 old files (V1 services, interfaces, Tool implementations, ContextModifiers, etc.)
- [x] Program.cs registration simplified to only new services
- [x] Agent.CustomToolNames deleted from model and tests

---

## Incomplete / Known Issues

### GenerationManagerPage — tree editor UX (P2-1 基本完成)
- [x] Cross-tree copy/paste via `NodeClipboard` (Copy/Paste/Clear/HasContent) + paste button in GenericNodeEditor
- [x] Move up / move down buttons (⇧⇩) on child nodes in GenericNodeEditor
- [x] Drag-and-drop reorder (TreeDragContext, DropStrip, ondragstart/ondrop in TreeEditor + GenericNodeEditor, JS helper in util.js, CSS in node-editor.css).
- [ ] Delete button only appears when `Depth > 0` in TreeEditor — depth-0 nodes can't be deleted directly.

### Streaming UX
`ToolCallLoop.Accumulate` mutates `ResponseEx` in-place. Tool call arguments are accumulated correctly, but thinking content deduplication is naive (string concat).

### Continuation
`GenerateContinuationStreamAsync` sets `prefix:true` on the continuation message's `ChatMessage.CustomProperties`. May not work correctly with all API backends.

### FragmentTrimNode token counting
Relies on static `TokenizerFactory` delegate — currently unset, so token-based trimming is disabled by default. Falls back to message-count trimming. Delegate is never assigned anywhere in the codebase.

### APISelectNode
When `APIIndex = -1` (global default), reads from KVData at execution time. If global API settings change mid-generation, the tree reflects them immediately — arguably correct but may surprise.

### IfNode conditions
Supports `SharedState['key'] == "value"` and `SharedState['key'] != "value"` literal string comparison (case-insensitive). No expression parser (deferred to Stage B).

### FileSystem tools
ToolEnvironment.KVData is set at startup. If KVData becomes unavailable at runtime (e.g., after storage migration), tools will fail silently.

### GenerationTreeExecutor partially unused
`GenerationTreeExecutor.ExecuteAsync()` is now called from `SubAgentToolV2` (line 101). However, `GenerationManagerV2.BuildEnvironment()` and `SubAgentNode` still call `rootNode.ExecuteAsync(context)` directly, bypassing the executor. Their `_executor` / `_treeExecutor` fields remain unused allocations.

### Missing Stage B features
- Enhanced ProbeNode (breakpoint/timing) — basic dump probe exists as `TransientProbeNode` (Console.WriteLine of Fragments/SharedState/Tools/API)
- Expression engine (full condition evaluator)
- Simulation preview (virtual execution, no API call)
- Preset import/export

### Tests
- `ShimmerChatLib.Tests/AgentTests.cs` — updated (CustomToolNames removed)
- `ShimmerChat.Tests/ContextBuilderServiceV1Tests.cs` — deleted (service no longer exists)
- No tests for: GenerationManagerV2, GenerationTreeExecutor, GenerationNodeSerializer, any nodes, SubAgentNode output modes, error paths, ToolPresetNode, IfNode

---

## Human-Reported Issues (from SHIMMERCHAT-2-STATUS-HUMAN.md)

### Static CSS extraction (partially done)
17 `.razor.css` files now exist (AgentChatPage, AgentPage, Home, MainLayout, NavMenu, Message, ToolCallMessage, ToolManager, SubAgentConfigurationPanel, etc.). Some components may still have inline styles that could be extracted.

### Documentation gaps
- No documentation on how to create a custom GenerationNode
- Overall code comment rate is low
- No SKILL.md files for agent infrastructure or module overview

### Localization analyzer needed
Localization key names need an analyzer to detect mismatches between keys used in code and keys defined in `Locales/*.json`.

### Preset export/import not implemented
No UI or service to export/import `GenerationPreset` or `ToolPreset` as standalone files (though `GenerationNodeSerializer.Serialize/Deserialize` provides the technical foundation).

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
    → rootNode.ExecuteAsync(context)  [direct call, NOT via GenerationTreeExecutor]
      → APISelectNode → sets TransientEnv.API
      → ToolPresetNode → loads Tools from ToolRegistry (Lazy-cached)
      → FragmentNode → injects system prompt
      → MemoryRetrieveNode → Qdrant memory search
      → FragmentTrimNode → token/message trimming
    → AppendChatHistory
  → ToolCallLoop.RunAsync (via MainLoopHost)
    → MainLoopHost.BuildPromptBuilder → PromptBuilder + available tools
    → Accumulate → single-pass stream + accumulation
    → Tool call detection + execution loop
```

### Tool preset flow
```
ToolManager.razor → save enabled tools → KVData("ToolPresets", name)
                                              ↓
GenerationManager → user adds ToolPresetNode(presetName: "xxx")
                                              ↓
ToolPresetNode.ExecuteAsync → KVData.Read("ToolPresets", "xxx")
  → ToolRegistry.FindByName(typeName) → CreateInstance → TransientEnv.Tools.Add(...)
```

### SubAgent execution flow
```
SubAgentNode.ExecuteAsync
  → Load SubAgentConfig from KVData
  → ResolveTree: private ModifierTreeJson ?? shared preset from GenerationManager
  → Create isolated PersistentEnv + TransientEnv
  → Execute modifier tree (API select, tool preset, fragments, etc.)
  → ToolCallLoop.RunAsync (via SubAgentLoopHost) with sub-agent tools
  → SubAgentFormatter.Format(mode, promptCtx):
      LastMessage → ctx.LastAssistantContent
      FullJson → JSON array [{role, content, tool_calls}, ...]
      None → ""
  → Inject result into parent TransientEnv.Fragments
```
