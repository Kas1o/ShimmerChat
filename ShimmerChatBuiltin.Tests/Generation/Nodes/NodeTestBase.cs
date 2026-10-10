using SharperLLM.Util;
using ShimmerChatLib.Interface;

namespace ShimmerChatBuiltin.Tests.Generation.Nodes;

/// <summary>
/// 节点测试基类，提供 mock PersistentEnv 的快捷构建
/// </summary>
public abstract class NodeTestBase
{
    protected readonly Mock<IKVDataService> KvMock = new();
    protected readonly Mock<IToolRegistry> ToolRegistryMock = new();
    protected readonly Mock<IPreGenerationNodeSerializer> SerializerMock = new();
    protected readonly Mock<ILocService> LocMock = new();
    protected readonly Mock<IDebugOutputService> DebugOutputMock = new();

    /// <summary>
    /// 默认的图像附件解析桩：把附件里已有的 Base64 原样转成请求内容块（不含 I/O）。
    /// 需要模拟解析失败的测试可以自行设置 Setup。
    /// </summary>
    protected readonly Mock<IImageAttachmentService> ImageAttachmentsMock = new();

    protected NodeTestBase()
    {
        ImageAttachmentsMock
            .Setup(s => s.ToChatImageAsync(It.IsAny<MessageImage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((MessageImage image, CancellationToken _) =>
                ChatImage.FromBase64(image.Base64 ?? "resolved", image.MimeType));
    }

    protected PersistentEnv CreatePersistentEnv()
    {
        return new PersistentEnv
        {
            KVData = KvMock.Object,
            ToolRegistry = ToolRegistryMock.Object,
            Serializer = SerializerMock.Object,
            LocService = LocMock.Object,
            DebugOutput = DebugOutputMock.Object,
            ImageAttachments = ImageAttachmentsMock.Object,
            Chat = new Chat { Name = "TestChat" },
            Agent = Agent.Create("TestAgent", "")
        };
    }

    protected PreNodeExecutionContext CreateContext()
    {
        var env = new PreGenerationEnv(CreatePersistentEnv());
        return new PreNodeExecutionContext(env);
    }

    protected PreNodeExecutionContext CreateContext(PreGenerationEnv env)
    {
        return new PreNodeExecutionContext(env);
    }
}
