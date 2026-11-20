using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using ShimmerChat.Components.Pages;
using ShimmerChatLib.Interface;
using ShimmerChatLib.Panel;

namespace ShimmerChat.Tests;

/// <summary>
/// 插件面板宿主页（PluginPanelPage）的托管形态守卫。
/// <para>
/// Flush 面板（ConfigShell 契约）必须去掉外层标题与卡片外壳，只保留面板自身的
/// cfg-page-main 操作面板，避免双标题并让操作栏无缝铺满内容区；
/// 普通面板保持原有「标题 + 卡片」外壳；未注册面板必须显式回退，不允许静默空白。
/// </para>
/// </summary>
public sealed class PluginPanelPageRenderTests
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

    /// <summary>记录刷新次数的最小 IPluginPanelService 假实现。</summary>
    private sealed class FakePanelService : IPluginPanelService
    {
        private readonly List<PluginPanelInfo> _panels;

        public int RefreshCount { get; private set; }

        public FakePanelService(params PluginPanelInfo[] panels) => _panels = [.. panels];

        public void LoadAllPluginPanels()
        {
        }

        public List<PluginPanelInfo> GetPluginPanels() => new(_panels);

        public Type? GetPluginPanelType(string panelName) => GetPluginPanel(panelName)?.PanelType;

        public PluginPanelInfo? GetPluginPanel(string panelName)
            => _panels.FirstOrDefault(p => p.NameKey.Equals(panelName, StringComparison.OrdinalIgnoreCase));

        public void RefreshPluginPanels() => RefreshCount++;
    }

    private sealed class FlushTestPanel : ComponentBase
    {
        protected override void BuildRenderTree(RenderTreeBuilder builder)
            => builder.AddMarkupContent(0, "<div class=\"cfg-page cfg-page-main\">FLUSH-PANEL-MARKER</div>");
    }

    private sealed class LegacyTestPanel : ComponentBase
    {
        protected override void BuildRenderTree(RenderTreeBuilder builder)
            => builder.AddMarkupContent(0, "<div class=\"legacy-panel\">LEGACY-PANEL-MARKER</div>");
    }

    private static async Task<string> RenderAsync(IServiceProvider services, string panelName)
    {
        await using var renderer = new HtmlRenderer(services, NullLoggerFactory.Instance);

        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = await renderer.RenderComponentAsync<PluginPanelPage>(
                ParameterView.FromDictionary(new Dictionary<string, object?> { ["PanelName"] = panelName }));
            return output.ToHtmlString();
        });
    }

    private static ServiceProvider BuildServices(IPluginPanelService panelService)
    {
        var services = new ServiceCollection();
        services.AddSingleton(panelService);
        services.AddSingleton<ILocService>(new FakeLoc());
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task FlushPanel_RendersWithoutOuterTitleOrCard()
    {
        var panel = new PluginPanelInfo(
            "panel.test_flush", "panel.test_flush.desc", null, 0, typeof(FlushTestPanel),
            PanelDisplayPlace.Settings, flush: true);
        using var services = BuildServices(new FakePanelService(panel));

        var html = await RenderAsync(services, "panel.test_flush");

        // 面板本体直接渲染，双标题与卡片外壳全部消失。
        html.Should().Contain("FLUSH-PANEL-MARKER");
        html.Should().Contain("cfg-page-main");
        html.Should().NotContain("plugin-panel-container");
        html.Should().NotContain("panel-header");
        html.Should().NotContain("panel-title");
        html.Should().NotContain("panel-content");
    }

    [Fact]
    public async Task NonFlushPanel_KeepsOuterTitleAndCard()
    {
        var panel = new PluginPanelInfo(
            "panel.test_legacy", "panel.test_legacy.desc", null, 1, typeof(LegacyTestPanel),
            PanelDisplayPlace.Settings);
        using var services = BuildServices(new FakePanelService(panel));

        var html = await RenderAsync(services, "panel.test_legacy");

        // 未声明 Flush 的面板维持原有外壳：外层标题仍由宿主负责。
        html.Should().Contain("plugin-panel-container");
        html.Should().Contain("panel-title");
        html.Should().Contain("panel-content");
        html.Should().Contain("panel.test_legacy");
        html.Should().Contain("LEGACY-PANEL-MARKER");
    }

    [Fact]
    public async Task UnknownPanel_RefreshesThenShowsExplicitFallback()
    {
        var panelService = new FakePanelService();
        using var services = BuildServices(panelService);

        var html = await RenderAsync(services, "panel.missing");

        // 未注册 Id 必须显式回退：先刷新插件列表，再展示可读错误，而不是空白页。
        panelService.RefreshCount.Should().BeGreaterThan(0);
        html.Should().Contain("panel-not-found");
        html.Should().Contain("plugin_panel.not_found");
        html.Should().Contain("plugin_panel.not_found_msg");
        html.Should().Contain("panel.missing");
    }
}
