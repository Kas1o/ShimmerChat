using ShimmerChatLib.Interface;

namespace ShimmerChatBuiltin.Tests.Generation.Nodes;

/// <summary>
/// 节点测试基类，提供 mock PersistentEnv 的快捷构建
/// </summary>
public abstract class NodeTestBase
{
    protected readonly Mock<IKVDataService> KvMock = new();
    protected readonly Mock<IToolRegistry> ToolRegistryMock = new();
    protected readonly Mock<IGenerationNodeSerializer> SerializerMock = new();
    protected readonly Mock<ILocService> LocMock = new();

    protected PersistentEnv CreatePersistentEnv()
    {
        return new PersistentEnv
        {
            KVData = KvMock.Object,
            ChatGuid = Guid.NewGuid(),
            AgentGuid = Guid.NewGuid(),
            ToolRegistry = ToolRegistryMock.Object,
            Serializer = SerializerMock.Object,
            LocService = LocMock.Object,
        };
    }

    protected NodeExecutionContext CreateContext()
    {
        var env = new GenerationEnv(CreatePersistentEnv());
        return new NodeExecutionContext(env);
    }

    protected NodeExecutionContext CreateContext(GenerationEnv env)
    {
        return new NodeExecutionContext(env);
    }
}
