using Newtonsoft.Json.Linq;
using SharperLLM.Util;
using ShimmerChatBuiltin.Fragments;

namespace ShimmerChatBuiltin.Tests.Generation.Nodes;

public class AdvancedFragmentNodeTests : NodeTestBase
{
    [Fact]
    public async Task MultimodalPayload_AcceptsDataUrlObjectAndRawBase64()
    {
        var node = new AdvancedFragmentNode
        {
            From = PromptBuilder.From.user,
            Content = "look",
            ImagesJson = """
                [
                  "data:image/webp;base64,AAA",
                  { "mimeType": "image/jpeg", "base64": "BBB" },
                  "CCC"
                ]
                """
        };
        var ctx = CreateContext();

        var result = await node.ExecuteAsync(ctx);

        result.Success.Should().BeTrue();
        var segment = ctx.Env.Transient.Fragments.Should().ContainSingle().Subject;
        segment.From.Should().Be(PromptBuilder.From.user);
        segment.SourceType.Should().Be(typeof(AdvancedFragmentNode));
        segment.Message.Content.Should().Be("look");
        segment.Message.Images.Should().HaveCount(3);
        segment.Message.Images![0].MimeType.Should().Be("image/webp");
        segment.Message.Images[0].Base64.Should().Be("AAA");
        segment.Message.Images[1].MimeType.Should().Be("image/jpeg");
        segment.Message.Images[1].Base64.Should().Be("BBB");
        segment.Message.Images[2].MimeType.Should().Be(ChatImage.DefaultMimeType);
        segment.Message.Images[2].Base64.Should().Be("CCC");
    }

    [Fact]
    public async Task ToolResultPayload_CarriesImagesAndToolCallId()
    {
        var node = new AdvancedFragmentNode
        {
            From = PromptBuilder.From.tool_result,
            Content = "tool output",
            ToolCallId = "call_1",
            ImagesJson = """["AAA"]"""
        };
        var ctx = CreateContext();

        var result = await node.ExecuteAsync(ctx);

        result.Success.Should().BeTrue();
        var message = ctx.Env.Transient.Fragments.Single().Message;
        message.id.Should().Be("call_1");
        message.Content.Should().Be("tool output");
        message.Images.Should().ContainSingle().Which.Base64.Should().Be("AAA");
    }

    [Fact]
    public async Task AssistantPayload_ParsesToolCallsAndObjectArguments()
    {
        var node = new AdvancedFragmentNode
        {
            From = PromptBuilder.From.assistant,
            Content = "",
            ToolCallsJson = """
                [
                  { "id": "call_1", "name": "ask_user", "arguments": { "question": "hi" } },
                  { "id": "call_2", "name": "noop", "arguments": "{}", "index": 5 }
                ]
                """
        };
        var ctx = CreateContext();

        var result = await node.ExecuteAsync(ctx);

        result.Success.Should().BeTrue();
        var calls = ctx.Env.Transient.Fragments.Single().Message.toolCalls;
        calls.Should().HaveCount(2);
        calls![0].id.Should().Be("call_1");
        calls[0].name.Should().Be("ask_user");
        calls[0].arguments.Should().Be("""{"question":"hi"}""");
        calls[0].index.Should().Be(0);
        calls[1].index.Should().Be(5);
    }

    [Fact]
    public async Task ThinkingAndCustomProperties_AreAppliedToTheMessage()
    {
        var node = new AdvancedFragmentNode
        {
            From = PromptBuilder.From.assistant,
            Content = "answer",
            Thinking = "reasoning",
            CustomPropertiesJson = """{ "custom_flag": true, "nested": { "n": 1 } }"""
        };
        var ctx = CreateContext();

        var result = await node.ExecuteAsync(ctx);

        result.Success.Should().BeTrue();
        var message = ctx.Env.Transient.Fragments.Single().Message;
        message.thinking.Should().Be("reasoning");
        message.CustomProperties.Should().NotBeNull();
        message.CustomProperties!["custom_flag"].Should().BeOfType<JValue>()
            .Which.Value<bool>().Should().BeTrue();
        message.CustomProperties["nested"].Should().BeOfType<JObject>();
    }

    [Fact]
    public async Task EmptyThinking_IsNotEchoed()
    {
        var node = new AdvancedFragmentNode { Content = "plain", Thinking = "" };
        var ctx = CreateContext();

        await node.ExecuteAsync(ctx);

        ctx.Env.Transient.Fragments.Single().Message.thinking.Should().BeNull();
    }

    [Fact]
    public async Task ToolResultWithoutToolCallId_FailsWithoutAddingAFragment()
    {
        var node = new AdvancedFragmentNode
        {
            From = PromptBuilder.From.tool_result,
            Content = "orphan result"
        };
        var ctx = CreateContext();

        var result = await node.ExecuteAsync(ctx);

        result.Success.Should().BeFalse();
        result.Code.Should().Be(NodeErrorCodes.ParameterError);
        ctx.Env.Transient.Fragments.Should().BeEmpty();
    }

    [Fact]
    public async Task InvalidImagesJson_FailsWithParseError()
    {
        var node = new AdvancedFragmentNode { Content = "x", ImagesJson = "{ not an array" };
        var ctx = CreateContext();

        var result = await node.ExecuteAsync(ctx);

        result.Success.Should().BeFalse();
        result.Code.Should().Be(NodeErrorCodes.ParseError);
        ctx.Env.Transient.Fragments.Should().BeEmpty();
    }

    [Fact]
    public async Task ImageWithoutBase64_FailsWithDataMissing()
    {
        var node = new AdvancedFragmentNode
        {
            Content = "x",
            ImagesJson = """[{ "mimeType": "image/png" }]"""
        };
        var ctx = CreateContext();

        var result = await node.ExecuteAsync(ctx);

        result.Success.Should().BeFalse();
        result.Code.Should().Be(NodeErrorCodes.DataMissing);
        ctx.Env.Transient.Fragments.Should().BeEmpty();
    }

    [Fact]
    public async Task FileBackedImage_IsResolvedThroughTheAttachmentService()
    {
        var node = new AdvancedFragmentNode
        {
            From = PromptBuilder.From.user,
            Content = "look",
            ImagesJson = """[{ "mimeType": "image/webp", "fileName": "abc.png" }]"""
        };
        var ctx = CreateContext();

        var result = await node.ExecuteAsync(ctx);

        result.Success.Should().BeTrue();
        var image = ctx.Env.Transient.Fragments.Single().Message.Images.Should()
            .ContainSingle().Subject;
        image.MimeType.Should().Be("image/webp");
        image.Base64.Should().Be("resolved");
        ImageAttachmentsMock.Verify(s => s.ToChatImageAsync(
            It.Is<MessageImage>(i => i.FileName == "abc.png" && i.MimeType == "image/webp"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task FileBackedImage_WithoutAttachmentService_FailsWithServiceError()
    {
        var node = new AdvancedFragmentNode
        {
            Content = "look",
            ImagesJson = """[{ "mimeType": "image/png", "fileName": "abc.png" }]"""
        };
        var env = new PreGenerationEnv(new PersistentEnv
        {
            KVData = KvMock.Object,
            ToolRegistry = ToolRegistryMock.Object,
            Serializer = SerializerMock.Object,
            LocService = LocMock.Object,
            DebugOutput = DebugOutputMock.Object,
            Chat = new Chat { Name = "TestChat" },
            Agent = Agent.Create("TestAgent", "")
        });

        var result = await node.ExecuteAsync(new PreNodeExecutionContext(env));

        result.Success.Should().BeFalse();
        result.Code.Should().Be(NodeErrorCodes.ServiceError);
        env.Transient.Fragments.Should().BeEmpty();
    }

    [Fact]
    public async Task UnresolvableFileBackedImage_FailsWithDataMissing()
    {
        ImageAttachmentsMock
            .Setup(s => s.ToChatImageAsync(It.IsAny<MessageImage>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new FileNotFoundException("missing"));

        var node = new AdvancedFragmentNode
        {
            Content = "look",
            ImagesJson = """[{ "mimeType": "image/png", "fileName": "missing.png" }]"""
        };
        var ctx = CreateContext();

        var result = await node.ExecuteAsync(ctx);

        result.Success.Should().BeFalse();
        result.Code.Should().Be(NodeErrorCodes.DataMissing);
        ctx.Env.Transient.Fragments.Should().BeEmpty();
    }

    [Fact]
    public async Task ToolCallWithoutId_FailsWithParameterError()
    {
        var node = new AdvancedFragmentNode
        {
            From = PromptBuilder.From.assistant,
            ToolCallsJson = """[{ "name": "ask_user" }]"""
        };
        var ctx = CreateContext();

        var result = await node.ExecuteAsync(ctx);

        result.Success.Should().BeFalse();
        result.Code.Should().Be(NodeErrorCodes.ParameterError);
        ctx.Env.Transient.Fragments.Should().BeEmpty();
    }
}
