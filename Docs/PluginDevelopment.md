# ShimmerChat 插件开发指南

## 概述

ShimmerChat 支持通过外部 DLL 扩展功能。插件放在 `Plugins/` 目录下，启动时自动发现和加载。

插件可以添加：
- **插件初始化器**（PluginInitializer）— 启动时自动初始化
- **自定义工具**（Function Calling Tool）
- **自定义生成节点**（GenerationNode）
- **消息渲染修改器**（MessageRenderModifier）
- **UI 面板**（PluginPanel）

---

## 程序集加载机制（ALC）

插件使用独立的 `AssemblyLoadContext`（`isCollectible: true`）加载，与宿主 AppDomain 隔离。

### 共享程序集（回退到默认 ALC）

以下程序集始终走默认 ALC，保证类型一致（`IsAssignableFrom` 正常工作）：

| 程序集 | 说明 |
|--------|------|
| `ShimmerChatLib` | 接口/抽象/模型 |
| `SharperLLM` | LLM API 抽象 |
| `Newtonsoft.Json` | JSON 序列化 |
| `System.*` | .NET 运行时 |
| `Microsoft.*` | ASP.NET Core |

### 插件私有依赖

不在上述列表中的程序集由插件的 ALC 从插件目录解析，不同插件的依赖相互隔离。

### 加载流程

```
启动 → 枚举 Plugins/*/plugin.json
     → 每个插件创建独立 PluginLoadContext(isCollectible: true)
     → 按 assembiles 列表 LoadFromAssemblyPath
     → 依赖解析时：共享程序集 → null（回退默认 ALC）
                    私有依赖 → 从插件目录加载
     → 卸载时 Dispose ALC → 该插件所有程序集释放
```

---

## 1. 获取开发依赖

### 方式 A：直接引用 DLL（快速开始）

从 ShimmerChat 发布包中复制 `ShimmerChatLib.dll` 和 `SharperLLM.dll`，在插件项目中直接引用。

### 方式 B：Git Clone 源码引用

```bash
git clone https://github.com/Kas1o/ShimmerChat.git
```

插件项目通过 `ProjectReference` 引用：

```xml
<ProjectReference Include="..\ShimmerChat\ShimmerChatLib\ShimmerChatLib.csproj" />
```

**注意**：插件只应引用 `ShimmerChatLib`，不要引用 `ShimmerChat` 宿主或 `ShimmerChatBuiltin`。

---

## 2. 创建插件项目

### 项目文件 (.csproj)

如果插件包含 Blazor UI 组件（面板、自定义编辑器），必须使用 **Razor 类库**：

```xml
<Project Sdk="Microsoft.NET.Sdk.Razor">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\ShimmerChat\ShimmerChatLib\ShimmerChatLib.csproj" />
  </ItemGroup>

  <!-- 本地化文件嵌入 -->
  <ItemGroup>
    <EmbeddedResource Include="Locales\*.json" />
  </ItemGroup>
</Project>
```

如果只包含纯逻辑（无 UI），使用普通类库即可：

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
  </PropertyGroup>
  ...
</Project>
```

### plugin.json 清单

每个插件目录下必须放置 `plugin.json`：

```json
{
  "name": "MyCoolPlugin",
  "version": "1.0.0",
  "description": "一个示例插件",
  "assembly": "MyCoolPlugin.dll"
}
```

| 字段 | 必填 | 说明 |
|------|------|------|
| `name` | 是 | 插件名称 |
| `assembly` | 是 | 入口程序集文件名，相对于插件目录 |
| `version` | 否 | 版本号 |
| `description` | 否 | 描述 |

### 部署

在 `.csproj` 中让 `plugin.json` 自动复制到输出目录：

```xml
<ItemGroup>
  <None Update="plugin.json">
    <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
  </None>
</ItemGroup>
```

然后添加 Post-Build 事件，将整个输出目录复制到 Plugins：

```xml
<Target Name="DeployToPlugins" AfterTargets="Build">
  <ItemGroup>
    <OutputFiles Include="$(OutputPath)**\*" Exclude="$(OutputPath)**\*.pdb" />
  </ItemGroup>
  <RemoveDir Directories="$(SolutionDir)ShimmerChat\Plugins\MyCoolPlugin" />
  <Copy SourceFiles="@(OutputFiles)"
        DestinationFolder="$(SolutionDir)ShimmerChat\Plugins\MyCoolPlugin\%(RecursiveDir)" />
</Target>
```

部署后的目录结构：

```
ShimmerChat/
└── Plugins/
    └── MyCoolPlugin/
        ├── plugin.json
        ├── MyCoolPlugin.dll
        └── SomeDependency.dll     ← ALC 自动解析
```

---

## 3. 插件初始化 (IPluginInitializer)

### 概述

插件可以实现 `IPluginInitializer` 接口，在应用启动时自动执行初始化逻辑。宿主通过 `IPluginLoaderService.InitializePluginsAsync()` 扫描所有实现并依次调用。

```csharp
using ShimmerChatLib.Interface;

public class MyPluginInitializer : IPluginInitializer
{
    private readonly IKVDataService _kvData;

    public MyPluginInitializer(IKVDataService kvData)
    {
        _kvData = kvData;
    }

    public Task InitializeAsync()
    {
        // 确保默认配置存在
        if (string.IsNullOrEmpty(_kvData.Read("MyPlugin", "config")))
        {
            _kvData.Write("MyPlugin", "config", "{}");
        }
        return Task.CompletedTask;
    }
}
```

### 初始化时机

初始化在 `app.Build()` 之后、`app.Run()` 之前执行：

```
启动 → 程序集加载 → DI 容器构建 → 迁移 → 插件初始化 → 中间件配置 → 开始监听请求
```

此时所有单例服务（`IKVDataService`、`IToolRegistry` 等）已可用，但尚未处理任何 HTTP 请求。

### 适合做什么

| 场景 | 示例 |
|------|------|
| 写入默认 KVData | 确保默认工具预设存在（`IsDefault = true`） |
| 创建必需的目录/文件 | 插件的工作目录、默认配置文件 |
| 注册初始数据 | 首次启动才需要写入的默认值 |

### 不适合做什么

| 场景 | 原因 |
|------|------|
| 耗时操作（网络请求、大文件 I/O） | 会阻塞启动，延长启动时间 |
| 依赖用户输入的操作 | 此时还没有用户会话 |
| 依赖 Scoped / Transient 服务 | DI 根容器中只有 Singleton 可用 |
| 修改其他插件的数据 | 违反插件隔离原则 |

### 默认预设的推荐做法

对于需要在启动时创建的默认预设（如工具预设、配置预设等）：

- **首次创建，后续由用户编辑**：初始化器仅在预设不存在时创建默认值，不会覆盖用户的后续修改。
- **使用 GUID + IsDefault 标记**：预设通过 GUID 标识，`IsDefault` 标记唯一默认预设。生成节点通过 `IsDefault` 查找，与名称解耦。
- **统一列表存储**：所有预设序列化为一个 `List<T>` 存入 KVData，避免以名称为键的散落存储。

参考 `ShimmerChatBuiltin/Misc/DefaultToolPresetInitializer.cs`：检查预设列表中是否已有 `IsDefault = true` 的项，没有则创建一个空的默认预设。

### 依赖注入

初始化器通过 `ActivatorUtilities.CreateInstance` 创建，支持构造函数注入。可用的服务：

- `IKVDataService` — 读写插件数据
- `IToolRegistry` — 查询可用工具
- 其他已注册的 Singleton 服务

> 不要在初始化器中注入 Scoped 或 Transient 服务，会导致生命周期异常。

---

## 4. 自定义工具

### 方式 A：自动创建工具（IAutoCreateToolV2）

推荐用于无外部构造依赖的工具。实现后由 `ToolRegistry` 自动扫描注册，无需在 Agent 树中配置。

```csharp
using ShimmerChatLib.Generation;
using SharperLLM.FunctionCalling;

public class MyTool : IAutoCreateToolV2
{
    // ========== static abstract 元数据 ==========
    public static string NameKey => "tool.my_tool";
    public static string DescriptionKey => "tool.my_tool.desc";
    public static string[] CategoryKeys => ["category.utility"];

    public static IAutoCreateToolV2 Create(PersistentEnv env)
    {
        // env 提供 KVData、ChatGuid、AgentGuid 等持久化服务
        return new MyTool(env.KVData, env.AgentGuid);
    }

    // ========== 实例成员 ==========
    private readonly IKVDataService _kvData;
    private readonly Guid _agentGuid;

    private MyTool(IKVDataService kvData, Guid agentGuid)
    {
        _kvData = kvData;
        _agentGuid = agentGuid;
    }

    // 无参构造供反射使用（不会被调用，但接口扫描需要）
    public MyTool() { }

    public Tool GetDefinition()
    {
        return new Tool
        {
            name = "my_tool",
            description = "执行自定义操作",
            parameters = new List<(ToolParameter, bool)>
            {
                (new ToolParameter
                {
                    name = "input",
                    type = ParameterType.String,
                    description = "输入文本"
                }, true)
            }
        };
    }

    public async Task<string> ExecuteAsync(string input)
    {
        MyArgs args;
        try { args = Newtonsoft.Json.JsonConvert.DeserializeObject<MyArgs>(input) ?? new(); }
        catch { return "Error: Invalid arguments. Expected JSON with 'input' field."; }

        if (string.IsNullOrWhiteSpace(args.input))
            return "Error: 'input' is required.";

        // 正常业务逻辑
        return $"处理完成: {args.input}";
    }

    private class MyArgs
    {
        public string? input { get; set; }
    }
}
```

### 工具错误处理原则

| 错误类型 | 处理方式 | 示例 |
|----------|----------|------|
| **输入参数错误** | `return "Error: ..."` | JSON 格式错误、缺少必填字段、值不在枚举范围 |
| **配置/环境错误** | `throw` 异常，终止生成 | API Key 未配置、依赖服务不可用、数据文件损坏 |

- 输入错误返回错误字符串，LLM 能看到并尝试修正参数后重新调用。
- 环境错误抛出异常，由执行管道捕获后报告给用户，因为 LLM 无法自行修复这类问题。

```csharp
// 正确示例
var kvData = env.KVData ?? throw new InvalidOperationException("KVData is not available");
var config = kvData.Read("MyPlugin", "config");
if (string.IsNullOrEmpty(config))
    return "Error: Plugin not configured. Please set up in Settings.";
```

### 方式 B：手动创建工具（IToolV2 + 专用节点）

用于需要特殊构造参数或由 Agent 树中的专用节点手动实例化的工具。

1. 实现 `IToolV2`（不需要 `IAutoCreateToolV2`）
2. 编写对应的 `IGenerationNode`，在 `ExecuteAsync` 中创建工具实例并加入 `context.Env.Transient.Tools`

参见 `SubAgentToolV2` + `SubAgentToolNode` 的组合模式。

---

## 5. 自定义生成节点

> **详见 [生成节点与编辑器开发指南](NodeDevelopment.md)** — 节点实现、属性编辑器、容器子列表、拖拽排序、自定义编辑器、上下文对象等完整文档。

### 快速示例

```csharp
using ShimmerChatLib.Generation;
using SharperLLM.Util;

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

### 核心概念速览

| 概念 | 说明 |
|------|------|
| `IGenerationNode` | 节点接口：`Id`、`Name`、`ExecuteAsync` |
| `[NodeInfo]` | 类级元数据：标签 Key、图标、颜色、分类 |
| `[NodeProperty]` | 属性级元数据：标签 Key、提示、排序。支持 string/int/float/bool/enum |
| `[NodeEditor(typeof(...))]` | 指定自定义 Blazor 编辑器组件 |
| `List<IGenerationNode>` | 标记 `[NodeProperty]` 后生成子节点列表 UI，支持拖拽排序 |
| `IGenerationNode` (单个) | 标记 `[NodeProperty]` 后生成单节点槽 UI |
| `DropStrip` | 可复用放置条组件，自定义编辑器用它来支持拖放 |
| `TreeDragContext` | 级联共享的拖拽状态，自动流入所有子组件 |

### 关键上下文对象速览

```csharp
// context.Env.Persistent — 跨生成持久化
KVData        // IKVDataService  键值存储
ChatGuid      // Guid            当前对话
AgentGuid     // Guid            当前 Agent
ToolRegistry  // IToolRegistry   工具注册表
Serializer    // IGenerationNodeSerializer

// context.Env.Transient — 每次生成重建
Fragments     // List<ContextSegment>  要发送给 LLM 的上下文
Tools         // List<IToolV2>         可用工具
API           // APISetting?           API 配置
SharedState   // Dictionary<string, object>  跨节点共享状态
```

---

## 6. 自定义消息渲染修改器

修改器在消息发送到 UI 渲染前对内容做后处理。所有插件中的修改器由 `MessageDisplayServiceV1` 自动发现。

```csharp
using ShimmerChatLib.Interface;

public class MyModifier : IMessageRenderModifier
{
    public MessageRenderModifierInfo Info => new()
    {
        Name = "My Modifier",
        Description = "对消息内容做自定义处理"
    };

    public string Modify(string content, string input, Chat? chat, Agent? agent)
    {
        // content: 原始消息内容
        // input:   用户在 UI 中配置的参数（自定义语义）
        // chat:    当前对话（可能为 null）
        // agent:   当前 Agent（可能为 null）
        return content.Replace("foo", "bar");
    }
}
```

- 实现 `IMessageRenderModifier` 即可，无需额外注册。
- 用户在设置界面选择并激活修改器，可配置 `input` 参数。

---

## 7. UI 面板

### 基本面板（Settings 位置）

```razor
@using ShimmerChatLib.Panel

@attribute [PluginPanelAttribute("panel.my_panel", "panel.my_panel.desc")]

<div class="my-panel">
    <h3>我的面板</h3>
    <p>这是一个自定义设置面板</p>
</div>
```

### Agent 级面板

```razor
@attribute [PluginPanelAttribute("panel.my_agent_panel", "panel.my_agent_panel.desc",
    PanelDisplayPlace.Agent)]
@* 自动注入 AgentGuid 和 EventHandler *@

@code {
    [Parameter] public Guid AgentGuid { get; set; }
    [Parameter] public Action<IChatPanelEventHandler> EventHandlerReg { get; set; } = null!;
}
```

### 对话级面板

```razor
@attribute [PluginPanelAttribute("panel.my_chat_panel", "panel.my_chat_panel.desc",
    PanelDisplayPlace.Chat)]
@* 自动注入 ChatGuid, AgentGuid, EventHandlerReg *@

@code {
    [Parameter] public Guid ChatGuid { get; set; }
    [Parameter] public Guid AgentGuid { get; set; }
    [Parameter] public Action<IChatPanelEventHandler> EventHandlerReg { get; set; } = null!;
}
```

### PanelDisplayPlace 对照

| 值 | 位置 | 注入参数 |
|----|------|---------|
| `Settings` | 全局设置页 | 无额外参数 |
| `Agent` | Agent 编辑页 | `AgentGuid`, `EventHandlerReg` |
| `Chat` | 聊天页侧栏 | `ChatGuid`, `AgentGuid`, `EventHandlerReg` |

---

## 8. 本地化

### 文件结构

```
Locales/
├── zh-CN.json
└── en-US.json
```

### Key 命名规范

| 前缀 | 用途 | 示例 |
|------|------|------|
| `tool.{name}` | 工具名称 | `"tool.my_tool"` |
| `tool.{name}.desc` | 工具描述 | `"tool.my_tool.desc"` |
| `node.{name}` | 节点标签 | `"node.my_fragment"` |
| `node.{name}.desc` | 节点描述 | `"node.my_fragment.desc"` |
| `prop.{node}.{prop}` | 属性标签 | `"prop.my_fragment.text"` |
| `prop.{node}.{prop}.hint` | 属性提示 | `"prop.my_fragment.text.hint"` |
| `panel.{name}` | 面板名称 | `"panel.my_panel"` |
| `panel.{name}.desc` | 面板描述 | `"panel.my_panel.desc"` |
| `category.{name}` | 分类名称 | `"category.utility"` |

### 示例

```json
// zh-CN.json
{
  "tool.my_tool": "我的工具",
  "tool.my_tool.desc": "执行自定义操作",
  "node.my_fragment": "我的片段",
  "prop.my_fragment.text": "文本内容",
  "prop.my_fragment.text.hint": "要注入的文本",
  "panel.my_panel": "我的面板",
  "panel.my_panel.desc": "自定义设置面板",
  "category.utility": "工具"
}
```

```json
// en-US.json
{
  "tool.my_tool": "My Tool",
  "tool.my_tool.desc": "Performs custom operations",
  "node.my_fragment": "My Fragment",
  "prop.my_fragment.text": "Text Content",
  "prop.my_fragment.text.hint": "Text to inject",
  "panel.my_panel": "My Panel",
  "panel.my_panel.desc": "Custom settings panel",
  "category.utility": "Utilities"
}
```

文件必须设为 `EmbeddedResource`（在 `.csproj` 中配置），启动时 `LocService` 自动从所有已加载程序集发现并加载。

---

## 9. 调试技巧

1. 将插件 DLL 和依赖放入 `Plugins/{PluginName}/`，启动宿主
2. 检查控制台日志：`[ALC] Failed to load ...` 表示加载问题
3. 工具未出现 → 检查 `IAutoCreateToolV2` 实现是否完整，本地化 Key 是否存在
4. 节点未出现 → 检查 `[NodeInfo]` 是否正确标记，`CategoryKeys` 是否匹配 UI 筛选
5. 面板未出现 → 检查 `[PluginPanelAttribute]` 的 `PanelDisplayPlace` 是否匹配当前页面
6. 类型异常 → 确保插件只引用 `ShimmerChatLib`，共享依赖自动走默认 ALC
