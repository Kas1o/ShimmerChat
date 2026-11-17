using System.Text.Json;
using Newtonsoft.Json;
using ShimmerChatBuiltin.Mcp;
using ShimmerChatBuiltin.SubAgent;

namespace ShimmerChatBuiltin.Tests;

/// <summary>
/// 配置页统一外壳依赖的收藏字段必须向后兼容：旧数据缺失时为 false，
/// 克隆时保留收藏状态，序列化往返不丢失。
/// </summary>
public class ConfigPinnedTests
{
    [Fact]
    public void EmbeddingProfile_PinnedDefaultsFalseAndSurvivesRoundTrip()
    {
        var profile = new EmbeddingProfile
        {
            Id = "embed-1",
            Name = "Embedding",
            BaseUrl = "http://localhost:5001/v1",
            ModelName = "text-embedding-3-small"
        };

        profile.Pinned.Should().BeFalse();

        var json = System.Text.Json.JsonSerializer.Serialize(profile);
        System.Text.Json.JsonSerializer.Deserialize<EmbeddingProfile>(json)!.Pinned.Should().BeFalse();

        profile.Pinned = true;
        var pinnedJson = System.Text.Json.JsonSerializer.Serialize(profile);
        System.Text.Json.JsonSerializer.Deserialize<EmbeddingProfile>(pinnedJson)!.Pinned.Should().BeTrue();

        var clone = profile.Clone();
        clone.Pinned.Should().BeTrue();
        clone.Id.Should().BeEmpty("the caller assigns a fresh id when cloning");
        clone.ModelName.Should().Be(profile.ModelName);
    }

    [Fact]
    public void ToolPreset_PinnedSurvivesRoundTripAndClone()
    {
        var preset = new ToolPreset
        {
            Id = "tools-1",
            Name = "Tools",
            Pinned = true,
            EnabledToolTypeNames = ["FileSystemRead", "FileSystemWrite"]
        };

        var json = JsonConvert.SerializeObject(preset);
        var roundTripped = JsonConvert.DeserializeObject<ToolPreset>(json)!;

        roundTripped.Pinned.Should().BeTrue();
        roundTripped.EnabledToolTypeNames.Should().Equal("FileSystemRead", "FileSystemWrite");

        var clone = preset.Clone();
        clone.Pinned.Should().BeTrue();
        clone.Id.Should().BeEmpty();
        clone.EnabledToolTypeNames.Should().NotBeSameAs(preset.EnabledToolTypeNames);
    }

    [Fact]
    public void DynPromptSet_HasStableIdAndClonesTermsDeeply()
    {
        var set = new DynPromptSet
        {
            Id = "set-1",
            Name = "Set",
            Pinned = true,
            Terms =
            [
                new DynPromptTerm
                {
                    Name = "term",
                    Content = "content",
                    TriggerRule = "hello"
                }
            ]
        };

        var clone = set.Clone();

        clone.Id.Should().BeEmpty();
        clone.Pinned.Should().BeTrue();
        clone.Terms.Should().HaveCount(1);
        clone.Terms[0].Should().NotBeSameAs(set.Terms[0]);
        clone.Terms[0].Name.Should().Be("term");

        var json = JsonConvert.SerializeObject(set);
        JsonConvert.DeserializeObject<DynPromptSet>(json)!.Pinned.Should().BeTrue();
    }

    [Fact]
    public void SubAgentConfig_PinnedSurvivesRoundTripAndCloneGetsNewGuid()
    {
        var config = new SubAgentConfig
        {
            Name = "Sub",
            Pinned = true,
            OutputMode = "FullJson"
        };

        var json = JsonConvert.SerializeObject(config);
        JsonConvert.DeserializeObject<SubAgentConfig>(json)!.Pinned.Should().BeTrue();

        var clone = config.Clone();
        clone.Pinned.Should().BeTrue();
        clone.Guid.Should().Be(Guid.Empty);
        clone.OutputMode.Should().Be("FullJson");
    }

    [Fact]
    public void McpEndpointConfig_PinnedSurvivesCatalogRoundTrip()
    {
        var endpoint = new McpEndpointConfig
        {
            Id = "server",
            Name = "Server",
            Command = "npx",
            Pinned = true
        };

        var json = JsonConvert.SerializeObject(new List<McpEndpointConfig> { endpoint });
        var roundTripped = JsonConvert.DeserializeObject<List<McpEndpointConfig>>(json)!;

        roundTripped.Should().ContainSingle();
        roundTripped[0].Pinned.Should().BeTrue();

        var clone = McpEndpointCatalog.Clone(endpoint);
        clone.Pinned.Should().BeTrue();
        clone.Id.Should().Be("server");
    }
}
