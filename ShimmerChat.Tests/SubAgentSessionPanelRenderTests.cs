using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;
using SharperLLM.Util;
using ShimmerChatBuiltin.SubAgent;
using ShimmerChatBuiltin.SubAgent.Sessions;
using ShimmerChatLib;
using ShimmerChatLib.Interface;

namespace ShimmerChat.Tests;

/// <summary>
/// SubAgent 会话面板（<see cref="ShimmerChatBuiltin.SubAgent.SubAgentSessionPanel"/>）的渲染守卫：
/// 面板必须能在空索引、已有会话与坏索引三种状态下挂载，并渲染出筛选 / 自动刷新入口，
/// 坏索引不得静默显示成「暂无会话」。
/// </summary>
public sealed class SubAgentSessionPanelRenderTests
{
    private sealed class FakeLoc : ILocService
    {
        public string CurrentCulture => "zh-CN";

        public IReadOnlyList<string> SupportedCultures => ["zh-CN"];

        public string this[string key] => key;

        public string Format(string key, params object[] args)
            => args.Length == 0 ? key : $"{key}:{string.Join(",", args.Select(a => a?.ToString() ?? string.Empty))}";

        public string Format(string key, params (string name, object value)[] args)
            => args.Length == 0 ? key : $"{key}:{string.Join(",", args.Select(a => a.value?.ToString() ?? string.Empty))}";

        public void SetCulture(string culture)
        {
        }
    }

    private sealed class FakeDisplay : IMessageDisplayService
    {
        public bool DebugModeEnabled { get; set; }

        public MarkupString Render(string markdownText, Chat? chat = null, Agent? agent = null)
            => new(markdownText);

        public void SaveDebugModeSetting()
        {
        }
    }

    private sealed class FakeDebugOutput : IDebugOutputService
    {
        public List<string> Writes { get; } = [];

        public void Write(string source, string category, string content) => Writes.Add($"{source}/{category}: {content}");

        public List<ShimmerChatLib.Models.DebugOutputEntry> GetEntries(int skip, int take, string? sourceFilter = null, string? categoryFilter = null, string? keyword = null, DateTime? from = null, DateTime? to = null)
            => [];

        public int GetCount(string? sourceFilter = null, string? categoryFilter = null, string? keyword = null, DateTime? from = null, DateTime? to = null)
            => 0;

        public List<string> GetSources() => [];

        public List<string> GetCategories() => [];

        public bool DeleteEntry(Guid id) => false;

        public int TrimToRecent(int keep) => 0;

        public int DeleteOlderThan(DateTime cutoffUtc) => 0;

        public void ClearAll()
        {
        }
    }

    private sealed class FakeKVData : IKVDataService
    {
        private readonly Dictionary<(string Space, string Key), string> _data = new();

        public string? Read(string spaceId, string key)
            => _data.TryGetValue((spaceId, key), out var value) ? value : null;

        public void Write(string spaceId, string key, string value) => _data[(spaceId, key)] = value;
    }

    private sealed class FakeJsRuntime : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
            => throw new NotSupportedException($"静态渲染不应调用 JS interop：{identifier}");

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
            => throw new NotSupportedException($"静态渲染不应调用 JS interop：{identifier}");
    }

    private static ServiceProvider BuildServices(FakeKVData kv, FakeDebugOutput debug)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IKVDataService>(kv);
        services.AddSingleton<ILocService>(new FakeLoc());
        services.AddSingleton<IMessageDisplayService>(new FakeDisplay());
        services.AddSingleton<IDebugOutputService>(debug);
        services.AddSingleton<IJSRuntime>(new FakeJsRuntime());
        return services.BuildServiceProvider();
    }

    private static async Task<string> RenderAsync(IServiceProvider services)
    {
        await using var renderer = new HtmlRenderer(services, NullLoggerFactory.Instance);

        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = await renderer.RenderComponentAsync<ShimmerChatBuiltin.SubAgent.SubAgentSessionPanel>(
                ParameterView.Empty);
            return output.ToHtmlString();
        });
    }

    private static void SeedSession(
        FakeKVData kv, FakeDebugOutput debug, SubAgentConfig config, string title, string task)
    {
        using var scope = SubAgentSessionStore.StartNew(
            kv, debug, config, title, UserMessage(task), Guid.NewGuid(), Guid.NewGuid()).Scope!;
        scope.Complete($"output for {title}");
    }

    private static Message UserMessage(string text)
        => SubAgentSessionMessages.FromPrompt(new ChatMessage { Content = text }, PromptBuilder.From.user);

    [Fact]
    public async Task EmptyIndex_RendersEmptyStateAndRefreshControls()
    {
        var kv = new FakeKVData();
        using var services = BuildServices(kv, new FakeDebugOutput());

        var html = await RenderAsync(services);

        html.Should().Contain("panel_sub_agent_sessions.title");
        html.Should().Contain("panel_sub_agent_sessions.refresh");
        html.Should().Contain("panel_sub_agent_sessions.auto_refresh");
        html.Should().Contain("panel_sub_agent_sessions.empty");
        // 没有会话时不渲染筛选区，避免空列表上出现无用控件
        html.Should().NotContain("panel_sub_agent_sessions.search_placeholder");
    }

    [Fact]
    public async Task Sessions_RenderGroupedListWithStatusBadgesAndFilterUi()
    {
        var kv = new FakeKVData();
        var debug = new FakeDebugOutput();

        SeedSession(kv, debug, new SubAgentConfig { Name = "researcher" }, "课题调研", "整理资料");
        SeedSession(kv, debug, new SubAgentConfig { Name = "writer" }, "写成稿", "写一份总结");

        using var services = BuildServices(kv, debug);
        var html = System.Net.WebUtility.HtmlDecode(await RenderAsync(services));

        html.Should().Contain("researcher");
        html.Should().Contain("writer");
        html.Should().Contain("课题调研");
        html.Should().Contain("panel_sub_agent_sessions.search_placeholder");
        html.Should().Contain("panel_sub_agent_sessions.filter_all");
        html.Should().Contain("panel_sub_agent_sessions.sort_label");
        html.Should().Contain("panel_sub_agent_sessions.status_completed");
        // 每个配置一组：分组标题上显示该组会话数量
        html.Should().Contain("su-badge");
        debug.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task CorruptedIndex_ShowsExplicitErrorInsteadOfEmptyState()
    {
        var kv = new FakeKVData();
        kv.Write(SubAgentSessionStore.SpaceId, SubAgentSessionStore.IndexKey, "{ broken");
        var debug = new FakeDebugOutput();

        using var services = BuildServices(kv, debug);
        var html = await RenderAsync(services);

        html.Should().Contain("panel_sub_agent_sessions.index_error");
        html.Should().Contain("corrupted");
        // 坏索引必须留下排障痕迹，不能只靠界面提示
        debug.Writes.Should().Contain(w => w.Contains("Session index unavailable"));
    }
}
