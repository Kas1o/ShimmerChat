using System.Text.Json;
using FluentAssertions;
using ShimmerChatBuiltin.Memory;
using ShimmerChatLib.Models;
using ShimmerChatLib.Storage;
using Xunit;

namespace ShimmerChat.Tests.Memory;

/// <summary>
/// 后端配置的只读呈现逻辑：配置文件定位、JSON 片段、数据位置解析。
/// 这些值直接展示给用户，错了会把人引向不生效的文件，所以逐条覆盖。
/// </summary>
public sealed class MemoryBackendInfoTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "shimmer-backend-info", Guid.NewGuid().ToString("N"));
    private readonly DataPaths _paths;

    public MemoryBackendInfoTests()
    {
        Directory.CreateDirectory(_root);
        _paths = new DataPaths(Path.Combine(_root, "Data"));
    }

    [Fact]
    public void ResolveConfigPath_PointsAtStorageSettingsFile()
    {
        MemoryBackendInfo.ResolveConfigPath(_paths)
            .Should().Be(_paths.StorageSettingsFile);

        MemoryBackendInfo.ResolveConfigPath(_paths)
            .Should().EndWith(DataPaths.StorageSettingsFileName);
    }

    [Fact]
    public void BuildSnippet_ReflectsCurrentProvider()
    {
        var config = new VectorStoreConfig { Provider = "File" };

        var json = MemoryBackendInfo.BuildSnippet(config);
        using var document = JsonDocument.Parse(json);
        var section = document.RootElement.GetProperty("VectorStore");

        section.GetProperty("Provider").GetString().Should().Be("File");
        section.TryGetProperty("RootPath", out _).Should().BeFalse();
        section.TryGetProperty("Scope", out _).Should().BeFalse();
    }

    [Fact]
    public void BuildSnippet_IsIndentedForDirectEditing()
    {
        var json = MemoryBackendInfo.BuildSnippet(new VectorStoreConfig());

        json.Should().Contain("\n  \"VectorStore\": {");
        json.Should().Contain("\n    \"Provider\": \"LiteDb\"");
    }

    [Fact]
    public void ResolveDataRoot_LiteDb_PointsAtVectorDatabaseFile()
    {
        var config = new VectorStoreConfig { Provider = "LiteDb" };

        MemoryBackendInfo.ResolveDataRoot(config, _paths).Should().Be(_paths.VectorDatabase);
    }

    [Fact]
    public void ResolveDataRoot_File_PointsAtLocalVectorDirectory()
    {
        var config = new VectorStoreConfig { Provider = "File" };

        MemoryBackendInfo.ResolveDataRoot(config, _paths).Should().Be(_paths.LocalVectorDirectory);
    }

    [Fact]
    public void ResolveDataRoot_InMemory_IsNotApplicable()
    {
        var config = new VectorStoreConfig { Provider = "InMemory" };

        MemoryBackendInfo.ResolveDataRoot(config, _paths).Should().BeNull();
    }

    [Fact]
    public void ResolveDataRoot_InvalidProvider_Throws()
    {
        var config = new VectorStoreConfig { Provider = "Qdrant" };

        var act = () => MemoryBackendInfo.ResolveDataRoot(config, _paths);

        act.Should().Throw<InvalidOperationException>().WithMessage("*Qdrant*");
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch
        {
            // 清理失败不影响断言
        }
    }
}
