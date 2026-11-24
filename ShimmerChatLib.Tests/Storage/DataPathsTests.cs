using FluentAssertions;
using ShimmerChatLib.Storage;
using Xunit;

namespace ShimmerChatLib.Tests.Storage;

/// <summary>
/// 数据目录布局是单一事实来源：所有落盘路径都从 DataPaths 推导，测试锁定草图约定。
/// </summary>
public sealed class DataPathsTests
{
    private readonly string _base = Path.Combine(Path.GetTempPath(), "shimmer-paths", Guid.NewGuid().ToString("N"));

    [Fact]
    public void DefaultLayout_MatchesDataDirectorySketch()
    {
        var data = Path.Combine(_base, "Data");
        var paths = new DataPaths(data);

        paths.Root.Should().Be(Path.GetFullPath(data));
        paths.StorageSettingsFile.Should().Be(Path.Combine(data, "storage_settings.json"));
        paths.KvDataDatabase.Should().Be(Path.Combine(data, "LiteDB", "kvdata.db"));
        paths.ChatMessageDatabase.Should().Be(Path.Combine(data, "LiteDB", "chat_message.db"));
        paths.DebugOutputDatabase.Should().Be(Path.Combine(data, "LiteDB", "debug_output.db"));
        paths.VectorDatabase.Should().Be(Path.Combine(data, "LiteDB", "vector.db"));
        paths.MemoryDatabase.Should().Be(Path.Combine(data, "LiteDB", "memory.db"));
        paths.LocalKvDataDirectory.Should().Be(Path.Combine(data, "LocalFileStorage", "kvdata"));
        paths.LocalChatMessageDirectory.Should().Be(Path.Combine(data, "LocalFileStorage", "chat_message"));
        paths.LocalDebugOutputDirectory.Should().Be(Path.Combine(data, "LocalFileStorage", "debug_output"));
        paths.LocalVectorDirectory.Should().Be(Path.Combine(data, "LocalFileStorage", "vector"));
        paths.UserUploadImageDirectory.Should().Be(Path.Combine(data, "UserUploadImage"));
    }

    [Fact]
    public void Create_WithoutConfiguredRoot_UsesDataUnderBaseDirectory()
    {
        var paths = DataPaths.Create(_base, null);

        paths.Root.Should().Be(Path.GetFullPath(Path.Combine(_base, DataPaths.DefaultRootDirectoryName)));
    }

    [Fact]
    public void Create_RelativeRoot_AnchorsToBaseDirectory()
    {
        var paths = DataPaths.Create(_base, "storage/data");

        paths.Root.Should().Be(Path.GetFullPath(Path.Combine(_base, "storage", "data")));
    }

    [Fact]
    public void Create_AbsoluteRoot_IsKept()
    {
        var absolute = Path.Combine(_base, "elsewhere");

        var paths = DataPaths.Create(Path.Combine(_base, "bin"), absolute);

        paths.Root.Should().Be(Path.GetFullPath(absolute));
    }

    [Fact]
    public void EnsureDirectories_CreatesOnlyStableRoots()
    {
        var paths = DataPaths.Create(_base, null);

        paths.EnsureDirectories();

        Directory.Exists(paths.Root).Should().BeTrue();
        Directory.Exists(paths.LiteDbDirectory).Should().BeTrue();
        Directory.Exists(paths.LocalFileStorageDirectory).Should().BeTrue();
        Directory.Exists(paths.UserUploadImageDirectory).Should().BeTrue();
    }
}
