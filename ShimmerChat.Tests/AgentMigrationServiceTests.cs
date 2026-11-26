using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json;
using ShimmerChat.Singletons;
using ShimmerChatLib;
using ShimmerChatLib.Generation;
using ShimmerChatLib.Interface;

namespace ShimmerChat.Tests;

/// <summary>
/// 启动迁移把旧版 Agent 图像 Guid 落到路径字段，并确保新数据不会被重复改写。
/// </summary>
public class AgentMigrationServiceTests
{
    [Fact]
    public void MigrateAgent_LegacyImageGuid_WritesPathAndClearsGuid()
    {
        var agentGuid = Guid.NewGuid();
        var avatarGuid = Guid.NewGuid();
        var backgroundGuid = Guid.NewGuid();
        var json = JsonConvert.SerializeObject(new
        {
            Guid = agentGuid,
            Name = "Legacy",
            ChatGuids = Array.Empty<Guid>(),
            ModifierTreeJson = "{}",
            AvatarGuid = avatarGuid,
            BackgroundGuid = backgroundGuid
        });

        var kv = new Mock<IKVDataService>();
        kv.Setup(k => k.Read("Agents", agentGuid.ToString())).Returns(json);
        string? saved = null;
        kv.Setup(k => k.Write("Agents", agentGuid.ToString(), It.IsAny<string>()))
            .Callback<string, string, string>((_, _, value) => saved = value);

        var service = CreateService(kv);

        service.MigrateAgent(agentGuid).Should().BeTrue();

        saved.Should().NotBeNull();
        saved!.Should().Contain($"\"AvatarPath\":\"{avatarGuid}.png\"");
        saved.Should().Contain($"\"BackgroundPath\":\"{backgroundGuid}.png\"");
        saved.Should().NotContain("AvatarGuid");
        saved.Should().NotContain("BackgroundGuid");
    }

    [Fact]
    public void MigrateAgent_AlreadyMigrated_ReturnsFalseAndDoesNotSave()
    {
        var agentGuid = Guid.NewGuid();
        var json = JsonConvert.SerializeObject(new
        {
            Guid = agentGuid,
            Name = "Current",
            ChatGuids = Array.Empty<Guid>(),
            ModifierTreeJson = "{}",
            AvatarPath = "avatar.png"
        });

        var kv = new Mock<IKVDataService>();
        kv.Setup(k => k.Read("Agents", agentGuid.ToString())).Returns(json);

        var service = CreateService(kv);

        service.MigrateAgent(agentGuid).Should().BeFalse();

        kv.Verify(k => k.Write("Agents", It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    private static AgentMigrationService CreateService(Mock<IKVDataService> kv)
        => new(
            kv.Object,
            new Mock<IPreGenerationNodeSerializer>().Object,
            NullLogger<AgentMigrationService>.Instance);
}
