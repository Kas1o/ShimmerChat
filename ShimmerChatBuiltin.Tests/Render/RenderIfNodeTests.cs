using FluentAssertions;
using Moq;
using ShimmerChatBuiltin.NodeBasic.Render;
using ShimmerChatLib.Generation;
using ShimmerChatLib.Interface;

namespace ShimmerChatBuiltin.Tests.Render;

/// <summary>
/// RenderIfNode：按渲染位置选择分支，命中执行 Then，未命中执行 Else，分支缺失时内容原样通过。
/// </summary>
public class RenderIfNodeTests
{
    private sealed class SuffixNode : IRenderModifierNode
    {
        private readonly string _suffix;

        public SuffixNode(string suffix) => _suffix = suffix;

        public void Execute(RenderNodeExecutionContext context)
            => context.Env.UpdateContent(context.Env.GetContent() + _suffix, "suffix", nameof(SuffixNode));
    }

    private static RenderEnv CreateEnv(string content = "text")
        => new(content, Mock.Of<ITreeNodeSerializer>(), Mock.Of<IKVDataService>());

    private static RenderNodeExecutionContext CreateContext(RenderEnv env, RenderPosition position)
        => new(env) { Position = position };

    private static RenderIfNode CreateNode(
        RenderPosition position,
        IRenderModifierNode? then = null,
        IRenderModifierNode? otherwise = null)
        => new()
        {
            Position = position,
            Then = then == null ? null : new RenderModifierNodeWrapper(then, "then"),
            Else = otherwise == null ? null : new RenderModifierNodeWrapper(otherwise, "else")
        };

    [Fact]
    public void MatchingPosition_RunsThenBranch()
    {
        var env = CreateEnv();
        var node = CreateNode(RenderPosition.Body,
            new SuffixNode("-body"), new SuffixNode("-thinking"));

        node.Execute(CreateContext(env, RenderPosition.Body));

        env.GetContent().Should().Be("text-body");
    }

    [Fact]
    public void NonMatchingPosition_RunsElseBranch()
    {
        var env = CreateEnv();
        var node = CreateNode(RenderPosition.Body,
            new SuffixNode("-body"), new SuffixNode("-thinking"));

        node.Execute(CreateContext(env, RenderPosition.Thinking));

        env.GetContent().Should().Be("text-thinking");
    }

    [Fact]
    public void MissingElse_LeavesContentUnchanged()
    {
        var env = CreateEnv();
        var node = CreateNode(RenderPosition.Body, new SuffixNode("-body"));

        node.Execute(CreateContext(env, RenderPosition.Thinking));

        env.GetContent().Should().Be("text");
        env.ChangeLog.Should().BeEmpty();
    }
}
