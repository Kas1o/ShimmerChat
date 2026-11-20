using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ShimmerChat.Singletons;
using ShimmerChatLib.Interface;
using ShimmerChatLib.Panel;

namespace ShimmerChat.Tests;

/// <summary>
/// 插件面板发现链路对 Flush 标记的传递：
/// 特性上的声明必须原样进入 PluginPanelInfo，宿主页只读这一份信息，
/// 不维护第二份「哪些面板要无缝铺满」的手工清单。
/// </summary>
public class PluginPanelServiceFlushTests
{
    [PluginPanel("panel.svc_flush", "panel.svc_flush.desc", PanelDisplayPlace.Settings, Flush = true)]
    private sealed class FlushTestPanel : ComponentBase
    {
    }

    [PluginPanel("panel.svc_legacy", "panel.svc_legacy.desc", PanelDisplayPlace.Settings)]
    private sealed class LegacyTestPanel : ComponentBase
    {
    }

    private static PluginPanelServiceV1 CreateService(params Type[] panelTypes)
    {
        var loader = new Mock<IPluginLoaderService>();
        loader.Setup(l => l.GetTypesWithAttribute<PluginPanelAttribute>())
            .Returns(panelTypes.ToList());
        return new PluginPanelServiceV1(loader.Object, NullLogger<PluginPanelServiceV1>.Instance);
    }

    [Fact]
    public void FlushFlag_PropagatesIntoPanelInfo()
    {
        var service = CreateService(typeof(FlushTestPanel), typeof(LegacyTestPanel));

        var panels = service.GetPluginPanels();

        panels.Single(p => p.NameKey == "panel.svc_flush").Flush.Should().BeTrue();
        panels.Single(p => p.NameKey == "panel.svc_legacy").Flush.Should().BeFalse();
    }

    [Fact]
    public void GetPluginPanel_ReturnsInfoForBothFlagStates()
    {
        var service = CreateService(typeof(FlushTestPanel), typeof(LegacyTestPanel));

        var flush = service.GetPluginPanel("panel.svc_flush");
        var legacy = service.GetPluginPanel("panel.svc_legacy");

        flush.Should().NotBeNull();
        flush!.Flush.Should().BeTrue();
        flush.PanelType.Should().Be(typeof(FlushTestPanel));
        legacy.Should().NotBeNull();
        legacy!.Flush.Should().BeFalse();
    }

    [Fact]
    public void GetPluginPanel_UnknownName_ReturnsNull()
    {
        var service = CreateService(typeof(LegacyTestPanel));

        service.GetPluginPanel("panel.not_registered").Should().BeNull();
        service.GetPluginPanelType("panel.not_registered").Should().BeNull();
    }
}
