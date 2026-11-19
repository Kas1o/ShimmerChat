using System.Reflection;
using Microsoft.Extensions.Logging;
using ShimmerChatLib.Interface;
using ShimmerChat.Singletons;
using static ShimmerChat.Tests.LocaleTestHelpers;

namespace ShimmerChat.Tests;

/// <summary>
/// LocService 多文件加载行为：
/// 嵌入资源按 ".{culture}.json" 后缀匹配、确定顺序合并、en-US 基线回退、
/// 坏文件显式降级（记错误日志）、覆盖目录与切换语言协同工作。
/// 测试数据来自本测试程序集嵌入的 Locales/*.zh-CN.json 系列资源。
/// </summary>
public class LocServiceTests
{
    private sealed class StubKVData : IKVDataService
    {
        public Dictionary<string, string> Store { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<(string Space, string Key, string Value)> Writes { get; } = [];

        public string? Read(string spaceId, string key)
            => Store.TryGetValue(spaceId + '/' + key, out var value) ? value : null;

        public void Write(string spaceId, string key, string value)
        {
            Store[spaceId + '/' + key] = value;
            Writes.Add((spaceId, key, value));
        }
    }

    /// <summary>仅提供程序集枚举能力的插件加载器桩。</summary>
    private sealed class StubPluginLoader(params Assembly[] assemblies) : IPluginLoaderService
    {
        public IEnumerable<Assembly> GetAssemblies() => assemblies;
        public List<T> LoadImplementations<T>() => [];
        public List<Type> GetTypesWithAttribute<TAttribute>() where TAttribute : Attribute => [];
        public List<Type> GetImplementingTypes(Type interfaceType) => [];
        public Task InitializePluginsAsync() => Task.CompletedTask;
    }

    private sealed class SpyLogger : ILogger<LocService>
    {
        public List<(LogLevel Level, string Message)> Records { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
            => Records.Add((logLevel, formatter(state, exception)));
    }

    private static LocService CreateForTestAssembly(StubKVData kv, SpyLogger? logger = null, string? overrides = null)
        => new(kv, new StubPluginLoader(TestAssembly), overrides, logger);

    [Fact]
    public void MultiFileLocale_MergesAllSuffixMatchedFiles()
    {
        var loc = CreateForTestAssembly(new StubKVData());

        // KV 无记录 → 默认 zh-CN；拆分文件（*.{culture}.json 后缀）全部被发现。
        loc.CurrentCulture.Should().Be("zh-CN");
        loc["loctest.zh_only"].Should().Be("zh 专属值");

        // 同键出现在两份拆分文件时，按资源名排序后者覆盖前者，结果确定可预期。
        loc["loctest.merge_priority"].Should().Be("Z 覆盖值");

        // 目标 culture 覆盖 en-US 基线；缺失键回退基线；完全缺失回退 Key 本身。
        loc["loctest.shared"].Should().Be("zh 共享值");
        loc["loctest.en_only"].Should().Be("en-only value");
        loc["loctest.missing"].Should().Be("loctest.missing");
    }

    [Fact]
    public void BrokenLocaleFile_DegradesExplicitlyWithoutKillingTheCulture()
    {
        var logger = new SpyLogger();
        var loc = CreateForTestAssembly(new StubKVData(), logger);

        // 坏文件被跳过并留下错误日志，同 culture 的其它文件照常生效。
        logger.Records.Should().Contain(r =>
            r.Level == LogLevel.Error && r.Message.Contains("broken.zh-CN.json"));
        loc["loctest.zh_only"].Should().Be("zh 专属值");
        loc["loctest.broken"].Should().Be("loctest.broken");
    }

    [Fact]
    public void SetCulture_SwitchesEntriesImmediately()
    {
        var kv = new StubKVData();
        var loc = CreateForTestAssembly(kv);

        loc.SetCulture("en-US");

        loc.CurrentCulture.Should().Be("en-US");
        loc["loctest.en_only"].Should().Be("en-only value");
        loc["loctest.shared"].Should().Be("en shared");
        // zh 专属键随语言一起切换，不残留旧条目。
        loc["loctest.zh_only"].Should().Be("loctest.zh_only");
        // 切换被持久化。
        kv.Writes.Should().Contain(w => w.Space == "_locale" && w.Key == "culture" && w.Value == "en-US");
    }

    [Fact]
    public void SetCulture_RejectsUnsupportedCultureVisibly()
    {
        var kv = new StubKVData();
        var logger = new SpyLogger();
        var loc = CreateForTestAssembly(kv, logger);

        loc.SetCulture("fr-FR");

        // 非法语言被显式拒绝：不切换、不持久化，并留下可追踪日志。
        loc.CurrentCulture.Should().Be("zh-CN");
        logger.Records.Should().Contain(r => r.Level == LogLevel.Warning && r.Message.Contains("fr-FR"));
        kv.Writes.Should().NotContain(w => w.Value == "fr-FR");
    }

    [Fact]
    public void OverrideDirectory_UsesSuffixRuleAndSurvivesCultureSwitch()
    {
        var dir = Path.Combine(Path.GetTempPath(), "loctest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "zh-CN.json"),
                """{ "loctest.shared": "目录覆盖值" }""");
            File.WriteAllText(Path.Combine(dir, "extra.en-US.json"),
                """{ "loctest.en_only": "覆盖的 en 值" }""");
            File.WriteAllText(Path.Combine(dir, "notalocale.json"),
                """{ "loctest.ignored": "不应生效" }""");

            var loc = CreateForTestAssembly(new StubKVData(), overrides: dir);

            // 覆盖目录与嵌入资源同一后缀规则：en 基线覆盖 + zh 覆盖；命名不符的文件被忽略。
            loc["loctest.shared"].Should().Be("目录覆盖值");
            loc["loctest.en_only"].Should().Be("覆盖的 en 值");
            loc["loctest.ignored"].Should().Be("loctest.ignored");

            // 切走再切回，覆盖仍然生效（回归守卫：覆盖不能只在首次启动时应用一次）。
            loc.SetCulture("en-US");
            loc["loctest.shared"].Should().Be("en shared");
            loc.SetCulture("zh-CN");
            loc["loctest.shared"].Should().Be("目录覆盖值");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void ProductAssemblies_EveryHostKeyResolvesWithHostValue()
    {
        // 装配顺序 Lib → Builtin → Host，宿主键不被其它程序集覆盖：
        // 全量断言拆分后的每一份宿主 zh 文件都参与合并且值保真。
        var loc = new LocService(new StubKVData(),
            new StubPluginLoader(LibAssembly, BuiltinAssembly, HostAssembly));

        var hostZh = MergeEntries(LoadAssemblyEntries(HostAssembly, "zh-CN"));
        hostZh.Should().NotBeEmpty();
        foreach (var (key, value) in hostZh)
            loc[key].Should().Be(value, $"宿主键 {key} 应来自宿主拆分文件");
    }

    [Fact]
    public void ProductAssemblies_BuiltinAndLibKeysRemainReadable()
    {
        var loc = new LocService(new StubKVData(),
            new StubPluginLoader(LibAssembly, BuiltinAssembly, HostAssembly));

        // 新增的规范键（替代原先与 "Name:" 大小写冲突的 "NAME:"）。
        loc["api_settings.template_name"].Should().Be("名称：");
        // 未拆分的 Lib 主文件仍被加载。
        loc["toolui.awaiting"].Should().Be("等待你的回答");
    }
}
