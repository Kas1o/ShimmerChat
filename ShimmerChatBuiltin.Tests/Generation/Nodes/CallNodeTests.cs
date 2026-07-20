using Newtonsoft.Json;
using ShimmerChatLib.Generation;

namespace ShimmerChatBuiltin.Tests.Generation.Nodes;

public class CallNodeTests : NodeTestBase
{
    private static string CreatePresetsJson(string id, string? rootNodeJson)
    {
        var presets = new List<PreGenerationPreset>
        {
            new() { Id = id, Name = "Test Preset", RootNodeJson = rootNodeJson ?? "" }
        };
        return JsonConvert.SerializeObject(presets);
    }

    [Fact]
    public async Task EmptyPresetId_ReturnsFailure()
    {
        var node = new CallNode { PresetId = "" };
        var result = await node.ExecuteAsync(CreateContext());
        result.Success.Should().BeFalse();
        result.Code.Should().Be(NodeErrorCodes.DataMissing);
    }

    [Fact]
    public async Task NoPresetsInKVData_ReturnsFailure()
    {
        KvMock.Setup(k => k.Read("GenerationManager", "generation_presets")).Returns((string?)null);
        var node = new CallNode { PresetId = "missing" };
        var result = await node.ExecuteAsync(CreateContext());
        result.Success.Should().BeFalse();
        result.Code.Should().Be(NodeErrorCodes.DataMissing);
    }

    [Fact]
    public async Task PresetNotFound_ReturnsFailure()
    {
        KvMock.Setup(k => k.Read("GenerationManager", "generation_presets"))
            .Returns(CreatePresetsJson("other", "{}"));
        var node = new CallNode { PresetId = "not-found" };
        var result = await node.ExecuteAsync(CreateContext());
        result.Success.Should().BeFalse();
        result.Code.Should().Be(NodeErrorCodes.PresetNotFound);
    }

    [Fact]
    public async Task EmptyRootNodeJson_ReturnsFailure()
    {
        KvMock.Setup(k => k.Read("GenerationManager", "generation_presets"))
            .Returns(CreatePresetsJson("empty", ""));
        var node = new CallNode { PresetId = "empty" };
        var result = await node.ExecuteAsync(CreateContext());
        result.Success.Should().BeFalse();
        result.Code.Should().Be(NodeErrorCodes.DataMissing);
    }

    [Fact]
    public async Task ValidPreset_ExecutesChildNode()
    {
        var childNode = new Mock<IPreGenerationNode>();
        childNode.Setup(n => n.ExecuteAsync(It.IsAny<PreNodeExecutionContext>()))
            .ReturnsAsync(NodeResult.SuccessResult());
        var childJson = "{\"$type\":\"MockNode\"}";
        KvMock.Setup(k => k.Read("GenerationManager", "generation_presets"))
            .Returns(CreatePresetsJson("valid", childJson));
        SerializerMock.Setup(s => s.Deserialize(childJson)).Returns(childNode.Object);
        var node = new CallNode { PresetId = "valid" };
        var ctx = CreateContext();

        var result = await node.ExecuteAsync(ctx);

        result.Success.Should().BeTrue();
        childNode.Verify(n => n.ExecuteAsync(It.IsAny<PreNodeExecutionContext>()), Times.Once);
    }
}
