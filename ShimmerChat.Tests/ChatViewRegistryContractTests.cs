using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging.Abstractions;
using ShimmerChatLib.ChatView;
using ShimmerChatLib.Interface;

namespace ShimmerChat.Tests;

/// <summary>
/// 对话界面注册表的契约守卫。
/// <para>
/// 宿主用 <c>DynamicComponent</c> 渲染界面并只传入 <c>Context</c> 一个参数，
/// 界面 Id 又被 <see cref="Agent.ChatViewId"/> 持久化引用，因此：
/// 每个界面都必须实现了 <see cref="IChatView"/>、是 Blazor 组件、声明了 <c>Context</c> 参数，
/// 且 Id 唯一、名称与描述的本地化 Key 真实存在。本测试把这些契约固化下来。
/// </para>
/// </summary>
public class ChatViewRegistryContractTests
{
    private static readonly Assembly HostAssembly = typeof(Program).Assembly;
    private static readonly Assembly LibAssembly = typeof(IChatView).Assembly;
    private static readonly Assembly BuiltinAssembly = typeof(ShimmerChatBuiltin.Target).Assembly;

    private static ChatViewRegistry CreateRegistry()
        => new(new StubPluginLoader(LibAssembly, BuiltinAssembly, HostAssembly), NullLogger<ChatViewRegistry>.Instance);

    private static IReadOnlyList<ChatViewInfo> DiscoverViews() => CreateRegistry().GetAll();

    public static TheoryData<string> ViewIds()
    {
        var data = new TheoryData<string>();
        foreach (var view in DiscoverViews())
            data.Add(view.Id);
        return data;
    }

    [Fact]
    public void Discovery_FindsBuiltinChatViews()
    {
        var ids = DiscoverViews().Select(v => v.Id).ToList();

        // 若新增界面而这里没跟上，说明发现逻辑退化了（而不是界面漏注册）。
        ids.Should().Contain("standard");
        ids.Should().Contain("aggregated");

        // 数量守卫：反射加载失败会静默丢类型，从而让契约测试失去意义。
        DiscoverViews().Should().HaveCount(2);
    }

    [Theory]
    [MemberData(nameof(ViewIds))]
    public void ChatView_DeclaresTheContextParameterProvidedByHost(string viewId)
    {
        var view = DiscoverViews().Single(v => v.Id == viewId);

        var parameter = view.ComponentType
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .SingleOrDefault(p => p.GetCustomAttribute<ParameterAttribute>() != null && p.Name == "Context");

        parameter.Should().NotBeNull($"{view.ComponentType.FullName} 必须声明 [Parameter] ChatViewContext Context");
        parameter!.PropertyType.Should().Be(typeof(ChatViewContext));
        typeof(ComponentBase).IsAssignableFrom(view.ComponentType).Should().BeTrue();
    }

    [Theory]
    [MemberData(nameof(ViewIds))]
    public void ChatView_DeclaresOnlyTheContextParameter(string viewId)
    {
        // 宿主只传 Context，多声明的 [Parameter] 会因缺少参数在渲染时抛异常
        var view = DiscoverViews().Single(v => v.Id == viewId);

        var declared = view.ComponentType
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.GetCustomAttribute<ParameterAttribute>() != null)
            .Select(p => p.Name)
            .ToList();

        declared.Should().Equal("Context");
    }

    [Theory]
    [InlineData("zh-CN")]
    [InlineData("en-US")]
    public void ChatView_LocalizationKeysExist(string culture)
    {
        var entries = LoadBuiltinLocale(culture);

        foreach (var view in DiscoverViews())
        {
            entries.Should().ContainKey(view.NameKey, $"{view.Id} 的名称 Key 必须在 {culture} 中存在");
            if (!string.IsNullOrEmpty(view.DescriptionKey))
                entries.Should().ContainKey(view.DescriptionKey, $"{view.Id} 的描述 Key 必须在 {culture} 中存在");
        }
    }

    [Fact]
    public void GetDefault_ReturnsTheViewMarkedAsDefault()
    {
        var registry = CreateRegistry();
        var defaultView = registry.GetDefault();

        defaultView.Should().NotBeNull();
        defaultView!.IsDefault.Should().BeTrue();
        defaultView.Id.Should().Be("standard");
    }

    [Fact]
    public void GetById_DoesNotSilentlyRepairUnknownOrEmptyId()
    {
        var registry = CreateRegistry();

        registry.GetById(null).Should().BeNull();
        registry.GetById("").Should().BeNull();
        registry.GetById("not-a-registered-view").Should().BeNull();
        registry.GetById("aggregated")!.Id.Should().Be("aggregated");
    }

    [Fact]
    public void Views_AreOrderedAndUniquelyIdentified()
    {
        var views = DiscoverViews();

        views.Select(v => v.Id).Should().OnlyHaveUniqueItems();
        views.Should().BeInAscendingOrder(v => v.Order);
        views.Should().OnlyContain(v => !string.IsNullOrWhiteSpace(v.NameKey));
    }

    private static Dictionary<string, string> LoadBuiltinLocale(string culture)
    {
        var resourceName = BuiltinAssembly.GetManifestResourceNames()
            .Single(n => n.EndsWith($"Locales.{culture}.json", StringComparison.OrdinalIgnoreCase));

        using var stream = BuiltinAssembly.GetManifestResourceStream(resourceName)!;
        using var reader = new StreamReader(stream);
        return JsonSerializer.Deserialize<Dictionary<string, string>>(reader.ReadToEnd())!;
    }

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
