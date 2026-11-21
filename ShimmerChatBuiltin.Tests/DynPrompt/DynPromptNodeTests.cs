using Newtonsoft.Json;
using SharperLLM.Util;
using ShimmerChatBuiltin.Tests.Generation.Nodes;

namespace ShimmerChatBuiltin.Tests.DynPrompt;

public class DynPromptNodeTests : NodeTestBase
{
    private static void SetupSets(Mock<ShimmerChatLib.Interface.IKVDataService> kv, params DynPromptSet[] sets)
    {
        kv.Setup(k => k.Read("DynPrompt", "DynPromptSets"))
            .Returns(JsonConvert.SerializeObject(sets));
    }

    private static DynPromptSet SetWithTerms(params DynPromptTerm[] terms) => new()
    {
        Name = "Set",
        Terms = terms.ToList()
    };

    private static DynPromptTerm SlotTerm(string name, string content) => new()
    {
        Name = name,
        Content = content,
        InjectionMode = DynPromptTermInjectionMode.InjectionSlot,
        TriggerRule = ""
    };

    [Fact]
    public void LegacyTermJsonWithoutInjectionMode_DefaultsToBeforeSystem()
    {
        // 旧数据没有 InjectionMode 字段时必须落到模型默认值 BeforeSystem，
        // 不能因为新增推荐模式就把既有词条变成注入槽模式。
        const string json = """
            [{"Name":"Set","Terms":[{"Name":"t","Content":"c","TriggerRule":""}]}]
            """;

        var sets = JsonConvert.DeserializeObject<List<DynPromptSet>>(json);

        sets!.Single().Terms.Single().InjectionMode
            .Should().Be(DynPromptTermInjectionMode.BeforeSystem);
    }

    [Fact]
    public void InjectionSlotEnumValue_IsAppendedAfterLegacyValues()
    {
        // 枚举数值是持久化契约：新值只能追加在末尾，重排会让旧数据指向别的模式。
        ((int)DynPromptTermInjectionMode.AtDepth).Should().Be(0);
        ((int)DynPromptTermInjectionMode.BeforeSystem).Should().Be(1);
        ((int)DynPromptTermInjectionMode.AfterSystem).Should().Be(2);
        ((int)DynPromptTermInjectionMode.InjectionSlot).Should().Be(3);
    }

    [Fact]
    public async Task Execute_InjectionSlot_AppendsContentAndKeepsSlotRole()
    {
        SetupSets(KvMock, SetWithTerms(SlotTerm("a", "first")));
        var ctx = CreateContext();

        var slotNode = new DynPromptSlotNode { From = PromptBuilder.From.user };
        await slotNode.ExecuteAsync(ctx);

        var result = await new DynPromptNode().ExecuteAsync(ctx);

        result.Success.Should().BeTrue();
        var slot = ctx.Env.Transient.Fragments.Single(f => DynPromptSlot.IsSlot(f));
        slot.Message.Content.Should().Be("first");
        slot.From.Should().Be(PromptBuilder.From.user);
    }

    [Fact]
    public async Task Execute_InjectionSlot_AppendsMultipleTermsInMatchOrder()
    {
        SetupSets(KvMock, SetWithTerms(SlotTerm("a", "first"), SlotTerm("b", "second")));
        var ctx = CreateContext();
        await new DynPromptSlotNode().ExecuteAsync(ctx);

        var result = await new DynPromptNode().ExecuteAsync(ctx);

        result.Success.Should().BeTrue();
        ctx.Env.Transient.Fragments.Single(f => DynPromptSlot.IsSlot(f))
            .Message.Content.Should().Be("first\nsecond");
    }

    [Fact]
    public async Task Execute_InjectionSlotWithoutSlot_FailsWithMissingCode()
    {
        SetupSets(KvMock, SetWithTerms(SlotTerm("a", "first")));
        var ctx = CreateContext();

        var result = await new DynPromptNode().ExecuteAsync(ctx);

        result.Success.Should().BeFalse();
        result.Code.Should().Be(NodeErrorCodes.InjectionSlotMissing);
        result.Details.Should().Contain(DynPromptSlot.MetadataKey);
    }

    [Fact]
    public async Task Execute_MultipleInjectionSlots_FailsWithAmbiguousCode()
    {
        SetupSets(KvMock, SetWithTerms(SlotTerm("a", "first")));
        var ctx = CreateContext();
        await new DynPromptSlotNode().ExecuteAsync(ctx);
        await new DynPromptSlotNode().ExecuteAsync(ctx);

        var result = await new DynPromptNode().ExecuteAsync(ctx);

        result.Success.Should().BeFalse();
        result.Code.Should().Be(NodeErrorCodes.InjectionSlotAmbiguous);
        ctx.Env.Transient.Fragments.Should().OnlyContain(f => string.IsNullOrEmpty(f.Message.Content));
    }

    [Fact]
    public async Task Execute_InjectionSlotWithNoMatchedTerm_LeavesSlotEmpty()
    {
        // 空 Terms：没有词条匹配，不应因为槽位为空而报错，也不应写入任何内容。
        SetupSets(KvMock, SetWithTerms());
        var ctx = CreateContext();
        await new DynPromptSlotNode().ExecuteAsync(ctx);

        var result = await new DynPromptNode().ExecuteAsync(ctx);

        result.Success.Should().BeTrue();
        ctx.Env.Transient.Fragments.Single(f => DynPromptSlot.IsSlot(f)).Message.Content.Should().BeEmpty();
    }

    [Fact]
    public async Task Execute_NoSlotTerm_DoesNotRequireSlot()
    {
        SetupSets(KvMock, SetWithTerms(new DynPromptTerm
        {
            Name = "legacy",
            Content = "legacy content",
            InjectionMode = DynPromptTermInjectionMode.BeforeSystem,
            TriggerRule = ""
        }));
        var ctx = CreateContext();

        var result = await new DynPromptNode().ExecuteAsync(ctx);

        result.Success.Should().BeTrue();
        ctx.Env.Transient.Fragments.Should().ContainSingle();
        ctx.Env.Transient.Fragments[0].Message.Content.Should().Be("legacy content");
    }
}
