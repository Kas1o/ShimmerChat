using ShimmerChatBuiltin.ImageGeneration;
using ShimmerChatBuiltin.Tests.Generation.Nodes;
using ShimmerChatLib.Interface;

namespace ShimmerChatBuiltin.Tests.ImageGeneration;

public class ImageGenerationToolSetNodeTests : NodeTestBase
{
    private PreNodeExecutionContext CreateContext(IImageAttachmentService? attachments = null)
    {
        var persistent = new PersistentEnv
        {
            KVData = KvMock.Object,
            ToolRegistry = ToolRegistryMock.Object,
            Serializer = SerializerMock.Object,
            LocService = new ImageGenerationStubLocService(),
            DebugOutput = DebugOutputMock.Object,
            ImageAttachments = attachments ?? ImageAttachmentsMock.Object,
            Chat = TestChat,
            Agent = Agent.Create("TestAgent", "")
        };

        return new PreNodeExecutionContext(new PreGenerationEnv(persistent));
    }

    private void SetupConfig(string? json) =>
        KvMock.Setup(k => k.Read("ImageGeneration", "Config")).Returns(json);

    [Fact]
    public async Task ConfiguresExpectedToolNames()
    {
        var disabledDraft = ImageGenerationTestData.CreateCustomWorkflow(enabled: false);
        disabledDraft.Id = "draft";
        disabledDraft.Name = "Draft";
        disabledDraft.WorkflowJson = "";
        disabledDraft.Parameters = [];

        SetupConfig(ImageGenerationTestData.ConfigJson(c =>
            c.CustomWorkflows =
            [
                ImageGenerationTestData.CreateCustomWorkflow(),
                disabledDraft
            ]));

        var context = CreateContext();
        var result = await new ImageGenerationToolSetNode().ExecuteAsync(context);

        result.Success.Should().BeTrue();
        context.Env.Transient.Tools.Select(t => t.GetDefinition().name)
            .Should().Equal("generate_image", "generate_image_portrait");
    }

    [Fact]
    public async Task MissingModel_Fails()
    {
        SetupConfig(ImageGenerationTestData.ConfigJson(c => c.DefaultWorkflow.Model = ""));

        var result = await new ImageGenerationToolSetNode().ExecuteAsync(CreateContext());

        result.Success.Should().BeFalse();
        result.Code.Should().Be(NodeErrorCodes.ConfigNotFound);
        result.Message.Should().Contain("node_err.image_generation_model_missing");
    }

    [Fact]
    public async Task MissingImageStorage_Fails()
    {
        SetupConfig(ImageGenerationTestData.ConfigJson());

        var persistent = new PersistentEnv
        {
            KVData = KvMock.Object,
            ToolRegistry = ToolRegistryMock.Object,
            Serializer = SerializerMock.Object,
            LocService = new ImageGenerationStubLocService(),
            DebugOutput = DebugOutputMock.Object,
            ImageAttachments = null,
            Chat = TestChat,
            Agent = Agent.Create("TestAgent", "")
        };

        var result = await new ImageGenerationToolSetNode()
            .ExecuteAsync(new PreNodeExecutionContext(new PreGenerationEnv(persistent)));

        result.Success.Should().BeFalse();
        result.Code.Should().Be(NodeErrorCodes.ConfigNotFound);
        result.Message.Should().Contain("node_err.image_generation_attachments_unavailable");
    }

    [Fact]
    public async Task InvalidConfig_Fails()
    {
        SetupConfig("{ broken");

        var result = await new ImageGenerationToolSetNode().ExecuteAsync(CreateContext());

        result.Success.Should().BeFalse();
        result.Code.Should().Be(NodeErrorCodes.DataMissing);
        result.Details.Should().Contain("not valid JSON");
    }

    [Fact]
    public async Task BothSourcesDisabled_AddsNothing()
    {
        SetupConfig(ImageGenerationTestData.ConfigJson());
        var node = new ImageGenerationToolSetNode
        {
            EnableDefaultWorkflow = false,
            EnableCustomWorkflows = false
        };

        var context = CreateContext();
        var result = await node.ExecuteAsync(context);

        result.Success.Should().BeTrue();
        context.Env.Transient.Tools.Should().BeEmpty();
    }

    [Fact]
    public async Task SelectedCustomWorkflow_ExposesOnlyThatTool()
    {
        var second = ImageGenerationTestData.CreateCustomWorkflow();
        second.Id = "landscape";
        second.Name = "Landscape";

        SetupConfig(ImageGenerationTestData.ConfigJson(c =>
            c.CustomWorkflows =
            [
                ImageGenerationTestData.CreateCustomWorkflow(),
                second
            ]));

        var node = new ImageGenerationToolSetNode
        {
            EnableDefaultWorkflow = false,
            CustomWorkflowId = "portrait"
        };

        var context = CreateContext();
        var result = await node.ExecuteAsync(context);

        result.Success.Should().BeTrue();
        context.Env.Transient.Tools.Select(t => t.GetDefinition().name)
            .Should().Equal("generate_image_portrait");
    }

    [Fact]
    public async Task SelectedCustomWorkflow_NotFound_Fails()
    {
        SetupConfig(ImageGenerationTestData.ConfigJson(c =>
            c.CustomWorkflows = [ImageGenerationTestData.CreateCustomWorkflow()]));

        var node = new ImageGenerationToolSetNode
        {
            EnableDefaultWorkflow = false,
            CustomWorkflowId = "missing"
        };

        var result = await node.ExecuteAsync(CreateContext());

        result.Success.Should().BeFalse();
        result.Code.Should().Be(NodeErrorCodes.ConfigNotFound);
        result.Message.Should().Contain("node_err.image_generation_workflow_not_found");
    }

    [Fact]
    public async Task SelectedCustomWorkflow_Disabled_FailsInsteadOfSilentlySkipping()
    {
        var draft = ImageGenerationTestData.CreateCustomWorkflow(enabled: false);
        draft.Id = "draft";
        draft.Name = "Draft";

        SetupConfig(ImageGenerationTestData.ConfigJson(c =>
            c.CustomWorkflows = [draft]));

        var node = new ImageGenerationToolSetNode
        {
            EnableDefaultWorkflow = false,
            CustomWorkflowId = "draft"
        };

        var result = await node.ExecuteAsync(CreateContext());

        result.Success.Should().BeFalse();
        result.Code.Should().Be(NodeErrorCodes.ConfigNotFound);
        result.Message.Should().Contain("node_err.image_generation_workflow_disabled");
    }

    [Fact]
    public async Task NoEnabledCustomWorkflows_AddsDefaultAndLogs()
    {
        var disabledDraft = ImageGenerationTestData.CreateCustomWorkflow(enabled: false);
        disabledDraft.WorkflowJson = "";
        disabledDraft.Parameters = [];

        SetupConfig(ImageGenerationTestData.ConfigJson(c =>
            c.CustomWorkflows = [disabledDraft]));

        var context = CreateContext();
        var result = await new ImageGenerationToolSetNode().ExecuteAsync(context);

        result.Success.Should().BeTrue();
        context.Env.Transient.Tools.Select(t => t.GetDefinition().name)
            .Should().Equal("generate_image");
        DebugOutputMock.Verify(d => d.Write("ImageGenerationToolSetNode", "ImageGeneration",
            It.Is<string>(m => m.Contains("no custom image workflow is enabled"))), Times.Once);
    }
}
