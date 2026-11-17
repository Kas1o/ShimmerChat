using System.Text.Json;

namespace ShimmerChatLib.Tests;

public class GenerationPresetTests
{
    [Theory]
    [InlineData(typeof(PreGenerationPreset))]
    [InlineData(typeof(PostGenerationPreset))]
    [InlineData(typeof(RenderModifierPreset))]
    public void Presets_PinnedDefaultsFalseAndSurvivesRoundTrip(Type presetType)
    {
        var preset = (dynamic)Activator.CreateInstance(presetType)!;
        preset.Name = "Preset";
        preset.RootNodeJson = "{}";

        ((bool)preset.Pinned).Should().BeFalse();
        preset.Pinned = true;

        var json = JsonSerializer.Serialize((object)preset, presetType);
        var restored = JsonSerializer.Deserialize(json, presetType)!;
        ((bool)restored.GetType().GetProperty("Pinned")!.GetValue(restored)!).Should().BeTrue();
    }
}
