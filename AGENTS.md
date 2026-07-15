# AGENTS.md — ShimmerChat 开发指南

## 1. 项目简介

ShimmerChat 是一个功能丰富的 AI 聊天应用，基于 **.NET 10 + Blazor Server**，支持多种 LLM API（OpenAI、Ollama、Kobold）接入。核心架构围绕 **可编辑的生成节点树（GenerationNode Tree）** 重构。

**目标用户：** 需要高度自定义 AI 生成管线的用户，可以通过可视化节点编辑器自由编排 system prompt、聊天历史注入、工具调用、子代理等生成流程。

---

## 2. 代码风格

### 核心原则：严格报错，禁止隐藏错误

- **不得** 在 `catch` 块中吞掉异常而不做任何处理。
- **不得** 通过隐式修补（如自动填充缺省值、静默降级）掩盖上游数据或配置错误。
- 在开发节点时，所有可能失败的操作必须返回明确的成功/失败结果。使用 `NodeResult.SuccessResult()` / `NodeResult.Failure(code, message)` 模式。
- 在开发节点时，失败时必须提供可追踪的错误码（`NodeErrorCodes` 中定义）和用户可读的错误信息。
- 在边界层（如序列化、插件加载）可以 catch 后记录日志并返回 null/空列表，但必须输出完整的 `LogError`。

### 具体规范

- **节点执行**：`ExecuteAsync` 返回 `NodeResult`，调用方检查 `Success`；失败时逐级向上汇报，不吞掉。
- **序列化**：反序列化失败时 `LogError` 并返回 `null`，调用方负责检查并返回 `NodeResult.Failure`。
- **插件加载**：类型扫描失败时 `LogError`（包含 `LoaderExceptions`），继续加载其余类型；不因一个坏插件拖垮整个系统。
- **Tool 调用**：`continueOnToolError=false` 时工具异常向上抛出；`=true` 时返回 `[Tool error]` 文本但要记录日志。

---

## 3. 项目结构

```
ShimmerChat/
├── SharperLLM/              # LLM 连接库（独立子模块，非必要不修改）
├── ShimmerChatLib/           # 共享库：模型、接口、生成管线核心抽象、共享组件
├── ShimmerChat/              # 主项目：Blazor Server 宿主、服务实现、UI 页面
├── ShimmerChatBuiltin/       # 内置插件：默认节点、内置工具、内置面板
├── src-tauri/                # Tauri 桌面发布项目
├── ShimmerChatLib.Tests/     # 共享库单元测试
├── ShimmerChat.Tests/        # 主项目单元测试
├── ShimmerChatBuiltin.Tests/ # 内置插件测试
└── Docs/                     # 开发文档
```

### 3.1 SharperLLM — LLM 连接库

独立的 .NET 类库（git 子模块）。**非必要不修改。**

| 模块 | 职责 |
|------|------|
| `API/` | `IChatCompletionClient` 接口及其实现（OpenAI、Ollama、Kobold）；`ITextCompletionClient` 接口及 `TextToChatAdapter` |
| `FunctionCalling/` | `Tool` / `ToolCall` / `ToolParameter` 类型定义；`ToolPromptParser` 工具声明格式化 |
| `Agents/` | `ToolCallLoopRunner` 通用 Tool Call 循环（流式/非流式）；`IToolExecutor` / `IPromptContext` 抽象 |
| `Util/` | `ChatMessage`、`PromptBuilder`、`ResponseEx` 等基础类型 |
| `Managers/` | `ConversationManager`、`RolePlayManager` 高层封装 |

### 3.2 ShimmerChatLib — 共享库

提供模型、接口和生成管线核心抽象。**不引用 ShimmerChat 主项目。**

| 目录/文件 | 职责 |
|-----------|------|
| `Models/` | UI 模型（`Theme`、`PopupOptions` 等） |
| `Interface/` | 所有服务接口（`IGenerationManagerV2`、`IPluginLoaderService`、`IKVDataService` 等） |
| `Generation/` | **生成管线核心**：`IGenerationNode`、`GenerationEnv`、`GenerationTreeExecutor`、`ToolCallLoop`、`IToolV2`、`IToolRegistry`、节点元数据 Attribute 等 |
| `Components/` | 跨项目共享的 Blazor 组件 |
| `Panel/` | 插件面板基础设施 |
| `Context/` | 共享上下文 |
| `Chat.cs` | 聊天对象模型 |
| `Agent.cs` | 代理对象模型（含 `ModifierTreeJson`） |
| `Message.cs` | 消息模型（多版本支持） |
| `Sender.cs` | 发送者枚举 |

### 3.3 ShimmerChat — 主项目

Blazor Server 宿主 + 所有服务实现 + UI 页面。

| 目录 | 职责 |
|------|------|
| `Singletons/` | 所有服务的具体实现 |
| `Components/Pages/` | Blazor 页面 |
| `Components/Layout/` | 布局组件 |
| `Components/SubComponents/` | 可复用子组件 |
| `wwwroot/` | 静态资源（CSS、JS、图标） |
| `Locales/` | 本地化 JSON（zh-CN、en-US） |

### 3.4 ShimmerChatBuiltin — 内置插件

所有内置节点、内置工具、内置面板。通过 `IPluginLoaderService` 自动发现，与外部插件具有同等地位。

| 目录 | 职责 |
|------|------|
| `Generation/Nodes/` | 22 个内置节点（SequenceNode、FragmentNode、CallNode、APISelectNode、ToolPresetNode 等） |
| `API/` | API 配置面板 |
| `DynPrompt/` | 动态提示系统 |
| `FileSystem/` | 文件系统工具集 |
| `Memory/` | 记忆系统 |
| `Variable/` | 变量管理 |
| `SubAgent/` | 子代理节点 |
| `RegexModifier/` | 消息显示修改器 |

### 3.5 src-tauri — 桌面发布

Tauri v2 桌面壳，将 Blazor Server 包装为桌面应用。通过 `SHIMMER_READY:{url}` stdout 信号完成启动握手。

---

## 4. 核心概念：GenerationNode 系统

### 4.1 概述

ShimmerChat 2.0 的核心创新是 **可编辑的生成节点树**。每次 AI 生成不再是简单的 "发消息 → 等回复"，而是执行一个用户可配置的节点树来构建完整的生成管线。

### 4.2 架构流程

```
Agent.ModifierTreeJson (节点树 JSON)
    │
    ▼
GenerationNodeSerializer.Deserialize()
    │
    ▼
IGenerationNode (根节点)
    │
    ▼  node.ExecuteAsync(context)
    │   节点修改 GenerationEnv
    │   ├── TransientEnv.Fragments  (上下文片段列表)
    │   ├── TransientEnv.Tools     (可用工具实例)
    │   └── TransientEnv.API       (选中的 API 配置)
    │
    ▼
Fragments → PromptBuilder.Messages
    │
    ▼
IChatCompletionClient.GenerateStreamAsync()  ← ToolCallLoop 循环
```

### 4.3 核心类型

#### IGenerationNode — 节点接口

```csharp
public interface IGenerationNode
{
    string Id { get; }                                    // 唯一标识（GUID）
    string Name { get; set; }                             // 用户可编辑名称
    Task<NodeResult> ExecuteAsync(NodeExecutionContext context);  // 执行逻辑
}
```

每个节点通过 `ExecuteAsync` 修改 `context.Env`（`GenerationEnv`），返回 `NodeResult`。

#### GenerationEnv — 生成环境

- **`TransientEnv`**（临时）：每次生成重新构建。
  - `Fragments` — `List<ContextSegment>`，最终转换为 PromptBuilder 的消息列表。
  - `Tools` — `List<IToolV2>`，本次生成可用的工具。
  - `API` — 当前使用的 `APISetting`（IChatCompletionClient + 能力标记）。
  - `SharedState` — `Dictionary<string, object>`，节点间共享状态。
- **`PersistentEnv`**（持久）：对话/Agent 级别不变。
  - `KVData`、`ChatGuid`、`AgentGuid`、`ToolRegistry`、`Serializer`、`LocService`、`DebugOutput`。

#### NodeResult — 执行结果

```csharp
public class NodeResult
{
    bool Success;          // 是否成功
    string? Code;          // 错误码（PRESET_NOT_FOUND、TOOL_NOT_FOUND 等）
    string? Message;       // 用户可读错误信息
    string? Details;       // 技术详情
    string? NodeId;        // 失败节点 ID
    string? NodeName;      // 失败节点名称
}
```

预定义错误码见 `NodeErrorCodes`：`PresetNotFound`、`ToolNotFound`、`ApiUnavailable`、`DataMissing`、`ParseError`、`ServiceError`、`ConfigNotFound`、`Cancelled`。

#### ContextSegment — 上下文片段

```csharp
public class ContextSegment
{
    ChatMessage Message;            // 消息内容
    PromptBuilder.From From;        // system / user / assistant
    Type? SourceType;               // 来源节点类型
    Dictionary<string, object> Metadata;
}
```

节点将 `ContextSegment` 追加到 `TransientEnv.Fragments`，生成时这些片段成为 `PromptBuilder.Messages`。

### 4.4 节点元数据系统（三个 Attribute）

| Attribute | 作用域 | 用途 |
|-----------|--------|------|
| `[NodeInfo]` | 类 | 节点显示名、图标、颜色、分类、描述 |
| `[NodeProperty]` | 属性 | 属性标签、提示、排序、多行编辑 |
| `[NodeEditor]` | 类 | 指定自定义编辑器组件（可选，默认使用 `GenericNodeEditor`） |

只要实现了 `IGenerationNode` 并标记了 `[NodeInfo]`，节点即自动出现在编辑器的添加菜单中。

### 4.5 关键内置节点

| 节点 | 职责 |
|------|------|
| `SequenceNode` | 顺序执行子节点，支持 Repeat |
| `FragmentNode` | 向 Fragments 追加自定义片段（role + content） |
| `CallNode` | 通过 PresetId 加载并执行预设 |
| `AppendChatMessagesNode` | 从 Chat 加载历史消息并追加到 Fragments |
| `APISelectNode` | 选择 API 配置 |
| `ToolPresetNode` | 根据预设名加载工具列表 |
| `ToolInstantiateNode` | 实例化单个工具 |
| `IfNode` / `AdvancedIfNode` | 条件分支 |
| `SubAgentNode` | 嵌套子代理调用 |
| `DynPromptNode` | 动态模板提示 |

### 4.6 Tool 系统

- **`IToolV2`** — 工具执行接口：`GetDefinition()` + `ExecuteAsync(input)`。
- **`IAutoCreateToolV2 : IToolV2`** — 可通过通用节点自动创建的工具。提供 `static abstract NameKey`、`DescriptionKey`、`CategoryKeys` 元数据和 `static abstract Create(PersistentEnv)` 工厂方法。
- **`IToolRegistry`** — 扫描所有 `IAutoCreateToolV2` 实现，提供按名称/类型查找和实例创建。
- **`ToolCallLoop`** — 流式 Tool Call 循环：累积流式响应 → 检测 FunctionCall → 执行工具 → 重建 env → 下一轮，直到 LLM 不再请求工具调用或达到最大轮次（默认 50）。

---

## 5. 服务详解

### 5.1 IGenerationManagerV2 / GenerationManagerV2

**生成管线总控。** 注册为 Singleton。

流程：
1. `BuildEnvironment` — 解析 Agent 的 `ModifierTreeJson`（或创建默认回退树），执行完整节点树，构建 `GenerationEnv`。
2. `GenerateStreamAsync` — 取出 Fragments 构建 `PromptBuilder`，通过 `ToolCallLoop` 执行流式 Tool Call 循环。
3. `MainLoopHost`（内部类）— 实现 `IToolCallLoopHost`，桥接 `ToolCallLoop` 和 ShimmerChat 的 UI 回调。

### 5.2 IPluginLoaderService / PluginLoaderServiceV1

**插件加载与类型发现。** 扫描 `Plugins/` 目录下所有插件程序集，连同 BuiltinAssembly、HostAssembly、LibAssembly 一起提供统一的类型扫描接口。

- `LoadImplementations<T>()` — 从所有程序集创建实现实例。
- `GetTypesWithAttribute<T>()` — 按 Attribute 过滤类型。
- `GetImplementingTypes(Type)` — 获取接口的所有具体实现。
- `InitializePluginsAsync()` — 执行所有 `IPluginInitializer`。

### 5.3 IGenerationNodeSerializer / GenerationNodeSerializer

**节点树序列化/反序列化。** 使用 Newtonsoft.Json + `TypeNameHandling.Objects`，通过 `SerializationBinder` 白名单类型解析（防止反序列化攻击）。

### 5.4 INodeTypeCatalog / NodeTypeCatalog

**节点类型目录。** 扫描所有 `IGenerationNode` 实现，提取 `[NodeInfo]` 元数据，供添加节点菜单使用。

### 5.5 IToolRegistry / ToolRegistry

**工具注册表。** 扫描所有 `IAutoCreateToolV2` 实现，提供按名称/类型名查找和通过 `Create(PersistentEnv)` 工厂实例化。

### 5.6 IPluginPanelService / PluginPanelServiceV1

**插件面板服务。** 扫描 `[PluginPanelAttribute]` 标记的类型，构建面板列表供 UI 导航。

### 5.7 IPopupService / PopupService

**弹窗服务。** 基于 `TaskCompletionSource<bool>` 的异步弹窗，返回用户确认/取消结果。

### 5.8 IMessageDisplayService / MessageDisplayServiceV1

**消息渲染服务。** 共享的 Markdig `MarkdownPipeline`，支持可插拔的 `IMessageRenderModifier` 链（正则替换、格式化等）。调试模式下可返回完整渲染流程中间结果。

### 5.9 IThemeService / ThemeServiceV2

**主题服务。** 注册为 Scoped。管理主题的 CRUD、切换、导入/导出。内置 Light/Dark 两套主题，支持通过 KVData 持久化自定义主题。主题 token（CSS 变量值）在 `Theme` 对象中定义。

### 5.10 ILocService / LocService

**本地化服务。** 轻量级 Key → 显示字符串查找。从所有程序集的嵌入资源 `Locales.{culture}.json` 加载翻译。支持 `zh-CN`、`en-US`。先加载 `en-US` 作为 fallback 再覆盖目标语言。语言设置通过 KVData 持久化，切换即时生效。

### 5.11 IDebugOutputService / DebugOutputService

**调试输出服务。** 结构化日志写入 LiteDB 集合 `debug_output`，按时间戳、来源、类别索引。支持分页查询、过滤、删除。

### 5.12 IKVDataService

**KV 数据存储。** 两种实现：
- **LocalFileStorageKVData** — JSON 文件存储。
- **LiteDBKVData** — LiteDB 嵌入式数据库存储。

通过 `appsettings.json` 中 `KVDataStorage` 配置节选择。支持启动时自动迁移（`AutoMigrateOnStartup`）。

### 5.13 IMessageStoreService

**消息持久化存储。** 两种实现：
- **FileMessageStoreService** — 文件存储。
- **LiteDBMessageStoreService** — LiteDB 存储。

与 KVData 共用同一个 `LiteDatabase` 实例。支持按 Chat GUID 分区的消息 CRUD 操作。

### 5.14 IKVDataMigrationService / KVDataMigrationMarker

**数据迁移服务。** `KVDataMigrationService` 执行 LocalFile ↔ LiteDB 的批量迁移。`KVDataMigrationMarker` 记录迁移完成状态，防止重复迁移。

### 5.15 IMessageStoreMigrationService

**消息存储迁移服务。** 执行 File ↔ LiteDB 的消息批量迁移。

### 5.16 IAgentMigrationService

**Agent 格式迁移。** 将 1.0 版本的 Agent 数据（旧格式的 Description、API 配置等）迁移到 2.0 格式（`ModifierTreeJson` 节点树）。启动时自动执行。

---

## 6. 快速参考

### 添加新节点的步骤

1. 创建类，实现 `IGenerationNode`。
2. 标记 `[NodeInfo("node.xxx", Icon = "✦", Color = "...", CategoryKeys = [...])]`。
3. 为可编辑属性标记 `[NodeProperty("prop.xxx")]`。
4. 实现 `ExecuteAsync` → 修改 `context.Env.Transient` → 返回 `NodeResult`。
5. （可选）在 `Locales/` 中添加对应的本地化条目。
6. 节点自动出现在编辑器菜单中，无需手动注册。

### 关键接口总览

| 接口 | 所在库 | 注册方式 |
|------|--------|----------|
| `IGenerationNode` | ShimmerChatLib | 自动发现 |
| `IAutoCreateToolV2` | ShimmerChatLib | 自动发现 |
| `IPluginInitializer` | ShimmerChatLib | 自动发现 |
| `IMessageRenderModifier` | ShimmerChatLib | 自动发现 |
| `IPluginLoaderService` | ShimmerChatLib | Singleton |
| `IGenerationManagerV2` | ShimmerChatLib | Singleton |
| `IKVDataService` | ShimmerChatLib | Singleton（按配置选实现） |
| `IMessageStoreService` | ShimmerChatLib | Singleton（按配置选实现） |
| `IThemeService` | ShimmerChatLib | Scoped |
| `ILocService` | ShimmerChatLib | Singleton |
| `IDebugOutputService` | ShimmerChatLib | Singleton |
