using System.Text.Json;
using FluentAssertions;
using ShimmerChatBuiltin.Memory;
using ShimmerChatLib.Models;
using Xunit;

namespace ShimmerChat.Tests.Memory;

/// <summary>
/// 后端配置的只读呈现逻辑：文件定位、JSON 片段、数据目录解析。
/// 这些值直接展示给用户，错了会把人引向不生效的文件，所以逐条覆盖。
/// </summary>
public sealed class MemoryBackendInfoTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "shimmer-backend-info", Guid.NewGuid().ToString("N"));

    public MemoryBackendInfoTests()
    {
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public void ResolveConfigPath_PrefersContentRoot()
    {
        var contentRoot = Path.Combine(_root, "content");
        var baseDir = Path.Combine(_root, "bin");
        Directory.CreateDirectory(contentRoot);
        Directory.CreateDirectory(baseDir);
        File.WriteAllText(Path.Combine(contentRoot, "appsettings.json"), "{}");
        File.WriteAllText(Path.Combine(baseDir, "appsettings.json"), "{}");

        MemoryBackendInfo.ResolveConfigPath(contentRoot, baseDir)
            .Should().Be(Path.GetFullPath(Path.Combine(contentRoot, "appsettings.json")));
    }

    [Fact]
    public void ResolveConfigPath_FallsBackToBaseDirectory()
    {
        var contentRoot = Path.Combine(_root, "content-empty");
        var baseDir = Path.Combine(_root, "bin");
        Directory.CreateDirectory(contentRoot);
        Directory.CreateDirectory(baseDir);
        File.WriteAllText(Path.Combine(baseDir, "appsettings.json"), "{}");

        MemoryBackendInfo.ResolveConfigPath(contentRoot, baseDir)
            .Should().Be(Path.GetFullPath(Path.Combine(baseDir, "appsettings.json")));
    }

    [Fact]
    public void ResolveConfigPath_WhenMissing_ReturnsContentRootCandidate()
    {
        var contentRoot = Path.Combine(_root, "nowhere");
        var baseDir = Path.Combine(_root, "also-nowhere");

        MemoryBackendInfo.ResolveConfigPath(contentRoot, baseDir)
            .Should().Be(Path.GetFullPath(Path.Combine(contentRoot, "appsettings.json")));
    }

    [Fact]
    public void BuildSnippet_ReflectsCurrentConfig()
    {
        var config = new VectorStoreConfig
        {
            Provider = "InMemory",
            Scope = "PerCollection",
            RootPath = @"D:\vec"
        };

        var json = MemoryBackendInfo.BuildSnippet(config);
        using var document = JsonDocument.Parse(json);
        var section = document.RootElement.GetProperty("VectorStore");

        section.GetProperty("Provider").GetString().Should().Be("InMemory");
        section.GetProperty("Scope").GetString().Should().Be("PerCollection");
        section.GetProperty("RootPath").GetString().Should().Be(@"D:\vec");
        section.TryGetProperty("DefaultTopK", out _).Should().BeFalse();
        section.TryGetProperty("DefaultMinScore", out _).Should().BeFalse();
    }

    [Fact]
    public void BuildSnippet_IsIndentedForDirectEditing()
    {
        var json = MemoryBackendInfo.BuildSnippet(new VectorStoreConfig());

        json.Should().Contain("\n  \"VectorStore\": {");
        json.Should().Contain("\n    \"Provider\": \"LiteDb\"");
    }

    [Fact]
    public void ResolveDataRoot_RelativePath_AnchorsToApplicationDirectory()
    {
        var config = new VectorStoreConfig { RootPath = "VectorStore" };

        MemoryBackendInfo.ResolveDataRoot(config)
            .Should().Be(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "VectorStore")));
    }

    [Fact]
    public void ResolveDataRoot_AbsolutePath_IsKept()
    {
        var absolute = Path.Combine(_root, "vectors");
        var config = new VectorStoreConfig { RootPath = absolute };

        MemoryBackendInfo.ResolveDataRoot(config).Should().Be(Path.GetFullPath(absolute));
    }

    [Fact]
    public void ResolveDataRoot_InMemory_IsNotApplicable()
    {
        var config = new VectorStoreConfig { Provider = "InMemory" };

        MemoryBackendInfo.ResolveDataRoot(config).Should().BeNull();
    }

    [Fact]
    public void ResolveDataRoot_InvalidProvider_Throws()
    {
        var config = new VectorStoreConfig { Provider = "Qdrant" };

        var act = () => MemoryBackendInfo.ResolveDataRoot(config);

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
