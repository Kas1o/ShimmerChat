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

    protected PersistentEnv CreatePersistentEnv()
    {
        return new PersistentEnv
        {
            KVData = KvMock.Object,
            ToolRegistry = ToolRegistryMock.Object,
            Serializer = SerializerMock.Object,
            LocService = LocMock.Object,
            DebugOutput = DebugOutputMock.Object,
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
