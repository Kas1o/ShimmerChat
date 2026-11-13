using Newtonsoft.Json;
using ShimmerChatBuiltin.Tests.Generation.Nodes;
using ShimmerChatBuiltin.Tests.SubAgent;
using ShimmerChatLib.Interface;

namespace ShimmerChatBuiltin.Tests.FileSystem;

public class FileSystemToolSetNodeTests : NodeTestBase, IDisposable
{
    private static readonly byte[] PngBytes =
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
        0x00, 0x00, 0x00, 0x0D
    ];

    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "shimmer-fs-node-" + Guid.NewGuid().ToString("N"));

    public FileSystemToolSetNodeTests()
    {
        Directory.CreateDirectory(_directory);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // 临时目录清理失败不影响测试结论。
        }
    }

    private PreNodeExecutionContext CreateContext(IToolRegistry registry)
    {
        var persistent = new PersistentEnv
        {
            KVData = KvMock.Object,
            ToolRegistry = registry,
            Serializer = SerializerMock.Object,
            LocService = LocMock.Object,
            DebugOutput = DebugOutputMock.Object,
            Chat = TestChat,
            Agent = Agent.Create("TestAgent", "")
        };

        return new PreNodeExecutionContext(new PreGenerationEnv(persistent));
    }

    private static StubToolRegistry CreateRegistry(IKVDataService kv)
    {
        return new StubToolRegistry()
            .Register(FileSystemImageReadToolV2.NameKey, typeof(FileSystemImageReadToolV2))
            .SetInstance(typeof(FileSystemImageReadToolV2), new FileSystemImageReadToolV2(kv));
    }

    private static FileSystemToolSetNode CreateImageOnlyNode() => new()
    {
        EnableBrowse = false,
        EnableRead = false,
        EnableImageRead = true,
        EnableWrite = false,
        EnableEdit = false,
        EnableOverview = false
    };

    private static ToolInvocationContext ContextFor(string path) => new()
    {
        ToolCallId = "call-1",
        ToolName = "read_image",
        ArgumentsJson = JsonConvert.SerializeObject(new { path }),
        RoundIndex = 0
    };

    [Fact]
    public async Task EnabledImageRead_AddsScopedToolThatPreservesImageResult()
    {
        var kv = new FakeKVDataService();
        var context = CreateContext(CreateRegistry(kv));
        var path = Path.Combine(_directory, "pixel.png");
        await File.WriteAllBytesAsync(path, PngBytes);

        var nodeResult = await CreateImageOnlyNode().ExecuteAsync(context);

        nodeResult.Success.Should().BeTrue();
        var tool = context.Env.Transient.Tools.Should().ContainSingle().Subject;
        tool.GetDefinition().name.Should().Be("read_image");
        tool.Should().BeAssignableTo<IToolV3>();

        var result = await ((IToolV3)tool).ExecuteAsync(ContextFor(path), CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.Completed);
        result.Images.Should().ContainSingle();
        result.Images![0].MimeType.Should().Be("image/png");
        result.Images[0].Base64.Should().Be(Convert.ToBase64String(PngBytes));
    }

    [Fact]
    public async Task NodeBanList_DeniesImageReadWithStructuredError()
    {
        var kv = new FakeKVDataService();
        var context = CreateContext(CreateRegistry(kv));
        var path = Path.Combine(_directory, "pixel.png");
        await File.WriteAllBytesAsync(path, PngBytes);

        var node = CreateImageOnlyNode();
        node.BanList = ".*";

        var nodeResult = await node.ExecuteAsync(context);
        nodeResult.Success.Should().BeTrue();

        var tool = (IToolV3)context.Env.Transient.Tools.Should().ContainSingle().Subject;
        var result = await tool.ExecuteAsync(ContextFor(path), CancellationToken.None);

        result.Status.Should().Be(ToolResultStatus.Failed);
        result.ErrorCode.Should().Be(ToolErrorCodes.AccessDenied);
        result.ErrorMessage.Should().Contain("Access denied by node scope");
    }

    [Fact]
    public async Task ImageReadDisabled_AddsNoTool()
    {
        var context = CreateContext(CreateRegistry(new FakeKVDataService()));
        var node = CreateImageOnlyNode();
        node.EnableImageRead = false;

        var result = await node.ExecuteAsync(context);

        result.Success.Should().BeTrue();
        context.Env.Transient.Tools.Should().BeEmpty();
    }
}
