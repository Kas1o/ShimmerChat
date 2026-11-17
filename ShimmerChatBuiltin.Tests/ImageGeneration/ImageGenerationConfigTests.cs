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
    public void MissingConfig_ReturnsUsableDefaultProfile()
    {
        Setup(null);

        var (config, error) = ImageGenerationConfigManager.Load(_kv.Object);

        error.Should().BeNull();
        config.Should().NotBeNull();
        config!.Version.Should().Be(ImageGenerationConfigManager.CurrentVersion);
        config.Profiles.Should().ContainSingle();

        var profile = config.Profiles[0];
        profile.Id.Should().Be(ImageGenerationConfigManager.DefaultProfileId);
        profile.Provider.Should().Be(ImageApiProvider.ComfyUi);
        profile.BaseUrl.Should().Be("http://127.0.0.1:8188");
        profile.TimeoutSeconds.Should().Be(30);
        profile.MaxWaitSeconds.Should().Be(300);
        profile.DefaultWorkflow.Width.Should().Be(512);
        profile.WorkflowMode.Should().Be(ImageWorkflowMode.Default);
    }

    [Fact]
    public void PinnedProfile_SurvivesRoundTripAndSave()
    {
        var config = ImageGenerationConfig.CreateDefault();
        config.Profiles[0].Pinned = true;

        var json = JsonConvert.SerializeObject(config);
        var restored = JsonConvert.DeserializeObject<ImageGenerationConfig>(json)!;

        restored.Profiles[0].Pinned.Should().BeTrue();
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
        Setup(ImageGenerationTestData.ConfigJson(c => c.DefaultProfile().BaseUrl = "not-a-url"));

        var (config, error) = ImageGenerationConfigManager.Load(_kv.Object);

        config.Should().BeNull();
        error.Should().Contain("BaseUrl");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(601)]
    public void OutOfRangeTimeout_IsRejected(int seconds)
    {
        Setup(ImageGenerationTestData.ConfigJson(c => c.DefaultProfile().TimeoutSeconds = seconds));

        var (config, error) = ImageGenerationConfigManager.Load(_kv.Object);

        config.Should().BeNull();
        error.Should().Contain("TimeoutSeconds");
    }

    [Theory]
    [InlineData(500)]
    [InlineData(513)]
    public void InvalidDefaultWidth_IsRejected(int width)
    {
        Setup(ImageGenerationTestData.ConfigJson(c => c.DefaultProfile().DefaultWorkflow.Width = width));

        var (config, error) = ImageGenerationConfigManager.Load(_kv.Object);

        config.Should().BeNull();
        error.Should().Contain("Width");
    }

    [Fact]
    public void DuplicateProfileIds_AreRejected()
    {
        Setup(ImageGenerationTestData.ConfigJson(c =>
        {
            var duplicate = ImageGenerationTestData.CreateCustomProfile();
            duplicate.Id = ImageGenerationConfigManager.DefaultProfileId;
            duplicate.Name = "Another default";
            c.Profiles.Add(duplicate);
        }));

        var (config, error) = ImageGenerationConfigManager.Load(_kv.Object);

        config.Should().BeNull();
        error.Should().Contain("Duplicate image configuration id");
    }

    [Fact]
    public void DuplicateProfileNames_AreRejected()
    {
        Setup(ImageGenerationTestData.ConfigJson(c =>
        {
            var duplicate = ImageGenerationTestData.CreateCustomProfile();
            duplicate.Name = c.Profiles[0].Name;
            c.Profiles.Add(duplicate);
        }));

        var (config, error) = ImageGenerationConfigManager.Load(_kv.Object);

        config.Should().BeNull();
        error.Should().Contain("Duplicate image configuration name");
    }

    [Fact]
    public void UndeclaredPlaceholder_IsRejected()
    {
        Setup(ImageGenerationTestData.ConfigJson(c =>
        {
            var profile = ImageGenerationTestData.CreateCustomProfile();
            profile.CustomWorkflowJson = """{"a":"{{surprise}}"}""";
            c.Profiles.Add(profile);
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
            var profile = ImageGenerationTestData.CreateCustomProfile();
            profile.CustomWorkflowJson = """{"a":"{{prompt}}","b":"{{model}}","c":{{seed}},"d":{{steps}}}""";
            c.Profiles.Add(profile);
        }));

        var (config, error) = ImageGenerationConfigManager.Load(_kv.Object);

        config.Should().BeNull();
        error.Should().Contain("never appear");
        error.Should().Contain("style");
    }

    [Fact]
    public void EnabledCustomWorkflowWithInvalidJson_IsRejected()
    {
        Setup(ImageGenerationTestData.ConfigJson(c =>
        {
            var profile = ImageGenerationTestData.CreateCustomProfile();
            profile.CustomWorkflowJson = "{ not json";
            c.Profiles.Add(profile);
        }));

        var (config, error) = ImageGenerationConfigManager.Load(_kv.Object);

        config.Should().BeNull();
        error.Should().Contain("not valid JSON");
    }

    [Fact]
    public void DisabledCustomWorkflow_MayBeIncompleteDraft()
    {
        Setup(ImageGenerationTestData.ConfigJson(c =>
        {
            var profile = ImageGenerationTestData.CreateCustomProfile(enabled: false);
            profile.CustomWorkflowJson = "";
            profile.CustomWorkflowParameters = [];
            c.Profiles.Add(profile);
        }));

        var (config, error) = ImageGenerationConfigManager.Load(_kv.Object);

        error.Should().BeNull();
        var draft = config!.Profiles.Single(p => p.Id == ImageGenerationTestData.CustomWorkflowId);
        draft.Enabled.Should().BeFalse();
        draft.CustomWorkflowParameters.Should().BeEmpty();
    }

    [Fact]
    public void OptionalParameterWithInvalidDefault_IsRejected()
    {
        Setup(ImageGenerationTestData.ConfigJson(c =>
        {
            var profile = ImageGenerationTestData.CreateCustomProfile();
            var steps = profile.CustomWorkflowParameters.Single(p => p.Name == "steps");
            steps.DefaultValue = "many";
            c.Profiles.Add(profile);
        }));

        var (config, error) = ImageGenerationConfigManager.Load(_kv.Object);

        config.Should().BeNull();
        error.Should().Contain("must be an integer");
    }

    [Fact]
    public void A1111WithCustomWorkflowMode_IsRejected()
    {
        Setup(ImageGenerationTestData.ConfigJson(c =>
        {
            var profile = ImageGenerationTestData.CreateA1111Profile();
            profile.WorkflowMode = ImageWorkflowMode.Custom;
            c.Profiles.Add(profile);
        }));

        var (config, error) = ImageGenerationConfigManager.Load(_kv.Object);

        config.Should().BeNull();
        error.Should().Contain("does not support custom workflows");
    }

    [Fact]
    public void A1111WithoutModel_IsAccepted()
    {
        Setup(ImageGenerationTestData.ConfigJson(c =>
            c.Profiles.Add(ImageGenerationTestData.CreateA1111Profile())));

        var (config, error) = ImageGenerationConfigManager.Load(_kv.Object);

        error.Should().BeNull();
        config!.Profiles.Should().HaveCount(2);
    }

    [Fact]
    public void LegacyConfig_IsMigratedToProfiles()
    {
        Setup(ImageGenerationTestData.LegacyConfigJson(
            customWorkflows: [ImageGenerationTestData.CreateCustomWorkflow()]));

        var (config, error) = ImageGenerationConfigManager.Load(_kv.Object);

        error.Should().BeNull();
        config!.Version.Should().Be(ImageGenerationConfigManager.CurrentVersion);
        config.Profiles.Should().HaveCount(2);
        config.ComfyUiBaseUrl.Should().BeNull();
        config.CustomWorkflows.Should().BeNull();

        var defaultProfile = config.Profiles[0];
        defaultProfile.Id.Should().Be(ImageGenerationConfigManager.DefaultProfileId);
        defaultProfile.BaseUrl.Should().Be("http://legacy.test:8188");
        defaultProfile.TimeoutSeconds.Should().Be(15);
        defaultProfile.DefaultWorkflow.Model.Should().Be("legacy.safetensors");
        defaultProfile.DefaultWorkflow.Width.Should().Be(640);
        defaultProfile.WorkflowMode.Should().Be(ImageWorkflowMode.Default);

        var portrait = config.Profiles[1];
        portrait.Id.Should().Be(ImageGenerationTestData.CustomWorkflowId);
        portrait.Name.Should().Be("Portrait");
        portrait.WorkflowMode.Should().Be(ImageWorkflowMode.Custom);
        portrait.CustomWorkflowParameters.Should().HaveCount(5);
        portrait.BaseUrl.Should().Be("http://legacy.test:8188");
    }

    [Fact]
    public void LegacyConfigWithoutDefaultWorkflow_OnlyMigratesCustomWorkflows()
    {
        var custom = ImageGenerationTestData.CreateCustomWorkflow();
        custom.Enabled = false;

        Setup(ImageGenerationTestData.LegacyConfigJson(
            includeDefault: false, customWorkflows: [custom]));

        var (config, error) = ImageGenerationConfigManager.Load(_kv.Object);

        error.Should().BeNull();
        config!.Profiles.Should().ContainSingle();
        config.Profiles[0].Id.Should().Be(ImageGenerationTestData.CustomWorkflowId);
        config.Profiles[0].Enabled.Should().BeFalse();
    }

    [Fact]
    public void LegacyCustomWorkflowNamedDefault_KeepsBothProfilesDistinct()
    {
        var custom = ImageGenerationTestData.CreateCustomWorkflow();
        custom.Id = ImageGenerationConfigManager.DefaultProfileId;
        custom.Name = "User default";

        Setup(ImageGenerationTestData.LegacyConfigJson(customWorkflows: [custom]));

        var (config, error) = ImageGenerationConfigManager.Load(_kv.Object);

        error.Should().BeNull();
        config!.Profiles.Should().HaveCount(2);
        config.Profiles.Select(p => p.Id).Should()
            .BeEquivalentTo([$"{ImageGenerationConfigManager.DefaultProfileId}_comfy",
                ImageGenerationConfigManager.DefaultProfileId]);
    }

    [Fact]
    public void LegacyCustomWorkflowAlsoNamedDefault_GetsUniqueMigratedName()
    {
        var custom = ImageGenerationTestData.CreateCustomWorkflow();
        custom.Name = "Default";

        Setup(ImageGenerationTestData.LegacyConfigJson(customWorkflows: [custom]));

        var (config, error) = ImageGenerationConfigManager.Load(_kv.Object);

        error.Should().BeNull();
        config!.Profiles.Should().HaveCount(2);
        config.Profiles.Select(p => p.Name).Should()
            .BeEquivalentTo(["Default", "Default (2)"]);
    }

    [Fact]
    public void Upgrade_IsIdempotent()
    {
        Setup(ImageGenerationTestData.LegacyConfigJson(
            customWorkflows: [ImageGenerationTestData.CreateCustomWorkflow()]));

        var (first, error) = ImageGenerationConfigManager.Load(_kv.Object);
        error.Should().BeNull();

        var second = ImageGenerationConfigManager.Upgrade(first!);

        second.Profiles.Should().HaveSameCount(first!.Profiles);
        second.Version.Should().Be(ImageGenerationConfigManager.CurrentVersion);
    }

    [Fact]
    public void SaveThenLoad_RoundTripsProfiles()
    {
        string? written = null;
        _kv.Setup(k => k.Write("ImageGeneration", "Config", It.IsAny<string>()))
            .Callback<string, string, string>((_, _, value) => written = value);

        var config = ImageGenerationTestData.CreateConfig(c =>
        {
            c.Profiles[0].DefaultWorkflow.Model = "my-model.safetensors";
            c.Profiles[0].DefaultWorkflow.Width = 768;
            c.Profiles[0].DefaultWorkflow.Height = 1024;
            c.Profiles.Add(ImageGenerationTestData.CreateCustomProfile());
            c.Profiles.Add(ImageGenerationTestData.CreateA1111Profile());
        });

        ImageGenerationConfigManager.Save(_kv.Object, config);
        Setup(written);

        var (loaded, error) = ImageGenerationConfigManager.Load(_kv.Object);

        error.Should().BeNull();
        loaded!.Profiles.Should().HaveCount(3);
        loaded.DefaultProfile().DefaultWorkflow.Model.Should().Be("my-model.safetensors");
        loaded.DefaultProfile().DefaultWorkflow.Width.Should().Be(768);
        loaded.DefaultProfile().DefaultWorkflow.Height.Should().Be(1024);
        loaded.FindProfile(ImageGenerationTestData.CustomWorkflowId)!.CustomWorkflowParameters.Should().HaveCount(5);
        loaded.FindProfile(ImageGenerationTestData.A1111ProfileId)!.Provider.Should()
            .Be(ImageApiProvider.Automatic1111);
    }
}
