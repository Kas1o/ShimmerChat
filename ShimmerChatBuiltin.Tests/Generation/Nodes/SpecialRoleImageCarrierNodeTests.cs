using SharperLLM.API;
using SharperLLM.Util;

namespace ShimmerChatBuiltin.Tests.Generation.Nodes;

public class SpecialRoleImageCarrierNodeTests : NodeTestBase
{
    private static ContextSegment Segment(
        PromptBuilder.From from,
        string content,
        string? id = null,
        params string[] imageData)
    {
        var message = new ChatMessage { Content = content, id = id };
        if (imageData.Length > 0)
        {
            message.Images = imageData
                .Select(data => ChatImage.FromBase64(data, "image/png"))
                .ToList();
        }

        return new ContextSegment
        {
            Message = message,
            From = from,
            Metadata = { ["sender"] = from.ToString() }
        };
    }

    private static void AddAssistantToolCalls(ContextSegment segment, params string[] ids)
    {
        segment.Message.toolCalls = ids
            .Select(id => new ToolCall { id = id, name = "ask_user", arguments = "{}" })
            .ToList();
    }

    [Fact]
    public async Task SystemImage_MovesToPseudoUserAfterTheSystemMessage()
    {
        var original = Segment(PromptBuilder.From.system, "rules", null, "AAA");
        var ctx = CreateContext();
        ctx.Env.Transient.Fragments.Add(original);
        ctx.Env.Transient.Fragments.Add(Segment(PromptBuilder.From.user, "hi"));

        var result = await new SpecialRoleImageCarrierNode().ExecuteAsync(ctx);

        result.Success.Should().BeTrue();
        var fragments = ctx.Env.Transient.Fragments;
        fragments.Should().HaveCount(3);

        fragments[0].From.Should().Be(PromptBuilder.From.system);
        fragments[0].Message.Content.Should().Be("rules");
        fragments[0].Message.Images.Should().BeNull();

        fragments[1].From.Should().Be(PromptBuilder.From.user);
        fragments[1].Message.Images.Should().ContainSingle().Which.Base64.Should().Be("AAA");
        fragments[1].Message.Content.Should().Contain("system");
        fragments[1].Metadata[SpecialRoleImageCarrierNode.CarrierMetadataKey].Should().Be("synthetic");

        fragments[2].Message.Content.Should().Be("hi");

        fragments[0].Metadata[SpecialRoleImageCarrierNode.GroupMetadataKey]
            .Should().Be(fragments[1].Metadata[SpecialRoleImageCarrierNode.GroupMetadataKey]);
        // 其他元数据在拆分时保留
        fragments[0].Metadata["sender"].Should().Be("system");

        // 原始片段不被就地修改
        original.Message.Images.Should().ContainSingle();
        fragments[0].Message.Should().NotBeSameAs(original.Message);
    }

    [Fact]
    public async Task ToolResultImages_AreCombinedAfterTheWholeToolRun()
    {
        var assistant = Segment(PromptBuilder.From.assistant, "");
        AddAssistantToolCalls(assistant, "c1", "c2");

        var tool1 = Segment(PromptBuilder.From.tool_result, "r1", "c1", "AAA");
        var tool2 = Segment(PromptBuilder.From.tool_result, "r2", "c2", "BBB");

        var ctx = CreateContext();
        ctx.Env.Transient.Fragments.Add(assistant);
        ctx.Env.Transient.Fragments.Add(tool1);
        ctx.Env.Transient.Fragments.Add(tool2);
        ctx.Env.Transient.Fragments.Add(Segment(PromptBuilder.From.user, "next"));

        var result = await new SpecialRoleImageCarrierNode().ExecuteAsync(ctx);

        result.Success.Should().BeTrue();
        var fragments = ctx.Env.Transient.Fragments;
        fragments.Should().HaveCount(5);

        // assistant(tool_calls) 与两条 tool 消息之间不得插入 user 消息
        fragments[0].From.Should().Be(PromptBuilder.From.assistant);
        fragments[1].From.Should().Be(PromptBuilder.From.tool_result);
        fragments[2].From.Should().Be(PromptBuilder.From.tool_result);
        fragments[1].Message.id.Should().Be("c1");
        fragments[2].Message.id.Should().Be("c2");
        fragments[1].Message.Images.Should().BeNull();
        fragments[2].Message.Images.Should().BeNull();

        var carrier = fragments[3];
        carrier.From.Should().Be(PromptBuilder.From.user);
        carrier.Message.Images.Should().HaveCount(2);
        carrier.Message.Images![0].Base64.Should().Be("AAA");
        carrier.Message.Images[1].Base64.Should().Be("BBB");
        carrier.Message.Content.Should().Contain("c1").And.Contain("c2");

        // 同一条工具结果段共享一个分组标记
        var group = carrier.Metadata[SpecialRoleImageCarrierNode.GroupMetadataKey];
        fragments[1].Metadata[SpecialRoleImageCarrierNode.GroupMetadataKey].Should().Be(group);
        fragments[2].Metadata[SpecialRoleImageCarrierNode.GroupMetadataKey].Should().Be(group);

        // 原始片段保持不变
        tool1.Message.Images.Should().ContainSingle();
        tool2.Message.Images.Should().ContainSingle();
    }

    [Fact]
    public async Task AssistantImage_IsMovedBeforeTheAssistantToKeepToolPairing()
    {
        var assistant = Segment(PromptBuilder.From.assistant, "", null, "AAA");
        AddAssistantToolCalls(assistant, "c1");
        var tool = Segment(PromptBuilder.From.tool_result, "r1", "c1");

        var ctx = CreateContext();
        ctx.Env.Transient.Fragments.Add(assistant);
        ctx.Env.Transient.Fragments.Add(tool);

        var result = await new SpecialRoleImageCarrierNode().ExecuteAsync(ctx);

        result.Success.Should().BeTrue();
        var fragments = ctx.Env.Transient.Fragments;
        fragments.Should().HaveCount(3);

        fragments[0].From.Should().Be(PromptBuilder.From.user);
        fragments[0].Message.Images.Should().ContainSingle().Which.Base64.Should().Be("AAA");
        fragments[1].From.Should().Be(PromptBuilder.From.assistant);
        fragments[1].Message.Images.Should().BeNull();
        fragments[1].Message.toolCalls.Should().ContainSingle().Which.id.Should().Be("c1");
        fragments[2].From.Should().Be(PromptBuilder.From.tool_result);

        fragments[0].Metadata[SpecialRoleImageCarrierNode.GroupMetadataKey]
            .Should().Be(fragments[1].Metadata[SpecialRoleImageCarrierNode.GroupMetadataKey]);
    }

    [Fact]
    public async Task DisabledRole_LeavesItsImagesInPlace()
    {
        var assistant = Segment(PromptBuilder.From.assistant, "");
        AddAssistantToolCalls(assistant, "c1");
        var tool = Segment(PromptBuilder.From.tool_result, "r1", "c1", "AAA");

        var ctx = CreateContext();
        ctx.Env.Transient.Fragments.Add(assistant);
        ctx.Env.Transient.Fragments.Add(tool);

        var result = await new SpecialRoleImageCarrierNode { CarryToolResultImages = false }
            .ExecuteAsync(ctx);

        result.Success.Should().BeTrue();
        ctx.Env.Transient.Fragments.Should().HaveCount(2);
        ctx.Env.Transient.Fragments[1].Message.Images.Should().ContainSingle();
    }

    [Fact]
    public async Task UserImages_AreNeverMoved()
    {
        var user = Segment(PromptBuilder.From.user, "look", null, "AAA");
        var ctx = CreateContext();
        ctx.Env.Transient.Fragments.Add(user);

        var result = await new SpecialRoleImageCarrierNode().ExecuteAsync(ctx);

        result.Success.Should().BeTrue();
        ctx.Env.Transient.Fragments.Should().ContainSingle()
            .Which.Should().BeSameAs(user);
        user.Message.Images.Should().ContainSingle();
    }

    [Fact]
    public async Task MissingImageData_FailsWithoutRewritingTheContext()
    {
        var assistant = Segment(PromptBuilder.From.assistant, "");
        AddAssistantToolCalls(assistant, "c1");
        var tool = Segment(PromptBuilder.From.tool_result, "r1", "c1");
        tool.Message.Images = [new ChatImage { MimeType = "image/png", Base64 = "" }];

        var ctx = CreateContext();
        ctx.Env.Transient.Fragments.Add(assistant);
        ctx.Env.Transient.Fragments.Add(tool);

        var result = await new SpecialRoleImageCarrierNode().ExecuteAsync(ctx);

        result.Success.Should().BeFalse();
        result.Code.Should().Be(NodeErrorCodes.DataMissing);
        ctx.Env.Transient.Fragments.Should().HaveCount(2);
        ctx.Env.Transient.Fragments[1].Message.Images.Should().ContainSingle();
    }
}
