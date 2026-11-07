using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using ShimmerChatBuiltin.API;
using ShimmerChatBuiltin.Memory;
using ShimmerChatLib.Interface;

namespace ShimmerChat.Tests.Memory;

public sealed class EmbeddingProfileTests
{
    private sealed class MemoryKV : IKVDataService
    {
        private readonly Dictionary<(string Space, string Key), string> _values = new();

        public string? Read(string spaceId, string key)
            => _values.TryGetValue((spaceId, key), out var value) ? value : null;

        public void Write(string spaceId, string key, string value)
            => _values[(spaceId, key)] = value;
    }

    [Fact]
    public void EnsureMigrated_FromLegacyKeys_CreatesDefaultProfile()
    {
        var kv = new MemoryKV();
        kv.Write("EmbeddingAPI", "uri", "https://api.example.com/v1");
        kv.Write("EmbeddingAPI", "apikey", "secret");
        kv.Write("EmbeddingAPI", "modelname", "text-embedding-3-small");

        var profiles = EmbeddingProfileStore.EnsureMigrated(kv);

        profiles.Should().ContainSingle();
        profiles[0].Id.Should().Be("default");
        profiles[0].BaseUrl.Should().Be("https://api.example.com/v1");
        profiles[0].ModelName.Should().Be("text-embedding-3-small");
        EmbeddingProfileStore.GetActiveId(kv).Should().Be("default");
    }

    [Fact]
    public void Load_WithExplicitProfileId_UsesThatProfile()
    {
        var kv = new MemoryKV();
        EmbeddingProfileStore.Save(kv,
        [
            new EmbeddingProfile { Id = "a", Name = "A", BaseUrl = "https://a/v1", ModelName = "model-a" },
            new EmbeddingProfile { Id = "b", Name = "B", BaseUrl = "https://b/v1", ModelName = "model-b" }
        ]);
        EmbeddingProfileStore.SetActive(kv, "a");

        var (active, activeError) = MemoryModelConfig.Load(kv);
        var (pinned, pinnedError) = MemoryModelConfig.Load(kv, "b");

        activeError.Should().BeNull();
        active!.ModelName.Should().Be("model-a");
        active.ProfileName.Should().Be("A");

        pinnedError.Should().BeNull();
        pinned!.ModelName.Should().Be("model-b");
        pinned.ProfileId.Should().Be("b");
        pinned.ModelKey.Should().NotBe(active.ModelKey);
    }

    [Fact]
    public void Load_WithUnknownProfileId_ReturnsError()
    {
        var kv = new MemoryKV();
        EmbeddingProfileStore.Save(kv,
        [
            new EmbeddingProfile { Id = "a", Name = "A", BaseUrl = "https://a/v1", ModelName = "model-a" }
        ]);

        var (config, error) = MemoryModelConfig.Load(kv, "missing");

        config.Should().BeNull();
        error.Should().Contain("missing");
    }

    [Fact]
    public void Load_WithCorruptJson_ReturnsExplicitError()
    {
        var kv = new MemoryKV();
        kv.Write("EmbeddingAPI", "profiles", "{not-json");

        var profiles = EmbeddingProfileStore.Load(kv, out var error);
        var (config, configError) = MemoryModelConfig.Load(kv);

        profiles.Should().BeEmpty();
        error.Should().Contain("损坏");
        config.Should().BeNull();
        configError.Should().Be(error);
    }

    [Fact]
    public void Load_WithMissingActiveProfile_ReturnsExplicitError()
    {
        var kv = new MemoryKV();
        EmbeddingProfileStore.Save(kv,
        [
            new EmbeddingProfile { Id = "a", Name = "A", BaseUrl = "https://a/v1", ModelName = "model-a" }
        ]);
        EmbeddingProfileStore.SetActive(kv, "gone");

        var (config, error) = MemoryModelConfig.Load(kv);

        config.Should().BeNull();
        error.Should().Contain("gone");
    }

    [Fact]
    public void OptionsProvider_MarksActiveProfile()
    {
        var kv = new MemoryKV();
        EmbeddingProfileStore.Save(kv,
        [
            new EmbeddingProfile { Id = "a", Name = "A", BaseUrl = "https://a/v1", ModelName = "model-a" },
            new EmbeddingProfile { Id = "b", Name = "B", BaseUrl = "https://b/v1", ModelName = "model-b" }
        ]);
        EmbeddingProfileStore.SetActive(kv, "b");

        using var services = new ServiceCollection()
            .AddSingleton<IKVDataService>(kv)
            .BuildServiceProvider();

        var options = new EmbeddingProfileOptionsProvider().GetOptions(services);

        options.Should().HaveCount(2);
        options.Single(x => x.Value == "b").Label.Should().Contain("★");
        options.Single(x => x.Value == "a").Label.Should().NotContain("★");
    }
}
