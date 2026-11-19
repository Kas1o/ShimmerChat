using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using ShimmerChat.Singletons;

namespace ShimmerChat.Tests;

/// <summary>
/// 本地化契约测试共用的工具：按 <see cref="LocService.GetLocaleResourceNames"/>
/// 的发现规则聚合程序集内某个 culture 的全部键值，避免测试与宿主各自维护一份匹配规则。
/// </summary>
internal static class LocaleTestHelpers
{
    public static readonly Assembly LibAssembly = typeof(ShimmerChatLib.Interface.ILocService).Assembly;
    public static readonly Assembly BuiltinAssembly = typeof(ShimmerChatBuiltin.Target).Assembly;
    public static readonly Assembly HostAssembly = typeof(Program).Assembly;
    public static readonly Assembly TestAssembly = typeof(LocaleTestHelpers).Assembly;

    /// <summary>列出某程序集某 culture 被后缀匹配发现的所有条目（保持文件内出现顺序）。</summary>
    public static List<KeyValuePair<string, string>> LoadAssemblyEntries(Assembly assembly, string culture)
    {
        var entries = new List<KeyValuePair<string, string>>();
        foreach (var name in LocService.GetLocaleResourceNames(assembly, culture))
        {
            using var stream = assembly.GetManifestResourceStream(name);
            stream.Should().NotBeNull($"本地化资源 {name} 应可打开");

            using var reader = new StreamReader(stream!);
            var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(reader.ReadToEnd());
            dict.Should().NotBeNull($"本地化资源 {name} 应是字符串键值对象");
            entries.AddRange(dict!);
        }

        return entries;
    }

    /// <summary>按运行时同样的合并语义（忽略大小写、后写覆盖、跳过空白值）聚成字典。</summary>
    public static Dictionary<string, string> MergeEntries(IEnumerable<KeyValuePair<string, string>> entries)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in entries)
        {
            if (!string.IsNullOrWhiteSpace(value))
                dict[key] = value;
        }

        return dict;
    }
}
