using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging.Abstractions;
using ShimmerChatLib.HomeWidget;
using ShimmerChatLib.Interface;

namespace ShimmerChat.Tests;

/// <summary>
/// 首页 Widget 的契约守卫。
/// <para>
/// 宿主用 <c>DynamicComponent</c> 只传入 <c>Context</c> 一个参数；Widget Id 会被
/// 首页布局持久化引用；默认 Widget 还会在首次运行时自动出现在首页。因此每个 Widget
/// 都必须实现 <see cref="IHomeWidget"/>、是 Blazor 组件、只声明 <c>Context</c> 参数、
/// 且名称/描述的本地化 Key 真实存在。
/// </para>
/// </summary>
public class HomeWidgetContractTests
{
    private static readonly Assembly HostAssembly = typeof(Program).Assembly;
    private static readonly Assembly LibAssembly = typeof(IHomeWidget).Assembly;
    private static readonly Assembly BuiltinAssembly = typeof(ShimmerChatBuiltin.Target).Assembly;

    private static HomeWidgetRegistry CreateRegistry()
        => new(new StubPluginLoader(LibAssembly, BuiltinAssembly, HostAssembly), NullLogger<HomeWidgetRegistry>.Instance);

    private static IReadOnlyList<HomeWidgetInfo> DiscoverWidgets() => CreateRegistry().GetAll();

    public static TheoryData<string> WidgetIds()
    {
        var data = new TheoryData<string>();
        foreach (var widget in DiscoverWidgets())
            data.Add(widget.Id);
        return data;
    }

    [Fact]
    public void Discovery_FindsBuiltinHomeWidgets()
    {
        var ids = DiscoverWidgets().Select(widget => widget.Id).ToList();

        ids.Should().Contain("agent-quick");
        ids.Should().Contain("getting-started");
        ids.Should().Contain("github-release");

        // 数量守卫：反射加载失败会静默丢类型，从而让契约测试失去意义。
        DiscoverWidgets().Should().HaveCount(3);
    }

    [Theory]
    [MemberData(nameof(WidgetIds))]
    public void Widget_DeclaresTheContextParameterProvidedByHost(string widgetId)
    {
        var widget = DiscoverWidgets().Single(w => w.Id == widgetId);

        typeof(ComponentBase).IsAssignableFrom(widget.ComponentType).Should().BeTrue();
        typeof(IHomeWidget).IsAssignableFrom(widget.ComponentType).Should().BeTrue();

        var parameter = widget.ComponentType
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .SingleOrDefault(p => p.GetCustomAttribute<ParameterAttribute>() != null && p.Name == "Context");

        parameter.Should().NotBeNull($"{widget.ComponentType.FullName} 必须声明 [Parameter] HomeWidgetContext Context");
        parameter!.PropertyType.Should().Be(typeof(HomeWidgetContext));
    }

    [Theory]
    [MemberData(nameof(WidgetIds))]
    public void Widget_DeclaresOnlyTheContextParameter(string widgetId)
    {
        // 宿主只传 Context，多声明的 [Parameter] 会因缺少参数在渲染时抛异常
        var widget = DiscoverWidgets().Single(w => w.Id == widgetId);

        var declared = widget.ComponentType
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.GetCustomAttribute<ParameterAttribute>() != null)
            .Select(p => p.Name)
            .ToList();

        declared.Should().Equal("Context");
    }

    [Theory]
    [InlineData("zh-CN")]
    [InlineData("en-US")]
    public void Widget_LocalizationKeysExist(string culture)
    {
        var entries = LoadBuiltinLocale(culture);

        foreach (var widget in DiscoverWidgets())
        {
            entries.Should().ContainKey(widget.NameKey, $"{widget.Id} 的名称 Key 必须在 {culture} 中存在");
            if (!string.IsNullOrEmpty(widget.DescriptionKey))
                entries.Should().ContainKey(widget.DescriptionKey, $"{widget.Id} 的描述 Key 必须在 {culture} 中存在");
        }
    }

    [Fact]
    public void Defaults_ContainTutorialAndReleaseCheckOnly()
    {
        var defaults = CreateRegistry().GetDefaults();

        defaults.Select(widget => widget.Id).Should().Equal("getting-started", "github-release");
        defaults.Should().OnlyContain(widget => widget.IsDefault);
    }

    [Fact]
    public void AgentShortcut_AllowsMultipleInstances()
    {
        var agentWidget = CreateRegistry().GetById("agent-quick");

        agentWidget.Should().NotBeNull();
        agentWidget!.AllowMultiple.Should().BeTrue();
    }

    [Fact]
    public void GetById_DoesNotSilentlyRepairUnknownOrEmptyId()
    {
        var registry = CreateRegistry();

        registry.GetById(null).Should().BeNull();
        registry.GetById("").Should().BeNull();
        registry.GetById("not-a-registered-widget").Should().BeNull();
        registry.GetById("agent-quick")!.Id.Should().Be("agent-quick");
    }

    [Fact]
    public void Widgets_AreOrderedAndUniquelyIdentified()
    {
        var widgets = DiscoverWidgets();

        widgets.Select(widget => widget.Id).Should().OnlyHaveUniqueItems();
        widgets.Should().BeInAscendingOrder(widget => widget.Order);
        widgets.Should().OnlyContain(widget => !string.IsNullOrWhiteSpace(widget.NameKey));
    }

    private static Dictionary<string, string> LoadBuiltinLocale(string culture)
        // 本地化已按领域拆分为多个 *.{culture}.json，聚合全部匹配资源。
        => LocaleTestHelpers.MergeEntries(LocaleTestHelpers.LoadAssemblyEntries(BuiltinAssembly, culture));

    /// <summary>仅提供类型扫描能力的插件加载器桩。</summary>
    private sealed class StubPluginLoader(params Assembly[] assemblies) : IPluginLoaderService
    {
        public IEnumerable<Assembly> GetAssemblies() => assemblies;

        public List<T> LoadImplementations<T>() => [];

        public List<Type> GetTypesWithAttribute<TAttribute>() where TAttribute : Attribute
            => assemblies.SelectMany(GetLoadableTypes)
                .Where(t => t.GetCustomAttributes(typeof(TAttribute), false).Any())
                .ToList();

        public List<Type> GetImplementingTypes(Type interfaceType)
            => assemblies.SelectMany(GetLoadableTypes)
                .Where(t => interfaceType.IsAssignableFrom(t) && !t.IsAbstract && !t.IsInterface)
                .ToList();

        public Task InitializePluginsAsync() => Task.CompletedTask;

        private static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
        {
            try
            {
                return assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                return ex.Types.Where(t => t != null).Cast<Type>();
            }
        }
    }
}
