using FluentAssertions;
using Moq;
using ShimmerChatBuiltin.NodeBasic.Render;
using ShimmerChatLib.Generation;
using ShimmerChatLib.Interface;

namespace ShimmerChatBuiltin.Tests.Render;

/// <summary>
/// RenderSequenceNode 的输出 / 错误缓存：命中跳过 children，内容、语言、树修订号变化时失效，
/// 流式期间禁止写入，超大输出不进入池子。
/// </summary>
public class RenderSequenceNodeCacheTests
{
    public RenderSequenceNodeCacheTests()
    {
        RenderSequenceCache.ClearForTests();
    }

    private static RenderEnv CreateEnv(string content,
        RenderTreeRevision revision,
        string culture = "zh-CN",
        bool allowCacheWrites = true)
    {
        var loc = new Mock<ILocService>();
        loc.SetupGet(l => l.CurrentCulture).Returns(culture);

        return new RenderEnv(
            content,
            Mock.Of<ITreeNodeSerializer>(),
            Mock.Of<IKVDataService>(),
            loc: loc.Object)
        {
            TreeRevision = revision,
            AllowCacheWrites = allowCacheWrites
        };
    }

    private static RenderEnv Execute(RenderModifierNodeWrapper wrapper, RenderEnv env)
    {
        wrapper.Execute(new RenderNodeExecutionContext(env));
        return env;
    }

    private sealed class CountingNode : IRenderModifierNode
    {
        public int Calls;
        public string? FailureMessage;
        public string Suffix { get; init; } = "!";

        public void Execute(RenderNodeExecutionContext context)
        {
            Calls++;
            if (FailureMessage != null)
                throw new RenderNodeException("TEST_FAIL", FailureMessage);

            context.Env.UpdateContent(
                context.Env.GetContent() + Suffix,
                "counter",
                nameof(CountingNode));
        }
    }

    private static (RenderModifierNodeWrapper Wrapper, CountingNode Child) Build(
        bool cacheOutput, bool cacheErrors)
    {
        var child = new CountingNode();
        var sequence = new RenderSequenceNode
        {
            CacheOutput = cacheOutput,
            CacheErrors = cacheErrors,
            Children = [new RenderModifierNodeWrapper(child, "child")]
        };

        return (new RenderModifierNodeWrapper(sequence, "sequence"), child);
    }

    [Fact]
    public void CacheOutput_HitSkipsChildren()
    {
        var (wrapper, child) = Build(cacheOutput: true, cacheErrors: false);
        var revision = new RenderTreeRevision(1, 1);

        var first = Execute(wrapper, CreateEnv("hello", revision));
        var second = Execute(wrapper, CreateEnv("hello", revision));

        child.Calls.Should().Be(1);
        first.GetContent().Should().Be("hello!");
        second.GetContent().Should().Be("hello!");
    }

    [Fact]
    public void CacheOutput_ContentChanged_Reruns()
    {
        var (wrapper, child) = Build(cacheOutput: true, cacheErrors: false);
        var revision = new RenderTreeRevision(1, 1);

        Execute(wrapper, CreateEnv("hello", revision));
        Execute(wrapper, CreateEnv("hello world", revision));

        child.Calls.Should().Be(2);
    }

    [Fact]
    public void CacheOutput_TreeRevisionChanged_Reruns()
    {
        var (wrapper, child) = Build(cacheOutput: true, cacheErrors: false);

        Execute(wrapper, CreateEnv("hello", new RenderTreeRevision(1, 1)));
        Execute(wrapper, CreateEnv("hello", new RenderTreeRevision(2, 1)));

        child.Calls.Should().Be(2);
    }

    [Fact]
    public void CacheOutput_CultureChanged_Reruns()
    {
        var (wrapper, child) = Build(cacheOutput: true, cacheErrors: false);
        var revision = new RenderTreeRevision(1, 1);

        Execute(wrapper, CreateEnv("hello", revision, culture: "zh-CN"));
        Execute(wrapper, CreateEnv("hello", revision, culture: "en-US"));

        child.Calls.Should().Be(2);
    }

    [Fact]
    public void CacheOutput_WritesDisabled_DoesNotPopulatePool()
    {
        var (wrapper, child) = Build(cacheOutput: true, cacheErrors: false);
        var revision = new RenderTreeRevision(1, 1);

        Execute(wrapper, CreateEnv("hello", revision, allowCacheWrites: false));
        Execute(wrapper, CreateEnv("hello", revision, allowCacheWrites: false));

        child.Calls.Should().Be(2);

        Execute(wrapper, CreateEnv("hello", revision, allowCacheWrites: true));
        child.Calls.Should().Be(3);

        Execute(wrapper, CreateEnv("hello", revision, allowCacheWrites: true));
        child.Calls.Should().Be(3);
    }

    [Fact]
    public void CacheOutput_Disabled_Reruns()
    {
        var (wrapper, child) = Build(cacheOutput: false, cacheErrors: false);
        var revision = new RenderTreeRevision(1, 1);

        Execute(wrapper, CreateEnv("hello", revision));
        Execute(wrapper, CreateEnv("hello", revision));

        child.Calls.Should().Be(2);
    }

    [Fact]
    public void CacheErrors_ReusesFailureWithoutExecutingChildren()
    {
        var (wrapper, child) = Build(cacheOutput: false, cacheErrors: true);
        var revision = new RenderTreeRevision(1, 1);
        child.FailureMessage = "boom";

        var first = FluentActions.Invoking(() => Execute(wrapper, CreateEnv("hello", revision)))
            .Should().Throw<RenderNodeException>();
        first.Which.Code.Should().Be("TEST_FAIL");

        var second = FluentActions.Invoking(() => Execute(wrapper, CreateEnv("hello", revision)))
            .Should().Throw<RenderNodeException>();
        second.Which.Message.Should().Be("boom");
        second.Which.NodeName.Should().Be("child");

        child.Calls.Should().Be(1);
    }

    [Fact]
    public void CacheErrors_Disabled_RerunsChildren()
    {
        var (wrapper, child) = Build(cacheOutput: false, cacheErrors: false);
        var revision = new RenderTreeRevision(1, 1);
        child.FailureMessage = "boom";

        FluentActions.Invoking(() => Execute(wrapper, CreateEnv("hello", revision)))
            .Should().Throw<RenderNodeException>();
        FluentActions.Invoking(() => Execute(wrapper, CreateEnv("hello", revision)))
            .Should().Throw<RenderNodeException>();

        child.Calls.Should().Be(2);
    }

    [Fact]
    public void CacheOutput_OversizedOutput_IsNotCached()
    {
        var child = new OversizedNode();
        var sequence = new RenderSequenceNode
        {
            CacheOutput = true,
            Children = [new RenderModifierNodeWrapper(child, "child")]
        };
        var wrapper = new RenderModifierNodeWrapper(sequence, "sequence");
        var revision = new RenderTreeRevision(1, 1);

        Execute(wrapper, CreateEnv("hello", revision));
        Execute(wrapper, CreateEnv("hello", revision));

        child.Calls.Should().Be(2);
        RenderSequenceCache.CountForTests.Should().Be(0);
    }

    [Fact]
    public void CacheOutput_PoolOverflow_EvictsOldestInsteadOfClearingAll()
    {
        var (wrapper, child) = Build(cacheOutput: true, cacheErrors: false);
        var revision = new RenderTreeRevision(1, 1);

        for (var i = 0; i < RenderSequenceCache.MaxEntries + 1; i++)
            Execute(wrapper, CreateEnv($"input-{i}", revision));

        var callsBefore = child.Calls;

        // 次早的条目仍在池子里，直接命中（同时把它移到最近使用）。
        Execute(wrapper, CreateEnv("input-1", revision));
        child.Calls.Should().Be(callsBefore);

        // 最早的条目被淘汰，重新执行一次。
        Execute(wrapper, CreateEnv("input-0", revision));
        child.Calls.Should().Be(callsBefore + 1);
    }

    private sealed class OversizedNode : IRenderModifierNode
    {
        public int Calls;

        public void Execute(RenderNodeExecutionContext context)
        {
            Calls++;
            context.Env.UpdateContent(
                new string('x', RenderSequenceCache.MaxEntryChars + 1),
                "oversized",
                nameof(OversizedNode));
        }
    }
}
