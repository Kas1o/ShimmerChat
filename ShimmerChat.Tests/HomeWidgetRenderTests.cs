using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using ShimmerChatBuiltin.HomeWidgets;
using ShimmerChatLib;
using ShimmerChatLib.HomeWidget;
using ShimmerChatLib.Interface;

namespace ShimmerChat.Tests;

/// <summary>
/// 首页 Widget 的渲染冒烟测试：宿主只传 Context，Widget 必须能实际渲染出
/// 绑定状态、空状态与操作按钮，而不是只在元数据层面可通过。
/// </summary>
public class HomeWidgetRenderTests
{
    private static HomeWidgetContext CreateContext(Dictionary<string, string>? settings = null)
        => new()
        {
            Instance = new HomeWidgetInstance
            {
                InstanceId = Guid.NewGuid(),
                WidgetId = "agent-quick",
                Settings = settings ?? new Dictionary<string, string>()
            },
            SaveSettingsAsync = _ => Task.FromResult(true),
            RequestRefreshAsync = () => Task.CompletedTask
        };

    private static async Task<string> RenderAsync<TComponent>(HomeWidgetContext context, Agent? agent = null)
        where TComponent : IComponent
    {
        using var provider = BuildServices(agent);
        await using var renderer = new HtmlRenderer(provider, NullLoggerFactory.Instance);

        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = await renderer.RenderComponentAsync<TComponent>(
                ParameterView.FromDictionary(new Dictionary<string, object?> { ["Context"] = context }));
            return output.ToHtmlString();
        });
    }

    private static ServiceProvider BuildServices(Agent? agent = null)
    {
        var loc = new Mock<ILocService>();
        loc.SetupGet(l => l.CurrentCulture).Returns("zh-CN");
        loc.SetupGet(l => l.SupportedCultures).Returns(["zh-CN"]);
        loc.Setup(l => l[It.IsAny<string>()]).Returns<string>(key => key);
        loc.Setup(l => l.Format(It.IsAny<string>(), It.IsAny<object[]>()))
            .Returns<string, object[]>((key, args) => string.Format(key, args));

        var kv = new MemoryKV();
        if (agent != null)
        {
            agent.Save(kv);
            Agent.AddAgentToAll(agent, kv);
        }

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(loc.Object);
        services.AddSingleton<IKVDataService>(kv);
        services.AddSingleton(Mock.Of<IChatCreationService>());
        services.AddSingleton(Mock.Of<IGitHubReleaseService>());
        services.AddSingleton<NavigationManager>(new TestNavigationManager());
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task AgentQuickWidget_WithBoundAgent_RendersBothActions()
    {
        var agent = Agent.Create("Test Agent", "A helpful test agent.", "hello");
        var html = await RenderAsync<AgentQuickWidget>(
            CreateContext(new Dictionary<string, string> { ["agentGuid"] = agent.Guid.ToString() }), agent);

        html.Should().Contain("Test Agent");
        html.Should().Contain("A helpful test agent.");
        html.Should().Contain("widget.agent.new_chat");
        html.Should().Contain("widget.agent.continue_chat");
        html.Should().Contain("widget.agent.no_chats");
    }

    [Fact]
    public async Task AgentQuickWidget_WithoutBoundAgent_RendersAgentPicker()
    {
        var agent = Agent.Create("Test Agent", "intro");
        var html = await RenderAsync<AgentQuickWidget>(CreateContext(), agent);

        html.Should().Contain("widget.agent.choose_title");
        html.Should().Contain("Test Agent");
        html.Should().Contain("widget.agent.choose_placeholder");
    }

    [Fact]
    public async Task AgentQuickWidget_ConfiguredAgentMissing_ShowsExplicitWarning()
    {
        var agent = Agent.Create("Test Agent", "intro");
        var html = await RenderAsync<AgentQuickWidget>(
            CreateContext(new Dictionary<string, string> { ["agentGuid"] = Guid.NewGuid().ToString() }), agent);

        html.Should().Contain("widget.agent.agent_missing");
    }

    [Fact]
    public async Task GettingStartedWidget_RendersWipMarkerAndSteps()
    {
        var html = await RenderAsync<GettingStartedWidget>(CreateContext());

        html.Should().Contain("WIP");
        html.Should().Contain("widget.getting_started.wip_note");
        html.Should().Contain("widget.getting_started.step1_title");
        html.Should().Contain("widget.getting_started.step4_title");
    }

    private sealed class MemoryKV : IKVDataService
    {
        private readonly Dictionary<(string Space, string Key), string> _data = new();

        public string? Read(string spaceId, string key)
            => _data.TryGetValue((spaceId, key), out var value) ? value : null;

        public void Write(string spaceId, string key, string value)
            => _data[(spaceId, key)] = value;
    }

    private sealed class TestNavigationManager : NavigationManager
    {
        public TestNavigationManager()
        {
            Initialize("http://localhost/", "http://localhost/");
        }

        protected override void NavigateToCore(string uri, bool forceLoad)
        {
        }
    }
}
