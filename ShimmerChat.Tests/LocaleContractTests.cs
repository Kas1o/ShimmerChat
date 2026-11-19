using System.Reflection;
using ShimmerChat.Singletons;
using static ShimmerChat.Tests.LocaleTestHelpers;

namespace ShimmerChat.Tests;

/// <summary>
/// 本地化文件拆分后的数据契约守卫：
/// <para>
/// 同一程序集、同一 culture 的多份文件不允许键重复（含忽略大小写，否则合并胜者不可预期）；
/// zh-CN 与 en-US 的键集合必须一致；后缀匹配必须能同时发现拆分文件与未拆分的主文件。
/// </para>
/// </summary>
public class LocaleContractTests
{
    private static readonly string[] Cultures = ["zh-CN", "en-US"];

    private static IEnumerable<Assembly> ProductAssemblies()
    {
        yield return LibAssembly;
        yield return BuiltinAssembly;
        yield return HostAssembly;
    }

    [Fact]
    public void LocaleFiles_HaveNoDuplicateKeysWithinAssembly()
    {
        foreach (var asm in ProductAssemblies())
        foreach (var culture in Cultures)
        {
            var folded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var duplicates = LoadAssemblyEntries(asm, culture)
                .Where(e => !folded.Add(e.Key))
                .Select(e => e.Key)
                .ToList();

            duplicates.Should().BeEmpty(
                $"{asm.GetName().Name} 的 {culture} 本地化不允许重复键：运行时按键合并，重复会让显示内容依赖文件加载顺序");
        }
    }

    [Fact]
    public void LocaleFiles_KeepCulturesAlignedWithinAssembly()
    {
        foreach (var asm in ProductAssemblies())
        {
            var zhKeys = MergeEntries(LoadAssemblyEntries(asm, "zh-CN")).Keys.ToHashSet();
            var enKeys = MergeEntries(LoadAssemblyEntries(asm, "en-US")).Keys.ToHashSet();

            zhKeys.Should().BeEquivalentTo(enKeys,
                $"{asm.GetName().Name} 每个键必须同时提供 zh-CN 与 en-US");
        }
    }

    [Fact]
    public void LocaleFiles_AreDiscoveredBySuffixAcrossSplitAndBaseFiles()
    {
        // 拆分守卫：宿主与内置插件的语言资源现在按领域分成多个 *.{culture}.json 文件。
        LocService.GetLocaleResourceNames(BuiltinAssembly, "zh-CN").Count().Should().BeGreaterThan(1);
        LocService.GetLocaleResourceNames(HostAssembly, "en-US").Count().Should().BeGreaterThan(1);

        // 兼容守卫：未拆分的主文件（Lib 的 Locales/zh-CN.json）依旧是受支持的形态。
        LocService.GetLocaleResourceNames(LibAssembly, "zh-CN")
            .Should().Contain(n => n.EndsWith(".Locales.zh-CN.json", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void LocaleResourceMatcher_IgnoresFilesOutsideTheLocalesFolder()
    {
        // TestLocales/trap.zh-CN.json 以 .zh-CN.json 结尾但不在 Locales/ 目录，必须被拒收。
        LocService.GetLocaleResourceNames(TestAssembly, "zh-CN")
            .Should().NotContain(n => n.Contains("trap", StringComparison.OrdinalIgnoreCase));
    }
}
