using SharperLLM.Util;

namespace ShimmerChatBuiltin.Tests.Generation.Nodes;

public class FragmentNodeTests : NodeTestBase
{
    [Fact]
    public async Task Execute_AddsSegmentToFragments()
    {
        var node = new FragmentNode { Content = "Hello", From = PromptBuilder.From.user };
        var ctx = CreateContext();

        await node.ExecuteAsync(ctx);

        ctx.Env.Transient.Fragments.Should().HaveCount(1);
        ctx.Env.Transient.Fragments[0].Message.Content.Should().Be("Hello");
    }

    [Fact]
    public async Task Execute_SystemRole_CorrectFrom()
    {
        var node = new FragmentNode { Content = "System prompt", From = PromptBuilder.From.system };
        var ctx = CreateContext();

        await node.ExecuteAsync(ctx);

        ctx.Env.Transient.Fragments[0].From.Should().Be(PromptBuilder.From.system);
        ctx.Env.Transient.Fragments[0].SourceType.Should().Be(typeof(FragmentNode));
    }

    [Fact]
    public async Task Execute_UserRole_CorrectFrom()
    {
        var node = new FragmentNode { Content = "User message", From = PromptBuilder.From.user };
        var ctx = CreateContext();

        await node.ExecuteAsync(ctx);

        ctx.Env.Transient.Fragments[0].From.Should().Be(PromptBuilder.From.user);
    }
}
