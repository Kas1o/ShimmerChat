using FluentAssertions;
using Moq;
using ShimmerChatLib.Generation;
using ShimmerChatLib.Interface;

namespace ShimmerChatLib.Tests.Generation;

/// <summary>
/// 预设存储版本号：任何经 PipelinePresetStore 的写入都必须让渲染树缓存失效。
/// </summary>
public class PipelinePresetStoreRevisionTests
{
    [Fact]
    public void WriteAll_IncrementsPipelineRevision()
    {
        var pipeline = GenerationPipeline.RenderModifier;
        var before = PipelinePresetStore.GetRevision(pipeline);

        PipelinePresetStore.WriteAll(
            Mock.Of<IKVDataService>(),
            pipeline,
            new List<RenderModifierPreset>
            {
                new() { Id = "p", Name = "p", RootNodeJson = "{}" }
            });

        PipelinePresetStore.GetRevision(pipeline).Should().BeGreaterThan(before);
    }
}
