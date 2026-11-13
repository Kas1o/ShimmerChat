using Newtonsoft.Json;
using ShimmerChatBuiltin.ImageGeneration;
using ShimmerChatLib.Interface;

namespace ShimmerChatBuiltin.Tests.ImageGeneration;

public class ImageGenerationConfigTests
{
    private readonly Mock<IKVDataService> _kv = new();

    private void Setup(string? json) =>
        _kv.Setup(k => k.Read("ImageGeneration", "Config")).Returns(json);

    [Fact]
    public void MissingConfig_ReturnsDefaults()
    {
        Setup(null);

        var (config, error) = ImageGenerationConfigManager.Load(_kv.Object);

        error.Should().BeNull();
        config.Should().NotBeNull();
        config!.ComfyUiBaseUrl.Should().Be("http://127.0.0.1:8188");
        config.TimeoutSeconds.Should().Be(30);
        config.MaxWaitSeconds.Should().Be(300);
        config.DefaultWorkflow.Width.Should().Be(512);
        config.CustomWorkflows.Should().BeEmpty();
    }

    [Fact]
    public void BrokenJson_ReturnsError()
    {
        Setup("{ oops");

        var (config, error) = ImageGenerationConfigManager.Load(_kv.Object);

        config.Should().BeNull();
        error.Should().Contain("not valid JSON");
    }

    [Fact]
    public void NonAbsoluteBaseUrl_IsRejected()
    {
        Setup(ImageGenerationTestData.ConfigJson(c => c.ComfyUiBaseUrl = "not-a-url"));

        var (config, error) = ImageGenerationConfigManager.Load(_kv.Object);

        config.Should().BeNull();
        error.Should().Contain("ComfyUiBaseUrl");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(601)]
    public void OutOfRangeTimeout_IsRejected(int seconds)
    {
        Setup(ImageGenerationTestData.ConfigJson(c => c.TimeoutSeconds = seconds));

        var (config, error) = ImageGenerationConfigManager.Load(_kv.Object);

        config.Should().BeNull();
        error.Should().Contain("TimeoutSeconds");
    }

    [Theory]
    [InlineData(500)]
    [InlineData(513)]
    public void InvalidDefaultWidth_IsRejected(int width)
    {
        Setup(ImageGenerationTestData.ConfigJson(c => c.DefaultWorkflow.Width = width));

        var (config, error) = ImageGenerationConfigManager.Load(_kv.Object);

        config.Should().BeNull();
        error.Should().Contain("Width");
    }

    [Fact]
    public void DuplicateCustomWorkflowIds_AreRejected()
    {
        Setup(ImageGenerationTestData.ConfigJson(c =>
        {
            c.CustomWorkflows =
            [
                ImageGenerationTestData.CreateCustomWorkflow(),
                ImageGenerationTestData.CreateCustomWorkflow()
            ];
        }));

        var (config, error) = ImageGenerationConfigManager.Load(_kv.Object);

        config.Should().BeNull();
        error.Should().Contain("Duplicate custom workflow id");
    }

    [Fact]
    public void UndeclaredPlaceholder_IsRejected()
    {
        Setup(ImageGenerationTestData.ConfigJson(c =>
        {
            var workflow = ImageGenerationTestData.CreateCustomWorkflow();
            workflow.WorkflowJson = """{"a":"{{surprise}}"}""";
            c.CustomWorkflows = [workflow];
        }));

        var (config, error) = ImageGenerationConfigManager.Load(_kv.Object);

        config.Should().BeNull();
        error.Should().Contain("undeclared placeholder");
    }

    [Fact]
    public void DeclaredParameterMissingFromTemplate_IsRejected()
    {
        Setup(ImageGenerationTestData.ConfigJson(c =>
        {
            var workflow = ImageGenerationTestData.CreateCustomWorkflow();
            workflow.WorkflowJson = """{"a":"{{prompt}}","b":"{{model}}","c":{{seed}},"d":{{steps}}}""";
            c.CustomWorkflows = [workflow];
        }));

        var (config, error) = ImageGenerationConfigManager.Load(_kv.Object);

        config.Should().BeNull();
        error.Should().Contain("never appear");
        error.Should().Contain("style");
    }

    [Fact]
    public void EnabledWorkflowWithInvalidJson_IsRejected()
    {
        Setup(ImageGenerationTestData.ConfigJson(c =>
        {
            var workflow = ImageGenerationTestData.CreateCustomWorkflow();
            workflow.WorkflowJson = "{ not json";
            c.CustomWorkflows = [workflow];
        }));

        var (config, error) = ImageGenerationConfigManager.Load(_kv.Object);

        config.Should().BeNull();
        error.Should().Contain("not valid JSON");
    }

    [Fact]
    public void DisabledWorkflow_MayBeIncompleteDraft()
    {
        Setup(ImageGenerationTestData.ConfigJson(c =>
        {
            var workflow = ImageGenerationTestData.CreateCustomWorkflow(enabled: false);
            workflow.WorkflowJson = "";
            workflow.Parameters = [];
            c.CustomWorkflows = [workflow];
        }));

        var (config, error) = ImageGenerationConfigManager.Load(_kv.Object);

        error.Should().BeNull();
        config!.CustomWorkflows.Should().ContainSingle();
        config.CustomWorkflows[0].Enabled.Should().BeFalse();
    }

    [Fact]
    public void OptionalParameterWithInvalidDefault_IsRejected()
    {
        Setup(ImageGenerationTestData.ConfigJson(c =>
        {
            var workflow = ImageGenerationTestData.CreateCustomWorkflow();
            var steps = workflow.Parameters.Single(p => p.Name == "steps");
            steps.DefaultValue = "many";
            c.CustomWorkflows = [workflow];
        }));

        var (config, error) = ImageGenerationConfigManager.Load(_kv.Object);

        config.Should().BeNull();
        error.Should().Contain("must be an integer");
    }

    [Fact]
    public void SaveThenLoad_RoundTripsDefaultAndCustomWorkflows()
    {
        string? written = null;
        _kv.Setup(k => k.Write("ImageGeneration", "Config", It.IsAny<string>()))
            .Callback<string, string, string>((_, _, value) => written = value);

        var config = ImageGenerationTestData.CreateConfig(c =>
        {
            c.DefaultWorkflow.Model = "my-model.safetensors";
            c.DefaultWorkflow.Width = 768;
            c.DefaultWorkflow.Height = 1024;
            c.CustomWorkflows = [ImageGenerationTestData.CreateCustomWorkflow()];
        });

        ImageGenerationConfigManager.Save(_kv.Object, config);
        Setup(written);

        var (loaded, error) = ImageGenerationConfigManager.Load(_kv.Object);

        error.Should().BeNull();
        loaded!.ComfyUiBaseUrl.Should().Be("http://127.0.0.1:8188");
        loaded.DefaultWorkflow.Model.Should().Be("my-model.safetensors");
        loaded.DefaultWorkflow.Width.Should().Be(768);
        loaded.DefaultWorkflow.Height.Should().Be(1024);
        loaded.CustomWorkflows.Should().ContainSingle();
        loaded.CustomWorkflows[0].Id.Should().Be(ImageGenerationTestData.CustomWorkflowId);
        loaded.CustomWorkflows[0].Parameters.Should().HaveCount(5);
    }
}
