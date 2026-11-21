using SharperLLM.Util;
using ShimmerChatBuiltin.Tests.Generation.Nodes;

namespace ShimmerChatBuiltin.Tests.DynPrompt;

public class DynPromptSlotNodeTests : NodeTestBase
{
    [Fact]
    public async Task Execute_AddsEmptyMarkedSlotWithConfiguredRole()
    {
        var node = new DynPromptSlotNode { From = PromptBuilder.From.user };
        var ctx = CreateContext();

        var result = await node.ExecuteAsync(ctx);

        result.Success.Should().BeTrue();
        ctx.Env.Transient.Fragments.Should().ContainSingle();

        var slot = ctx.Env.Transient.Fragments[0];
        slot.Message.Content.Should().BeEmpty();
        slot.From.Should().Be(PromptBuilder.From.user);
        slot.SourceType.Should().Be(typeof(DynPromptSlotNode));
        slot.Metadata.Should().ContainKey(DynPromptSlot.MetadataKey);
        slot.Metadata[DynPromptSlot.MetadataKey].Should().Be(true);
    }

    [Fact]
    public async Task Execute_ToolResultRole_FailsWithoutAddingSlot()
    {
        LocMock.Setup(l => l["node_err.dynprompt_slot_tool_result_role"]).Returns("tool_result is not allowed");
        var node = new DynPromptSlotNode { From = PromptBuilder.From.tool_result };
        var ctx = CreateContext();

        var result = await node.ExecuteAsync(ctx);

        result.Success.Should().BeFalse();
        result.Code.Should().Be(NodeErrorCodes.ParameterError);
        ctx.Env.Transient.Fragments.Should().BeEmpty();
    }
}
