# 自定义 API 插件指南

> **前置阅读**：请先阅读 [插件开发指南](PluginDevelopment.md)，了解插件项目创建、程序集加载、本地化等基础知识。

本文介绍如何以插件形式为 ShimmerChat 添加自定义 API 客户端（私有部署服务、非 OpenAI 兼容 API 等）。

---

## 架构概览

自定义 API 的入口是一个**自定义 GenerationNode**——它替代内置的 `APISelectNode`，负责实例化你的客户端并注入管线。

```
你的插件 DLL（只引用 ShimmerChatLib + SharperLLM）
  ├─ 实现 IChatCompletionClient 或 ITextCompletionClient   ← 网络层
  ├─ 自定义 IPreGenerationNode                                ← 选 API 节点
  ├─ 自定义 PluginPanel (@attribute PluginPanelAttribute)   ← 配置面板（可选）
  └─ 自定义 IPluginInitializer                             ← 写入默认配置（可选）
```

插件加载后，用户在 Agent 生成树中将内置 `APISelectNode` 替换为你的节点即可。

**关键接口都在 SharperLLM 中**：

| 程序集 | 包含 |
|--------|------|
| `SharperLLM` | `IChatCompletionClient`、`ITextCompletionClient`、`TextToChatAdapter`、`PromptBuilder`、`ResponseEx` |
| `ShimmerChatLib` | `IPreGenerationNode`、`APISetting`、`PreNodeExecutionContext`、`NodeResult` |

**核心原则**：管线只认识 `IChatCompletionClient`。如果你的 API 是文本补全接口，实现 `ITextCompletionClient`，再用 `TextToChatAdapter` 包装即可。

---

## 步骤 1：选择要实现的接口

| 你的 API 类型 | 实现的接口 | 产出 |
|-------------|-----------|------|
| 原生支持 `/chat/completions`（OpenAI 兼容，含 tool calling） | `IChatCompletionClient` | 直接使用 |
| 原生支持对话，但无 tool calling | `IChatCompletionClient` | `SupportsToolCalling = false` |
| 仅支持文本补全（Kobold、Ollama 等） | `ITextCompletionClient` | 通过 `TextToChatAdapter` 包装 |
| 不支持流式输出 | 任选，`SupportsStreaming = false` | 管线自动用非流式 fallback |

---

## 步骤 2：实现客户端接口

### 2a. 实现 IChatCompletionClient

```csharp
namespace SharperLLM.API
{
    public interface IChatCompletionClient
    {
        Task<ResponseEx> GenerateAsync(PromptBuilder pb);
        IAsyncEnumerable<ResponseEx> GenerateStreamAsync(PromptBuilder pb, CancellationToken cancellationToken);
    }
}
```

**关键类型**：

- **`PromptBuilder`（输入）**：`System`、`Messages`（`(ChatMessage, From)[]`）、`AvailableTools`
- **`ResponseEx`（输出）**：`Body`（`ChatMessage { Content, thinking, toolCalls, CustomProperties }`）、`FinishReason`（`Stop / Length / ContentFilter / FunctionCall / None`）

> 流式输出时，中间 chunk 用 `FinishReason.None`，最后一个 chunk 用 `FinishReason.Stop`（或 `FunctionCall`）。

```csharp
using SharperLLM.API;
using SharperLLM.Util;
using System.Runtime.CompilerServices;
using Newtonsoft.Json;

public class MyChatClient : IChatCompletionClient
{
    private readonly string _endpoint;
    private readonly string _apiKey;

    public MyChatClient(string endpoint, string apiKey)
    {
        _endpoint = endpoint;
        _apiKey = apiKey;
    }

    public async Task<ResponseEx> GenerateAsync(PromptBuilder pb)
    {
        var body = BuildRequestBody(pb, stream: false);
        var json = await PostAsync(_endpoint, body);
        return ParseResponse(json);
    }

    public async IAsyncEnumerable<ResponseEx> GenerateStreamAsync(
        PromptBuilder pb,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var body = BuildRequestBody(pb, stream: true);
        var lines = PostStreamAsync(_endpoint, body, cancellationToken);

        await foreach (var line in lines)
        {
            if (line == "[DONE]") break;
            var chunk = JsonConvert.DeserializeObject<MyChunk>(line);
            yield return new ResponseEx
            {
                Body = new ChatMessage { Content = chunk?.delta?.content ?? "" },
                FinishReason = chunk?.finish_reason == "stop"
                    ? FinishReason.Stop
                    : FinishReason.None
            };
        }
    }

    // --- 你的适配逻辑 ---
    private object BuildRequestBody(PromptBuilder pb, bool stream) { ... }
    private async Task<string> PostAsync(string url, object body) { ... }
    private IAsyncEnumerable<string> PostStreamAsync(string url, object body, CancellationToken ct) { ... }
    private ResponseEx ParseResponse(string json) { ... }
}
```

> **注意**：`GenerateStreamAsync` 的 `cancellationToken` 参数必须加 `[EnumeratorCancellation]` 属性。

### 2b. 实现 ITextCompletionClient（文本补全 API）

```csharp
namespace SharperLLM.API
{
    public interface ITextCompletionClient
    {
        Task<string> GenerateAsync(string prompt);
        IAsyncEnumerable<string> GenerateStreamAsync(string prompt, CancellationToken cancellationToken);
    }
}
```

实现后用 `TextToChatAdapter` 包装，即可接入对话管线：

```csharp
var textClient = new MyTextClient("http://localhost:8080");
var chatClient = new TextToChatAdapter(
    textClient,
    SharperLLM.Util.PromptBuilder.ChatML  // 消息拼接模板
);
```

内置模板：`PromptBuilder.ChatML`、`PromptBuilder.Alpaca`、`PromptBuilder.Mistral`。也可自定义 `SysSeqPrefix/Suffix`、`InputPrefix/Suffix`、`OutputPrefix/Suffix`。

---

## 步骤 3：创建选 API 节点（替代内置 APISelectNode）

这是插件接入管线的**唯一入口**。节点负责构造你的客户端，设置 `TransientEnv.API`，并声明能力标记。

```csharp
using ShimmerChatLib.Generation;
using SharperLLM.API;
using SharperLLM.Util;

[NodeInfo("node.my_api_select", Icon = "⚡", Color = "#60a0e0",
    CategoryKeys = ["category.config"])]
public class MyApiSelectNode : IPreGenerationNode
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "My API";

    [NodeProperty("prop.my_api.url", Order = 0)]
    public string Url { get; set; } = "http://localhost:8080";

    [NodeProperty("prop.my_api.key", Order = 1)]
    public string ApiKey { get; set; } = "";

    public Task<NodeResult> ExecuteAsync(PreNodeExecutionContext context)
    {
        context.Env.Transient.API = new APISetting
        {
            ChatClient = new MyChatClient(Url, ApiKey),
            SupportsStreaming = true,
            SupportsToolCalling = false,
        };

        return Task.FromResult(NodeResult.SuccessResult());
    }
}
```

### 能力标记

`APISetting` 的两个标记决定管线行为：

| 标记 | 作用 |
|------|------|
| `SupportsStreaming = false` | 管线走非流式路径，调用 `GenerateAsync` 一次性输出 |
| `SupportsToolCalling = false` | Tool 列表非空时管线打印警告 |

如实设置即可：Chat API 设 `true`，Text API（通过适配器）设 `false`。

### 进阶：从 KVData 读取用户配置

如果希望用户通过设置面板（而非节点属性）来配置 API 参数：

```csharp
public Task<NodeResult> ExecuteAsync(NodeExecutionContext context)
{
    var kvData = context.Env.Persistent.KVData;
    var url = kvData.Read("MyPlugin", "url") ?? "http://localhost:8080";
    var apiKey = kvData.Read("MyPlugin", "apiKey") ?? "";

    context.Env.Transient.API = new APISetting
    {
        ChatClient = new MyChatClient(url, apiKey),
        SupportsStreaming = true,
        SupportsToolCalling = false,
    };

    return Task.FromResult(NodeResult.SuccessResult());
}
```

这种方式配合**步骤 4**的设置面板，用户可以在 UI 中修改配置。

---

## 步骤 4：创建设置面板（可选）

添加一个 UI 面板，让用户在设置页中配置 API 参数：

```razor
@* MyPlugin/MyApiSettingsPanel.razor *@
@using ShimmerChatLib.Panel
@using ShimmerChatLib.Interface

@attribute [PluginPanelAttribute("panel.my_api_settings", "panel.my_api_settings.desc",
    PanelDisplayPlace.Settings)]

@inject IKVDataService KVData

<h3>My API Settings</h3>
<div class="su-group">
    <label>URL</label>
    <input class="shimmer-input" @bind="_url" />
    <label>API Key</label>
    <input type="password" class="shimmer-input" @bind="_apiKey" />
    <button class="btn btn-primary" @onclick="Save">Save</button>
</div>

@code {
    private string _url = "";
    private string _apiKey = "";

    protected override void OnInitialized()
    {
        _url = KVData.Read("MyPlugin", "url") ?? "http://localhost:8080";
        _apiKey = KVData.Read("MyPlugin", "apiKey") ?? "";
    }

    private void Save()
    {
        KVData.Write("MyPlugin", "url", _url);
        KVData.Write("MyPlugin", "apiKey", _apiKey);
    }
}
```

### 配置默认值（PluginInitializer）

如果希望在首次加载时自动写入默认配置：

```csharp
using ShimmerChatLib.Interface;

public class MyPluginInitializer : IPluginInitializer
{
    private readonly IKVDataService _kvData;

    public MyPluginInitializer(IKVDataService kvData) { _kvData = kvData; }

    public Task InitializeAsync()
    {
        if (string.IsNullOrEmpty(_kvData.Read("MyPlugin", "url")))
        {
            _kvData.Write("MyPlugin", "url", "http://localhost:8080");
            _kvData.Write("MyPlugin", "apiKey", "");
        }
        return Task.CompletedTask;
    }
}
```

---

## 步骤 5：使用方式

1. 部署插件 DLL 到 `Plugins/MyPlugin/`
2. 重启 ShimmerChat，插件自动加载
3. 在设置页配置 API 参数（如果有面板）
4. 编辑 Agent 的生成树：拖入 `MyApiSelectNode`，移除默认的 `APISelectNode`
5. 开始对话——管线会使用你的自定义客户端

---

## 关于"继续"功能

内置的续写功能由 `APISelectNode` 处理——它检测 `SharedState["IsContinuation"]`，对 OpenAI/DeepSeek 类型的最后一条 AI 消息添加 `prefix: true`。如果你自建了选 API 节点且希望支持续写，在 `ExecuteAsync` 中添加类似逻辑：

```csharp
if (context.Env.Transient.SharedState.TryGetValue("IsContinuation", out var v) && v is true)
{
    var msgs = context.Env.Transient.SharedState["ChatMessages"] as List<Message>;
    var lastAi = msgs?.LastOrDefault(m => m.sender == Sender.AI);
    if (lastAi != null)
    {
        lastAi.message.CustomProperties ??= new();
        lastAi.message.CustomProperties["prefix"] = true;
    }
}
```

如果你的 API 不支持续写，应返回 `NodeResult.Failure` 明确拒绝，而不是静默跳过——否则用户会以为续写成功，实际管线行为不可预期：

```csharp
if (context.Env.Transient.SharedState.TryGetValue("IsContinuation", out var v) && v is true)
{
    return Task.FromResult(NodeResult.Failure(
        NodeErrorCodes.ApiUnavailable,
        "MyApiSelect: Continuation is not supported by this API.",
        nodeId: Id, nodeName: Name));
}
```

---

## 检查清单

- [ ] 已阅读 [插件开发指南](PluginDevelopment.md)
- [ ] 实现了 `IChatCompletionClient` 或 `ITextCompletionClient`
- [ ] 流式方法的 `CancellationToken` 加了 `[EnumeratorCancellation]`
- [ ] `FinishReason` 在流式输出末尾正确设置为 `Stop`
- [ ] 创建了自定义 `IPreGenerationNode`，在 `ExecuteAsync` 中设置 `TransientEnv.API`
- [ ] 能力标记如实设置（`SupportsStreaming`、`SupportsToolCalling`）
- [ ] 如果用了 `ITextCompletionClient`，用 `TextToChatAdapter` 包装并选择合适的消息模板
- [ ] 可选：添加了 `PluginPanel` 供用户配置 API 参数
- [ ] 可选：添加了 `IPluginInitializer` 写入默认 KVData 配置
