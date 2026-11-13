using ShimmerChatBuiltin.CodeAct;
using ShimmerChatBuiltin.Tests.Generation.Nodes;

namespace ShimmerChatBuiltin.Tests.CodeAct;

/// <summary>
/// CodeAct 前生成节点：注册工具、拒绝重复节点，且注册的是活工具列表（后续添加的工具也能被脚本看到）。
/// </summary>
public class CodeActNodeTests : NodeTestBase
{
    [Fact]
    public async Task Execute_AddsCodeActTool()
    {
        var context = CreateContext();

        var result = await new CodeActNode().ExecuteAsync(context);

        result.Success.Should().BeTrue();
        var tool = context.Env.Transient.Tools.Should().ContainSingle().Subject;
        tool.Should().BeOfType<CodeActToolV2>();
        tool.GetDefinition().name.Should().Be(CodeActToolV2.ToolName);
    }

    [Fact]
    public async Task Execute_SecondCodeActNode_FailsExplicitly()
    {
        LocMock.Setup(l => l["node_err.codeact_duplicate"]).Returns("duplicate codeact");

        var context = CreateContext();
        (await new CodeActNode().ExecuteAsync(context)).Success.Should().BeTrue();

        var second = new CodeActNode();
        var result = await second.ExecuteAsync(context);

        result.Success.Should().BeFalse();
        result.Code.Should().Be(NodeErrorCodes.ParameterError);
        result.Message.Should().Be("duplicate codeact");
        result.NodeId.Should().Be(second.Id);
        context.Env.Transient.Tools.Should().ContainSingle();
    }

    [Fact]
    public async Task Execute_ImageOutputToggle_IsPassedToTool()
    {
        var context = CreateContext();

        var result = await new CodeActNode { EnableImageOutput = false }.ExecuteAsync(context);

        result.Success.Should().BeTrue();
        var tool = context.Env.Transient.Tools.OfType<CodeActToolV2>().Single();

        // 关闭图像输出后 AddImageAsync 必须明确失败，而不是静默丢弃。
        var call = await tool.ExecuteAsync(new ToolInvocationContext
        {
            ToolCallId = "call",
            ToolName = CodeActToolV2.ToolName,
            ArgumentsJson = """{"code":"await AddImageAsync(new byte[]{0x89,0x50,0x4E,0x47}, \"image/png\")"}""",
            RoundIndex = 0
        }, CancellationToken.None);

        call.Status.Should().Be(ToolResultStatus.Failed);
        call.ErrorCode.Should().Be(ToolErrorCodes.ImageOutputDisabled);
    }
}
