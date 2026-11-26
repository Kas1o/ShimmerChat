using ShimmerChatLib;

namespace ShimmerChatLib.Tests;

/// <summary>
/// Agent 头像 / 背景图路径规则：只允许 UserUploadImage 下的相对路径，
/// URL 与磁盘路径都必须由同一套规则推导，越界路径一律拒绝。
/// </summary>
public class AgentImagePathTests
{
    private static readonly string Root =
        Path.Combine(Path.GetTempPath(), "shimmer-agent-image", Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("avatar.png", "avatar.png")]
    [InlineData("sub/avatar.png", "sub/avatar.png")]
    [InlineData("sub\\avatar.png", "sub/avatar.png")]
    public void TryNormalize_EmptyOrRelativePath_IsAccepted(string? input, string? expected)
    {
        AgentImagePath.TryNormalize(input, out var normalized).Should().BeTrue();
        normalized.Should().Be(expected);
    }

    [Theory]
    [InlineData("../avatar.png")]
    [InlineData("sub/../../avatar.png")]
    [InlineData("/avatar.png")]
    [InlineData("\\\\server\\share\\avatar.png")]
    [InlineData("C:\\avatar.png")]
    [InlineData("sub//avatar.png")]
    [InlineData("sub/./avatar.png")]
    [InlineData("sub/../avatar.png")]
    [InlineData("sub/avatar?.png")]
    public void TryNormalize_UnsafePath_IsRejected(string input)
    {
        AgentImagePath.TryNormalize(input, out var normalized).Should().BeFalse();
        normalized.Should().BeNull();
    }

    [Fact]
    public void ToUrl_EncodesSegmentsAndUsesUserImagesPrefix()
    {
        AgentImagePath.ToUrl("sub dir/头 像.png").Should().Be("/userimages/sub%20dir/%E5%A4%B4%20%E5%83%8F.png");
    }

    [Fact]
    public void ToUrl_EmptyOrUnsafe_ReturnsNull()
    {
        AgentImagePath.ToUrl(null).Should().BeNull();
        AgentImagePath.ToUrl("  ").Should().BeNull();
        AgentImagePath.ToUrl("../avatar.png").Should().BeNull();
    }

    [Fact]
    public void TryGetFullPath_RelativePath_ResolvesInsideRoot()
    {
        AgentImagePath.TryGetFullPath(Root, "sub/avatar.png", out var fullPath).Should().BeTrue();
        fullPath.Should().Be(Path.GetFullPath(Path.Combine(Root, "sub", "avatar.png")));
    }

    [Fact]
    public void TryGetFullPath_EmptyPath_ReturnsNullPath()
    {
        AgentImagePath.TryGetFullPath(Root, "", out var fullPath).Should().BeTrue();
        fullPath.Should().BeNull();
    }

    [Theory]
    [InlineData("../avatar.png")]
    [InlineData("C:\\avatar.png")]
    [InlineData("/avatar.png")]
    public void TryGetFullPath_UnsafePath_ReturnsFalse(string input)
    {
        AgentImagePath.TryGetFullPath(Root, input, out var fullPath).Should().BeFalse();
        fullPath.Should().BeNull();
    }

    [Fact]
    public void NewFileName_UsesRandomNameAndGivenExtension()
    {
        var fileName = AgentImagePath.NewFileName(".webp");

        fileName.Should().EndWith(".webp");
        fileName.Should().MatchRegex("^[0-9a-f]{32}\\.webp$");
    }

    [Theory]
    [InlineData("png")]
    [InlineData(".p/ng")]
    [InlineData("")]
    public void NewFileName_InvalidExtension_Throws(string extension)
    {
        var act = () => AgentImagePath.NewFileName(extension);

        act.Should().Throw<ArgumentException>();
    }
}
