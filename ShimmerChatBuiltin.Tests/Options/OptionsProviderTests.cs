using Meziantou.Framework.SyntaxHighlighting;
using Newtonsoft.Json;
using ShimmerChatBuiltin.DynPrompt.Options;
using ShimmerChatBuiltin.Misc.Node.Render.Options;
using ShimmerChatBuiltin.Options;
using ShimmerChatBuiltin.SubAgent;
using ShimmerChatBuiltin.SubAgent.Options;
using ShimmerChatBuiltin.Tests.SubAgent;
using ShimmerChatLib.Interface;

namespace ShimmerChatBuiltin.Tests.Options;

/// <summary>
/// 节点属性选项提供者：正常取值、空数据、损坏数据三条路径。
/// 这些类由插件加载器自动发现，失败时只能「返回空集合 + 日志」表现，
/// UI 侧会把空集合显示成「暂无可选项」，不允许静默当成功。
/// </summary>
public class OptionsProviderTests
{
    private static IServiceProvider Services(
        IKVDataService? kv = null,
        IToolRegistry? registry = null)
    {
        var services = new TestServices();
        services.Add(typeof(ILocService), new KeyLoc());
        if (kv is not null) services.Add(typeof(IKVDataService), kv);
        if (registry is not null) services.Add(typeof(IToolRegistry), registry);
        return services;
    }

    [Fact]
    public void PreGenerationPresets_MapIdAndName()
    {
        var kv = new FakeKVDataService();
        kv.Write("GenerationManager", "generation_presets", JsonConvert.SerializeObject(new[]
        {
            new PreGenerationPreset { Id = "p1", Name = "Alpha" },
            new PreGenerationPreset { Id = "p2", Name = "Beta" }
        }));

        var provider = new PreGenerationPresetOptionsProvider();
        var options = provider.GetOptions(Services(kv: kv));

        provider.Key.Should().Be("pre_generation_presets");
        options.Should().HaveCount(2);
        options[0].Value.Should().Be("p1");
        options[0].Label.Should().Be("Alpha");
        options[1].Value.Should().Be("p2");
    }

    [Fact]
    public void PostAndRenderPresets_ReadTheirOwnStore()
    {
        var kv = new FakeKVDataService();
        kv.Write("PostGenerationManager", "post_generation_presets",
            JsonConvert.SerializeObject(new[] { new PostGenerationPreset { Id = "post1", Name = "Post" } }));
        kv.Write("RenderModifierManager", "render_modifier_presets",
            JsonConvert.SerializeObject(new[] { new RenderModifierPreset { Id = "r1", Name = "Render" } }));

        var services = Services(kv: kv);

        new PostGenerationPresetOptionsProvider().GetOptions(services)
            .Should().ContainSingle().Which.Value.Should().Be("post1");
        new RenderModifierPresetOptionsProvider().GetOptions(services)
            .Should().ContainSingle().Which.Value.Should().Be("r1");
    }

    [Fact]
    public void MissingStore_ReturnsEmptyWithoutThrowing()
    {
        var kv = new FakeKVDataService();

        new PreGenerationPresetOptionsProvider().GetOptions(Services(kv: kv)).Should().BeEmpty();
        new ToolPresetOptionsProvider().GetOptions(Services(kv: kv)).Should().BeEmpty();
    }

    [Fact]
    public void MissingKVDataService_ReturnsEmpty()
    {
        new PreGenerationPresetOptionsProvider().GetOptions(Services()).Should().BeEmpty();
    }

    [Fact]
    public void CorruptJson_ReturnsEmpty()
    {
        var kv = new FakeKVDataService();
        kv.Write("DynPrompt", "DynPromptSets", "{ not a json array");

        new DynPromptSetOptionsProvider().GetOptions(Services(kv: kv)).Should().BeEmpty();
    }

    [Fact]
    public void ToolPreset_MarksDefaultAndExposesToolNamesAsSearchTerms()
    {
        var kv = new FakeKVDataService();
        kv.Write("ToolPresets", "__presets__", JsonConvert.SerializeObject(new[]
        {
            new ToolPreset
            {
                Id = "t1", Name = "Default", IsDefault = true,
                EnabledToolTypeNames = ["FileSystemReadToolV2", "FileSystemWriteToolV2"]
            }
        }));

        var option = new ToolPresetOptionsProvider().GetOptions(Services(kv: kv)).Single();

        option.Value.Should().Be("t1");
        option.Label.Should().Contain("Default").And.Contain("(default)");
        option.Description.Should().Be("2 tools");
        option.SearchTerms.Should().Contain("FileSystemReadToolV2");
    }

    [Fact]
    public void ApiConfigs_GroupByTypeAndMarkGloballySelected()
    {
        var openAi = new ApiConfig
        {
            Id = Guid.NewGuid(), Name = "OpenAI One", Type = ApiConfigType.OpenAI, OpenAIModel = "gpt-4o"
        };
        var deepSeek = new ApiConfig
        {
            Id = Guid.NewGuid(), Name = "DeepSeek One", Type = ApiConfigType.DeepSeek,
            DeepSeekModel = "deepseek-v4-flash"
        };

        var kv = new FakeKVDataService();
        kv.Write("ApiSettings", "apiSetting", JsonConvert.SerializeObject(new[] { openAi, deepSeek }));
        kv.Write("ApiSettings", "selectedAPIGuid", deepSeek.Id.ToString());

        var options = new ApiConfigOptionsProvider().GetOptions(Services(kv: kv));

        options.Should().HaveCount(2);
        options.Single(o => o.Value == openAi.Id.ToString()).Group.Should().Be("OpenAI");
        options.Single(o => o.Value == openAi.Id.ToString()).Label.Should().NotContain("★");

        var selected = options.Single(o => o.Value == deepSeek.Id.ToString());
        selected.Group.Should().Be("DeepSeek");
        selected.Label.Should().Contain("★");
        selected.Description.Should().Contain("deepseek-v4-flash");
    }

    [Fact]
    public void ToolTypes_ComeFromRegistryWithLocalizedGroupAndDescription()
    {
        var registry = new Generation.Nodes.StubToolRegistry()
            .Register("tool.read", typeof(string), "category.file_system", "category.read_write")
            .Register("tool.write", typeof(int), "category.file_system", "category.read_write");

        var options = new ToolTypeOptionsProvider().GetOptions(Services(registry: registry));

        options.Should().HaveCount(2);
        options.Should().OnlyContain(o => o.Group == "category.file_system / category.read_write");
        // 无本地化时标签回退到类型名，描述回退为空（不显示副标题）
        options.Should().OnlyContain(o => o.Label == o.Value);
        options.Should().OnlyContain(o => o.Description == string.Empty);
        options.Select(o => o.Value).Should().Contain(typeof(string).FullName);
    }

    [Fact]
    public void ToolTypes_WithoutRegistry_ReturnsEmpty()
    {
        new ToolTypeOptionsProvider().GetOptions(Services()).Should().BeEmpty();
    }

    [Fact]
    public void SubAgentConfigs_ExposeOutputModeSummary()
    {
        var kv = new FakeKVDataService();
        kv.Write("SubAgent", "configs", JsonConvert.SerializeObject(new[]
        {
            new SubAgentConfig
            {
                Name = "Researcher", Guid = Guid.NewGuid(), UseSharedPreset = true,
                ModifierPresetId = "preset-1", OutputMode = "LastMessage"
            }
        }));

        var option = new SubAgentConfigOptionsProvider().GetOptions(Services(kv: kv)).Single();

        option.Label.Should().Be("Researcher");
        option.Description.Should().Contain("LastMessage").And.Contain("preset-1");
        option.SearchTerms.Should().Contain("preset-1");
    }

    [Fact]
    public void SubAgentOutputModes_AreTheThreeFormatterValues()
    {
        var options = new SubAgentOutputModeOptionsProvider().GetOptions(Services());

        options.Select(o => o.Value).Should().Equal("LastMessage", "FullJson", "None");
        options.Should().OnlyContain(o => !string.IsNullOrWhiteSpace(o.Description));
    }

    [Fact]
    public void DynPromptSets_UseNameAsValueAndRuleNamesAsSearchTerms()
    {
        var kv = new FakeKVDataService();
        kv.Write("DynPrompt", "DynPromptSets", JsonConvert.SerializeObject(new[]
        {
            new DynPromptSet
            {
                Name = "Style",
                Terms = [new DynPromptTerm { Name = "concise", Content = "be brief" }]
            }
        }));

        var option = new DynPromptSetOptionsProvider().GetOptions(Services(kv: kv)).Single();

        option.Value.Should().Be("Style");
        option.SearchTerms.Should().Contain("concise");
        option.Description.Should().Be("1 rules");
    }

    [Fact]
    public void HighlightLanguages_ComeFromTheLibraryAndAreAllSupported()
    {
        var options = new HighlightLanguageOptionsProvider().GetOptions(Services());

        options.Should().NotBeEmpty();
        options.Should().OnlyContain(o => SyntaxHighlighter.IsSupported(o.Value));
        options.Select(o => o.Value).Should().BeInAscendingOrder(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>只做类型 → 实例映射的服务容器桩，避免测试工程额外引入 DI 包。</summary>
    private sealed class TestServices : IServiceProvider
    {
        private readonly Dictionary<Type, object> _map = new();

        public void Add(Type type, object instance) => _map[type] = instance;

        public object? GetService(Type serviceType) =>
            _map.TryGetValue(serviceType, out var value) ? value : null;
    }

    /// <summary>返回 Key 本身的本地化桩；带参数的 Key 回显参数，便于断言。</summary>
    private sealed class KeyLoc : ILocService
    {
        public string CurrentCulture => "zh-CN";

        public IReadOnlyList<string> SupportedCultures => ["zh-CN", "en-US"];

        public string this[string key] => key;

        public string Format(string key, params object[] args) =>
            $"{key}|{string.Join(",", args)}";

        public string Format(string key, params (string name, object value)[] args) => key;

        public void SetCulture(string culture) { }
    }
}
