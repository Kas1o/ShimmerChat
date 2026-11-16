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
    public async Task ConfiguresEveryEnabledProfile()
    {
        var disabledDraft = ImageGenerationTestData.CreateCustomProfile(enabled: false);
        disabledDraft.Id = "draft";
        disabledDraft.Name = "Draft";
        disabledDraft.CustomWorkflowJson = "";
        disabledDraft.CustomWorkflowParameters = [];

        SetupConfig(ImageGenerationTestData.ConfigJson(c =>
        {
            c.Profiles.Add(ImageGenerationTestData.CreateCustomProfile());
            c.Profiles.Add(ImageGenerationTestData.CreateA1111Profile());
            c.Profiles.Add(disabledDraft);
        }));

        var context = CreateContext();
        var result = await new ImageGenerationToolSetNode().ExecuteAsync(context);

        result.Success.Should().BeTrue();
        context.Env.Transient.Tools.Select(t => t.GetDefinition().name)
            .Should().Equal("generate_image", "generate_image_portrait", "generate_image_a1111");
    }

    [Fact]
    public async Task MissingModelForComfyDefaultProfile_Fails()
    {
        SetupConfig(ImageGenerationTestData.ConfigJson(c =>
            c.DefaultProfile().DefaultWorkflow.Model = ""));

        var result = await new ImageGenerationToolSetNode().ExecuteAsync(CreateContext());

        result.Success.Should().BeFalse();
        result.Code.Should().Be(NodeErrorCodes.ConfigNotFound);
        result.Message.Should().Contain("node_err.image_generation_model_missing");
        result.Message.Should().Contain("Default");
    }

    [Fact]
    public async Task A1111WithoutModel_DoesNotFail()
    {
        SetupConfig(ImageGenerationTestData.ConfigJson(c =>
        {
            c.Profiles[0].Enabled = false;
            c.Profiles.Add(ImageGenerationTestData.CreateA1111Profile());
        }));

        var context = CreateContext();
        var result = await new ImageGenerationToolSetNode().ExecuteAsync(context);

        result.Success.Should().BeTrue();
        context.Env.Transient.Tools.Select(t => t.GetDefinition().name)
            .Should().Equal("generate_image_a1111");
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
    public async Task NoEnabledProfile_AddsNothingAndLogs()
    {
        SetupConfig(ImageGenerationTestData.ConfigJson(c => c.Profiles[0].Enabled = false));

        var context = CreateContext();
        var result = await new ImageGenerationToolSetNode().ExecuteAsync(context);

        result.Success.Should().BeTrue();
        context.Env.Transient.Tools.Should().BeEmpty();
        DebugOutputMock.Verify(d => d.Write("ImageGenerationToolSetNode", "ImageGeneration",
            It.Is<string>(m => m.Contains("no enabled image generation configuration"))), Times.Once);
    }

    [Fact]
    public async Task SelectedConfiguration_ExposesOnlyThatTool()
    {
        SetupConfig(ImageGenerationTestData.ConfigJson(c =>
        {
            c.Profiles.Add(ImageGenerationTestData.CreateCustomProfile());
            c.Profiles.Add(ImageGenerationTestData.CreateA1111Profile());
        }));

        var node = new ImageGenerationToolSetNode
        {
            ConfigurationId = ImageGenerationTestData.A1111ProfileId
        };

        var context = CreateContext();
        var result = await node.ExecuteAsync(context);

        result.Success.Should().BeTrue();
        context.Env.Transient.Tools.Select(t => t.GetDefinition().name)
            .Should().Equal("generate_image_a1111");
    }

    [Fact]
    public async Task SelectedConfiguration_NotFound_Fails()
    {
        SetupConfig(ImageGenerationTestData.ConfigJson(c =>
            c.Profiles.Add(ImageGenerationTestData.CreateCustomProfile())));

        var node = new ImageGenerationToolSetNode { ConfigurationId = "missing" };

        var result = await node.ExecuteAsync(CreateContext());

        result.Success.Should().BeFalse();
        result.Code.Should().Be(NodeErrorCodes.ConfigNotFound);
        result.Message.Should().Contain("node_err.image_generation_config_not_found");
    }

    [Fact]
    public async Task SelectedConfiguration_Disabled_FailsInsteadOfSilentlySkipping()
    {
        var draft = ImageGenerationTestData.CreateCustomProfile(enabled: false);
        draft.Id = "draft";
        draft.Name = "Draft";

        SetupConfig(ImageGenerationTestData.ConfigJson(c =>
        {
            c.Profiles.Add(draft);
        }));

        var node = new ImageGenerationToolSetNode { ConfigurationId = "draft" };

        var result = await node.ExecuteAsync(CreateContext());

        result.Success.Should().BeFalse();
        result.Code.Should().Be(NodeErrorCodes.ConfigNotFound);
        result.Message.Should().Contain("node_err.image_generation_config_disabled");
        result.Message.Should().Contain("Draft");
    }
}
