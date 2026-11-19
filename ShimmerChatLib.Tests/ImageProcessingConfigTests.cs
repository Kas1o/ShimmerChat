using ShimmerChatLib;

namespace ShimmerChatLib.Tests;

/// <summary>
/// 配置级输出处理（跟随全局 / 原图 / 自定义）的折算语义。
/// </summary>
public class ImageProcessingConfigTests
{
    private static readonly ImageProcessingOptions Global = new()
    {
        PixelBudget = 1024L * 1024,
        Algorithm = ImageCompressionAlgorithm.Jpeg,
        Quality = 60
    };

    [Fact]
    public void FollowGlobal_ResolvesToTheGlobalSettings()
    {
        var config = new ImageProcessingConfig();

        config.Resolve(Global).Should().BeSameAs(Global);
    }

    [Fact]
    public void FollowGlobal_ResolvesToNullWhenGlobalIsOriginal()
    {
        // 全局原图模式下，跟随全局 = 不处理
        var config = new ImageProcessingConfig();

        config.Resolve(global: null).Should().BeNull();
    }

    [Fact]
    public void Original_AlwaysResolvesToNull()
    {
        var config = new ImageProcessingConfig { Mode = ToolImageOutputProcessing.Original };

        config.Resolve(Global).Should().BeNull();
        config.Resolve(null).Should().BeNull();
    }

    [Fact]
    public void Custom_IgnoresTheGlobalSettings()
    {
        var config = new ImageProcessingConfig
        {
            Mode = ToolImageOutputProcessing.Custom,
            PixelBudget = 2048L * 2048,
            Algorithm = ImageCompressionAlgorithm.Jpeg,
            Quality = 70
        };

        var resolved = config.Resolve(Global);

        resolved.Should().NotBeNull();
        resolved!.PixelBudget.Should().Be(2048L * 2048);
        resolved.Algorithm.Should().Be(ImageCompressionAlgorithm.Jpeg);
        resolved.Quality.Should().Be(70);
    }

    [Fact]
    public void Custom_ConvergesOutOfRangeValuesToLegalBounds()
    {
        // 手改 JSON 可能带出越界值：折算时收敛而不是让解码器收到垃圾参数。
        // 配置校验负责在保存阶段提示；这里是最后防线。
        var config = new ImageProcessingConfig
        {
            Mode = ToolImageOutputProcessing.Custom,
            PixelBudget = 0,
            Quality = 999
        };

        var resolved = config.Resolve(Global);

        resolved!.PixelBudget.Should().Be(ImageAttachmentRules.MinPixelBudget);
        resolved.Quality.Should().Be(ImageAttachmentRules.MaxCompressionQuality);
    }

    [Fact]
    public void ResolveOutput_TreatsMissingConfigAsFollowGlobal()
    {
        // 旧配置数据没有该字段（序列化为 null）：语义是跟随全局，而不是抛 NRE
        ImageProcessingConfig? missing = null;

        ((object?)missing.ResolveOutput(Global)).Should().BeSameAs(Global);
        missing.ResolveOutput(null).Should().BeNull();
    }
}
