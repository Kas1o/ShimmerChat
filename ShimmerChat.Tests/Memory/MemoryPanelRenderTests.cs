using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;
using ShimmerChat.Singletons.VectorStore;
using ShimmerChatBuiltin.Memory;
using ShimmerChatLib.Interface;
using ShimmerChatLib.Models;
using ShimmerChatLib.Panel;

namespace ShimmerChat.Tests.Memory;

/// <summary>
/// 用真实 Blazor 渲染器覆盖 Agent / Chat 面板：宿主传参缺失、参数类型不符或初始化异常
/// 都会在这里直接暴露，而不是等到用户打开页面才发现。
/// </summary>
public sealed class MemoryPanelRenderTests : IDisposable
{
    private readonly string _root;
    private readonly MemoryRecordStore _store;
    private readonly InMemoryVectorStore _vectors = new();
    private readonly MemoryKV _kv = new();

    public MemoryPanelRenderTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "shimmer-panel-tests", Guid.NewGuid().ToString("N"));
        _store = new MemoryRecordStore(Path.Combine(_root, "records.db"));
    }

    private ServiceProvider BuildServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IKVDataService>(_kv);
        services.AddSingleton<IVectorStoreService>(_vectors);
        services.AddSingleton(new VectorStoreConfig());
        services.AddSingleton<ILocService>(new FakeLoc());
        services.AddLogging();
        return services.BuildServiceProvider();
    }

    private static async Task<string> RenderAsync<T>(IServiceProvider services, Dictionary<string, object?> parameters)
        where T : IComponent
    {
        await using var renderer = new HtmlRenderer(services, NullLoggerFactory.Instance);

        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = await renderer.RenderComponentAsync<T>(ParameterView.FromDictionary(parameters));
            return output.ToHtmlString();
        });
    }

    [Fact]
    public async Task AgentPanel_RendersRecordsForAgentPartition()
    {
        using var scope = MemoryStoreProvider.Override(_store);
        var agentGuid = Guid.NewGuid();
        _store.Add(MemoryPartition.ForAgent(agentGuid), "memory-alpha", "tag1", null);

        await using var services = BuildServices();
        var html = await RenderAsync<AgentMemoryPanel>(services, new Dictionary<string, object?>
        {
            ["AgentGuid"] = agentGuid,
            ["EventHandlerReg"] = (Action<IChatPanelEventHandler>)(_ => { }),
            ["PanelContext"] = null
        });

        html.Should().Contain("memory-alpha");
        html.Should().Contain("panel.memory.agent.title");
    }

    [Fact]
    public async Task AgentPanel_WithoutConfiguration_StillRendersList()
    {
        using var scope = MemoryStoreProvider.Override(_store);
        var agentGuid = Guid.NewGuid();
        _store.Add(MemoryPartition.ForAgent(agentGuid), "visible-without-config", string.Empty, null);

        await using var services = BuildServices();
        var html = await RenderAsync<AgentMemoryPanel>(services, new Dictionary<string, object?>
        {
            ["AgentGuid"] = agentGuid,
            ["EventHandlerReg"] = (Action<IChatPanelEventHandler>)(_ => { }),
            ["PanelContext"] = null
        });

        html.Should().Contain("visible-without-config");
    }

    [Fact]
    public async Task ChatPanel_RendersWithoutConfiguration()
    {
        using var scope = MemoryStoreProvider.Override(_store);

        await using var services = BuildServices();
        var html = await RenderAsync<ChatMemoryPanel>(services, new Dictionary<string, object?>
        {
            ["ChatGuid"] = Guid.NewGuid(),
            ["AgentGuid"] = Guid.NewGuid(),
            ["EventHandlerReg"] = (Action<IChatPanelEventHandler>)(_ => { }),
            ["PanelContext"] = null
        });

        html.Should().Contain("panel.memory.chat.title");
        html.Should().Contain("panel.memory.chat.no_recall");
    }

    public void Dispose()
    {
        _store.Dispose();
        try
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch
        {
            // 清理失败不影响断言
        }
    }

    [Fact]
    public async Task SettingsPanel_RendersWithHostServices()
    {
        using var scope = MemoryStoreProvider.Override(_store);

        await using var services = BuildServices();
        var html = await RenderAsync<MemorySettingsPanel>(services, new Dictionary<string, object?>());

        html.Should().Contain("panel.memory.title");
        html.Should().Contain("panel.memory.tab.backend");
    }

    [Fact]
    public async Task BackendSection_ShowsConfigFileAndSnippetInsteadOfEditableFields()
    {
        var contentRoot = Path.Combine(_root, "content");
        Directory.CreateDirectory(contentRoot);
        File.WriteAllText(Path.Combine(contentRoot, "appsettings.json"), "{}");

        await using var services = BuildBackendServices(contentRoot, new VectorStoreConfig());
        var html = await RenderAsync<MemoryBackendSection>(services, new Dictionary<string, object?>());

        html.Should().Contain("appsettings.json");
        html.Should().Contain("su-pre");
        // 配置值以代码块呈现，而不是看起来能改的输入框
        html.Should().Contain("LiteDb");
        html.Should().Contain("RootPath");
        html.Should().NotContain("<input");
        // 修改方法必须写清楚
        html.Should().Contain("panel.memory.backend_step_edit");
        html.Should().Contain("panel.memory.backend_values_provider");
        html.Should().Contain("panel.memory.backend_step_restart");
        html.Should().Contain("panel.memory.backend_data_root");
    }

    [Fact]
    public async Task BackendSection_InMemory_ExplainsNothingIsWrittenToDisk()
    {
        var contentRoot = Path.Combine(_root, "content-memory");
        Directory.CreateDirectory(contentRoot);

        await using var services = BuildBackendServices(
            contentRoot,
            new VectorStoreConfig { Provider = "InMemory" });
        var html = await RenderAsync<MemoryBackendSection>(services, new Dictionary<string, object?>());

        html.Should().Contain("panel.memory.backend_no_disk");
        html.Should().NotContain("panel.memory.backend_data_root");
    }

    private ServiceProvider BuildBackendServices(string contentRoot, VectorStoreConfig config)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILocService>(new FakeLoc());
        services.AddSingleton(config);
        services.AddSingleton<IHostEnvironment>(new FakeHost { ContentRootPath = contentRoot });
        services.AddSingleton<IJSRuntime>(new FakeJs());
        services.AddLogging();
        return services.BuildServiceProvider();
    }

    private sealed class MemoryKV : IKVDataService
    {
        private readonly Dictionary<(string Space, string Key), string> _values = new();

        public string? Read(string spaceId, string key)
            => _values.TryGetValue((spaceId, key), out var value) ? value : null;

        public void Write(string spaceId, string key, string value)
            => _values[(spaceId, key)] = value;
    }

    private sealed class FakeLoc : ILocService
    {
        public string CurrentCulture => "zh-CN";

        public IReadOnlyList<string> SupportedCultures => ["zh-CN"];

        public string this[string key] => key;

        public string Format(string key, params object[] args) => key;

        public string Format(string key, params (string name, object value)[] args) => key;

        public void SetCulture(string culture)
        {
        }
    }

    private sealed class FakeHost : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Test";
        public string ApplicationName { get; set; } = "ShimmerChat";
        public string ContentRootPath { get; set; } = string.Empty;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class FakeJs : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
            => throw new NotSupportedException("渲染阶段不应调用 JS");

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
            => throw new NotSupportedException("渲染阶段不应调用 JS");
    }
}
