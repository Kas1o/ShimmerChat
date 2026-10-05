using System.Reflection;
using Microsoft.AspNetCore.Components;
using ShimmerChatLib.Panel;

namespace ShimmerChat.Tests;

/// <summary>
/// 插件面板的参数契约守卫。
/// <para>
/// 宿主用 <c>DynamicComponent</c> 渲染面板并按 <see cref="PanelDisplayPlace"/> 传入一组固定参数。
/// <c>DynamicComponent</c> 在「面板声明了某个 [Parameter] 但参数字典里没有」时会直接抛异常，
/// 因此每个面板都必须声明其位置对应的全部参数。本测试把这条契约固化下来，
/// 避免新增面板或新增注入参数时漏适配。
/// </para>
/// </summary>
public class PluginPanelParameterContractTests
{
    /// <summary>每个显示位置必须被所有该位置面板声明的参数名。</summary>
    private static readonly Dictionary<PanelDisplayPlace, string[]> RequiredParameters = new()
    {
        [PanelDisplayPlace.Settings] = [],
        [PanelDisplayPlace.Agent] = ["AgentGuid", "EventHandlerReg", "PanelContext"],
        [PanelDisplayPlace.Chat] = ["ChatGuid", "AgentGuid", "EventHandlerReg", "PanelContext"]
    };

    /// <summary>每个位置下 PanelContext 参数应有的静态类型。</summary>
    private static readonly Dictionary<PanelDisplayPlace, Type> ContextTypes = new()
    {
        [PanelDisplayPlace.Agent] = typeof(AgentPanelContext),
        [PanelDisplayPlace.Chat] = typeof(ChatPanelContext)
    };

    private static List<Type> DiscoverPanelTypes()
    {
        var assemblies = new[]
        {
            typeof(Program).Assembly,                       // 宿主
            typeof(ShimmerChatLib.Panel.PluginPanelAttribute).Assembly,  // 共享库
            typeof(ShimmerChatBuiltin.Target).Assembly      // 内置插件
        };

        return assemblies
            .SelectMany(a =>
            {
                try
                {
                    return a.GetTypes();
                }
                catch (ReflectionTypeLoadException ex)
                {
                    return ex.Types.Where(t => t != null).Cast<Type>();
                }
            })
            .Where(t => t is { IsClass: true, IsAbstract: false })
            .Where(t => t.GetCustomAttribute<PluginPanelAttribute>(inherit: false) != null)
            .Distinct()
            .ToList();
    }

    public static TheoryData<Type> AllPanelTypes()
    {
        var data = new TheoryData<Type>();
        foreach (var type in DiscoverPanelTypes())
            data.Add(type);
        return data;
    }

    [Fact]
    public void Discovery_FindsEveryBuiltinPanel()
    {
        var discovered = DiscoverPanelTypes();
        var types = discovered.Select(t => t.FullName).ToList();

        // 若新增面板而这里没跟上，说明发现逻辑退化了（而不是面板漏适配）。
        Assert.Contains("ShimmerChatBuiltin.Variable.VariablePanel", types);
        Assert.Contains("ShimmerChatBuiltin.Variable.AgentVariablePanel", types);
        Assert.Contains("ShimmerChatBuiltin.Memory.MemoryManagementAgentPluginPanel", types);
        Assert.Contains("ShimmerChatBuiltin.SubAgent.SubAgentConfigurationPanel", types);
        Assert.Contains("ShimmerChatBuiltin.FileSystem.FileSystemPanel", types);
        Assert.Contains("ShimmerChatBuiltin.API.ApiSettingsPage", types);
        Assert.Contains("ShimmerChatBuiltin.Misc.ToolManager", types);
        Assert.Contains("ShimmerChatBuiltin.Example.ExamplePanel", types);

        // 数量守卫：反射加载失败会静默丢类型，从而让契约测试失去意义。
        Assert.Equal(12, discovered.Count);
    }

    [Theory]
    [MemberData(nameof(AllPanelTypes))]
    public void Panel_DeclaresEveryParameterProvidedByItsHost(Type panelType)
    {
        var attribute = panelType.GetCustomAttribute<PluginPanelAttribute>()!;
        var required = RequiredParameters[attribute.PanelDisplayPlace];

        var declared = panelType
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.GetCustomAttribute<ParameterAttribute>() != null)
            .Select(p => p.Name)
            .ToHashSet(StringComparer.Ordinal);

        var missing = required.Where(name => !declared.Contains(name)).ToList();

        Assert.True(missing.Count == 0,
            $"{panelType.FullName} ({attribute.PanelDisplayPlace}) 缺少宿主会传入的参数: {string.Join(", ", missing)}。" +
            "DynamicComponent 会因缺少参数而抛异常。");
    }

    [Theory]
    [MemberData(nameof(AllPanelTypes))]
    public void Panel_ContextParameterHasExpectedType(Type panelType)
    {
        var attribute = panelType.GetCustomAttribute<PluginPanelAttribute>()!;
        if (!ContextTypes.TryGetValue(attribute.PanelDisplayPlace, out var expected)) return;

        var property = panelType.GetProperty("PanelContext", BindingFlags.Public | BindingFlags.Instance);

        Assert.NotNull(property);
        Assert.True(
            property!.PropertyType == expected || property.PropertyType == expected.MakeByRefType(),
            $"{panelType.FullName} 的 PanelContext 类型为 {property.PropertyType.Name}，" +
            $"但 {attribute.PanelDisplayPlace} 位置应使用 {expected.Name}。");
    }

    [Fact]
    public void ContentPanels_DoNotDeclareChatGuidOrAgentGuid()
    {
        // Settings 位置不注入任何参数：面板不应声明 ChatGuid/AgentGuid，否则会因缺少参数报错。
        foreach (var panelType in DiscoverPanelTypes())
        {
            var attribute = panelType.GetCustomAttribute<PluginPanelAttribute>()!;
            if (attribute.PanelDisplayPlace != PanelDisplayPlace.Settings) continue;

            var declared = panelType
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.GetCustomAttribute<ParameterAttribute>() != null)
                .Select(p => p.Name)
                .ToList();

            Assert.DoesNotContain("ChatGuid", declared);
            Assert.DoesNotContain("AgentGuid", declared);
            Assert.DoesNotContain("PanelContext", declared);
        }
    }

    [Fact]
    public void AgentPanelContext_HierarchyExpressesHostDifferenceByType()
    {
        // 基类不应暴露 Agent 实体：只有真正持有 Agent 的宿主才提供 LiveAgentPanelContext。
        Assert.Null(typeof(AgentPanelContext).GetProperty("Agent"));
        Assert.NotNull(typeof(LiveAgentPanelContext).GetProperty("Agent"));
        Assert.True(typeof(LiveAgentPanelContext).IsSubclassOf(typeof(AgentPanelContext)));

        // Agent 作用域契约不应再暴露 Chat（对话数据只属于 ChatPanelContext）。
        Assert.Null(typeof(AgentPanelContext).GetProperty("Chat"));
        Assert.Null(typeof(ChatPanelContext).GetProperty("Config"));
    }
}
