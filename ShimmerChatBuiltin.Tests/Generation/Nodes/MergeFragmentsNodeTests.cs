using SharperLLM.Util;
using ShimmerChatBuiltin.Fragments;

namespace ShimmerChatBuiltin.Tests.Generation.Nodes;

public class MergeFragmentsNodeTests : NodeTestBase
{
    [Fact]
    public async Task EmptyFragments_ReturnsSuccess()
    {
        var node = new MergeFragmentsNode();
        var result = await node.ExecuteAsync(CreateContext());
        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task MultipleFragments_MergesIntoOne()
    {
        var node = new MergeFragmentsNode { TargetRole = MergeTargetRole.System };
        var env = new PreGenerationEnv(CreatePersistentEnv());
        env.Transient.Fragments.Add(new ContextSegment
            { Message = new ChatMessage { Content = "A" }, From = PromptBuilder.From.system });
        env.Transient.Fragments.Add(new ContextSegment
            { Message = new ChatMessage { Content = "B" }, From = PromptBuilder.From.user });
        var ctx = CreateContext(env);

        var result = await node.ExecuteAsync(ctx);

        result.Success.Should().BeTrue();
        ctx.Env.Transient.Fragments.Should().HaveCount(1);
        ctx.Env.Transient.Fragments[0].From.Should().Be(PromptBuilder.From.system);
        ctx.Env.Transient.Fragments[0].Message.Content.Should().Contain("A").And.Contain("B");
    }

    [Fact]
    public async Task TargetRole_User_UsesUserFrom()
    {
        var node = new MergeFragmentsNode { TargetRole = MergeTargetRole.User };
        var env = new PreGenerationEnv(CreatePersistentEnv());
        env.Transient.Fragments.Add(new ContextSegment
            { Message = new ChatMessage { Content = "X" }, From = PromptBuilder.From.system });
        var ctx = CreateContext(env);

        await node.ExecuteAsync(ctx);

        ctx.Env.Transient.Fragments[0].From.Should().Be(PromptBuilder.From.user);
    }
}
