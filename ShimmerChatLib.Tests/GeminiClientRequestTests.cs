using Newtonsoft.Json.Linq;
using SharperLLM.API;
using SharperLLM.FunctionCalling;
using SharperLLM.Util;

namespace ShimmerChatLib.Tests;

/// <summary>
/// Gemini 原生客户端的请求构建与响应解析：
/// 角色模型（置顶 system 合并 / 中部 system 失败或模板降级）、
/// functionResponse 形状（配对、output 包装、连续合并、图像归属）、
/// 签名回传与 dummy 垫片、恒不发送的弃用采样参数、finishReason 四桶映射。
/// </summary>
public class GeminiClientRequestTests
{
    private static GeminiChatCompletionClient Client(Action<GeminiChatCompletionClient>? configure = null)
    {
        var client = new GeminiChatCompletionClient("http://localhost/v1beta", "key", "gemini-test");
        configure?.Invoke(client);
        return client;
    }

    private static PromptBuilder Pb(params (ChatMessage, PromptBuilder.From)[] messages)
        => new() { Messages = messages };

    // ------------------------------------------------------------------
    // systemInstruction 与角色模型
    // ------------------------------------------------------------------

    [Fact]
    public void LeadingSystems_MergeIntoSystemInstruction_AndSamplingParamsNeverSent()
    {
        var client = Client();
        var pb = new PromptBuilder
        {
            System = "base prompt",
            Messages = new (ChatMessage, PromptBuilder.From)[]
            {
                (new ChatMessage { Content = "extra system" }, PromptBuilder.From.system),
                (new ChatMessage { Content = "hello" }, PromptBuilder.From.user)
            }
        };

        var body = client.BuildRequestBody(pb);

        var systemParts = (JArray)body["systemInstruction"]!["parts"]!;
        systemParts.Count.Should().Be(2);
        systemParts[0]!["text"]!.ToString().Should().Be("base prompt");
        systemParts[1]!["text"]!.ToString().Should().Be("extra system");

        var contents = (JArray)body["contents"]!;
        contents.Count.Should().Be(1);
        body["generationConfig"]!["maxOutputTokens"]!.Value<int>().Should().Be(16384);

        // temperature / top_p / top_k 已被 Gemini 4 与 3.6+ 弃用，一律不发送
        var serialized = body.ToString(Newtonsoft.Json.Formatting.None);
        serialized.Should().NotContain("temperature");
        serialized.Should().NotContain("top_p");
        serialized.Should().NotContain("top_k");
    }

    [Fact]
    public void SystemOnlyHistory_FailsExplicitly()
    {
        var client = Client();
        var pb = Pb((new ChatMessage { Content = "only system" }, PromptBuilder.From.system));

        var act = () => client.BuildRequestBody(pb);

        act.Should().Throw<InvalidOperationException>().WithMessage("*no contents*");
    }

    [Fact]
    public void MidSystem_WithoutTemplate_Fails()
    {
        var client = Client();
        var pb = Pb(
            (new ChatMessage { Content = "sys" }, PromptBuilder.From.system),
            (new ChatMessage { Content = "hi" }, PromptBuilder.From.user),
            (new ChatMessage { Content = "mid sys" }, PromptBuilder.From.system));

        var act = () => client.BuildRequestBody(pb);

        act.Should().Throw<InvalidOperationException>().WithMessage("*#2*");
    }

    [Fact]
    public void MidSystem_WithTemplate_MapsToUserTurnAndLogs()
    {
        var logs = new List<string>();
        var client = Client(c =>
        {
            c.MidSystemTemplate = "[System note: {0}]";
            c.Log = logs.Add;
        });
        var pb = Pb(
            (new ChatMessage { Content = "hi" }, PromptBuilder.From.user),
            (new ChatMessage { Content = "mid sys" }, PromptBuilder.From.system));

        var body = client.BuildRequestBody(pb);

        var contents = (JArray)body["contents"]!;
        contents[1]!["role"]!.ToString().Should().Be("user");
        contents[1]!["parts"]![0]!["text"]!.ToString().Should().Be("[System note: mid sys]");
        logs.Should().ContainSingle();
    }

    [Fact]
    public void MidSystem_TemplateWithoutPlaceholder_Fails()
    {
        var client = Client(c => c.MidSystemTemplate = "template without placeholder");
        var pb = Pb(
            (new ChatMessage { Content = "hi" }, PromptBuilder.From.user),
            (new ChatMessage { Content = "mid sys" }, PromptBuilder.From.system));

        var act = () => client.BuildRequestBody(pb);

        act.Should().Throw<InvalidOperationException>().WithMessage("*{0}*");
    }

    // ------------------------------------------------------------------
    // thinking 与自定义请求体
    // ------------------------------------------------------------------

    [Fact]
    public void ThinkingControls_AllDefault_NoThinkingConfigSent()
    {
        // 档位默认 + 摘要关闭：thinkingConfig 整体不发送
        var client = Client(c => c.IncludeThoughts = false);
        var pb = Pb((new ChatMessage { Content = "hi" }, PromptBuilder.From.user));

        var body = client.BuildRequestBody(pb);

        body["generationConfig"]!["thinkingConfig"].Should().BeNull();
    }

    [Fact]
    public void IncludeThoughts_DefaultOn_EmitsThinkingConfigWithoutLevel()
    {
        // 默认客户端只带 includeThoughts，不带档位键（档位仍由用户显式选择）
        var client = Client();
        var pb = Pb((new ChatMessage { Content = "hi" }, PromptBuilder.From.user));

        var body = client.BuildRequestBody(pb);

        var thinkingConfig = body["generationConfig"]!["thinkingConfig"]!;
        thinkingConfig["includeThoughts"]!.Value<bool>().Should().BeTrue();
        thinkingConfig["thinkingLevel"].Should().BeNull();
    }

    [Fact]
    public void IncludeThoughts_False_WithLevel_OnlyLevelSent()
    {
        // 两个开关互不联动：关闭摘要不影响档位
        var client = Client(c =>
        {
            c.ThinkingLevel = "high";
            c.IncludeThoughts = false;
        });
        var pb = Pb((new ChatMessage { Content = "hi" }, PromptBuilder.From.user));

        var body = client.BuildRequestBody(pb);

        var thinkingConfig = body["generationConfig"]!["thinkingConfig"]!;
        thinkingConfig["thinkingLevel"]!.ToString().Should().Be("high");
        thinkingConfig["includeThoughts"].Should().BeNull();
    }

    [Fact]
    public void ThinkingLevel_IsSentLiterally_WithoutModelProbing()
    {
        var client = Client(c => c.ThinkingLevel = "high");
        var pb = Pb((new ChatMessage { Content = "hi" }, PromptBuilder.From.user));

        var body = client.BuildRequestBody(pb);

        var thinkingConfig = body["generationConfig"]!["thinkingConfig"]!;
        thinkingConfig["thinkingLevel"]!.ToString().Should().Be("high");
        // 摘要开关与档位互不联动：显式选择的键才出现
        thinkingConfig["includeThoughts"]!.Value<bool>().Should().BeTrue();
    }

    [Fact]
    public void CustomRequestProperties_ReplaceRootKeysWholesale()
    {
        // 浅合并：用户写了 generationConfig 就要写全，客户端不做深合并魔法
        var client = Client(c => c.CustomRequestProperties = new Dictionary<string, object>
        {
            ["generationConfig"] = new { thinkingConfig = new { thinkingBudget = 0 } }
        });
        var pb = Pb((new ChatMessage { Content = "hi" }, PromptBuilder.From.user));

        var body = client.BuildRequestBody(pb);

        body["generationConfig"]!["thinkingConfig"]!["thinkingBudget"]!.Value<int>().Should().Be(0);
        body["generationConfig"]!["maxOutputTokens"].Should().BeNull();
    }

    // ------------------------------------------------------------------
    // assistant 回传与思维链签名
    // ------------------------------------------------------------------

    [Fact]
    public void AssistantToolCalls_EchoRealSignature_FromProviderData()
    {
        var client = Client();
        var assistant = new ChatMessage
        {
            Content = "calling",
            toolCalls = new List<ToolCall>
            {
                new() { name = "t1", id = "c1", arguments = "{}", index = 0 },
                new() { name = "t2", id = "c2", arguments = "{}", index = 1 }
            },
            ProviderData = new Dictionary<string, string>
            {
                [GeminiChatCompletionClient.ThoughtSignatureKey] = "REAL_SIG"
            }
        };

        var body = client.BuildRequestBody(Pb((assistant, PromptBuilder.From.assistant)));

        var parts = (JArray)body["contents"]![0]!["parts"]!;
        parts[0]!["text"]!.ToString().Should().Be("calling");
        // 官方规则：并行调用只有首个 functionCall part 携带签名
        parts[1]!["functionCall"]!["name"]!.ToString().Should().Be("t1");
        parts[1]!["thoughtSignature"]!.ToString().Should().Be("REAL_SIG");
        parts[2]!["functionCall"]!["name"]!.ToString().Should().Be("t2");
        parts[2]!["thoughtSignature"].Should().BeNull();
    }

    [Fact]
    public void AssistantToolCalls_WithoutSignature_PadsDummyAndLogs()
    {
        var logs = new List<string>();
        var client = Client(c => c.Log = logs.Add);
        var assistant = new ChatMessage
        {
            Content = "",
            toolCalls = new List<ToolCall> { new() { name = "t1", id = "c1", arguments = "{}", index = 0 } }
        };

        var body = client.BuildRequestBody(Pb((assistant, PromptBuilder.From.assistant)));

        var parts = (JArray)body["contents"]![0]!["parts"]!;
        parts[0]!["thoughtSignature"]!.ToString().Should().Be(GeminiChatCompletionClient.SkipSignatureValue);
        logs.Should().ContainSingle();
    }

    [Fact]
    public void AssistantToolCalls_InvalidArgumentsJson_Fails()
    {
        var client = Client();
        var assistant = new ChatMessage
        {
            Content = "",
            toolCalls = new List<ToolCall> { new() { name = "t1", id = "c1", arguments = "not json", index = 0 } }
        };

        var act = () => client.BuildRequestBody(Pb((assistant, PromptBuilder.From.assistant)));

        act.Should().Throw<InvalidDataException>().WithMessage("*t1*");
    }

    [Fact]
    public void Assistant_ThinkingIsNeverEchoed_AndImagesFail()
    {
        var client = Client();
        var assistant = new ChatMessage
        {
            Content = "answer",
            thinking = "secret reasoning",
            Images = new List<ChatImage> { ChatImage.FromBase64("AAA", "image/png") }
        };

        var act = () => client.BuildRequestBody(Pb((assistant, PromptBuilder.From.assistant)));

        act.Should().Throw<InvalidOperationException>().WithMessage("*images*");
    }

    // ------------------------------------------------------------------
    // functionResponse
    // ------------------------------------------------------------------

    [Fact]
    public void ToolResult_PlainTextWrappedAsOutput_PairedById()
    {
        var client = Client();
        var pb = Pb(
            (new ChatMessage { Content = "run the tool" }, PromptBuilder.From.user),
            (new ChatMessage
            {
                Content = "",
                toolCalls = new List<ToolCall>
                {
                    new() { name = "get_weather", id = "call_0", arguments = "{\"city\":\"Paris\"}", index = 0 }
                }
            }, PromptBuilder.From.assistant),
            (new ChatMessage { Content = "sunny", id = "call_0" }, PromptBuilder.From.tool_result));

        var body = client.BuildRequestBody(pb);

        var contents = (JArray)body["contents"]!;
        contents.Count.Should().Be(3);

        var modelParts = (JArray)contents[1]!["parts"]!;
        modelParts[0]!["functionCall"]!["args"]!["city"]!.ToString().Should().Be("Paris");

        contents[2]!["role"]!.ToString().Should().Be("user");
        var fr = contents[2]!["parts"]![0]!["functionResponse"]!;
        fr["name"]!.ToString().Should().Be("get_weather");
        fr["response"]!["output"]!.ToString().Should().Be("sunny");
    }

    [Fact]
    public void ToolResult_ObjectContent_PassesThrough()
    {
        // 悬空占位注入的历史本来就是 JSON 对象：原样透传，模型能读懂 no_result 语义
        var client = Client();
        var assistant = new ChatMessage
        {
            Content = "",
            toolCalls = new List<ToolCall> { new() { name = "t", id = "c1", arguments = "{}", index = 0 } }
        };
        var placeholder = "{\"status\":\"no_result\",\"tool\":\"t\",\"tool_call_id\":\"c1\"}";
        var pb = Pb(
            (assistant, PromptBuilder.From.assistant),
            (new ChatMessage { Content = placeholder, id = "c1" }, PromptBuilder.From.tool_result));

        var body = client.BuildRequestBody(pb);

        var response = body["contents"]![1]!["parts"]![0]!["functionResponse"]!["response"]!;
        response["status"]!.ToString().Should().Be("no_result");
        response["output"].Should().BeNull();
    }

    [Fact]
    public void ConsecutiveToolResults_MergeIntoSingleUserContent_WithPositionalPairing()
    {
        var client = Client();
        var assistant = new ChatMessage
        {
            Content = "",
            toolCalls = new List<ToolCall>
            {
                new() { name = "t1", id = "c1", arguments = "{}", index = 0 },
                new() { name = "t2", id = "c2", arguments = "{}", index = 1 }
            }
        };
        var pb = Pb(
            (new ChatMessage { Content = "go" }, PromptBuilder.From.user),
            (assistant, PromptBuilder.From.assistant),
            (new ChatMessage { Content = "r1" }, PromptBuilder.From.tool_result),
            (new ChatMessage { Content = "r2" }, PromptBuilder.From.tool_result));

        var body = client.BuildRequestBody(pb);

        var contents = (JArray)body["contents"]!;
        contents.Count.Should().Be(3);
        var frContent = contents[2]!;
        frContent["role"]!.ToString().Should().Be("user");
        var frParts = (JArray)frContent["parts"]!;
        frParts.Count.Should().Be(2);
        frParts[0]!["functionResponse"]!["name"]!.ToString().Should().Be("t1");
        frParts[1]!["functionResponse"]!["name"]!.ToString().Should().Be("t2");
    }

    [Fact]
    public void ToolResult_Images_GoIntoFunctionResponseParts()
    {
        var client = Client();
        var assistant = new ChatMessage
        {
            Content = "",
            toolCalls = new List<ToolCall> { new() { name = "shot", id = "c1", arguments = "{}", index = 0 } }
        };
        var result = new ChatMessage
        {
            Content = "screenshot taken",
            id = "c1",
            Images = new List<ChatImage> { ChatImage.FromBase64("AAA", "image/png") }
        };

        var body = client.BuildRequestBody(Pb(
            (assistant, PromptBuilder.From.assistant),
            (result, PromptBuilder.From.tool_result)));

        var fr = body["contents"]![1]!["parts"]![0]!["functionResponse"]!;
        fr["response"]!["output"]!.ToString().Should().Be("screenshot taken");
        var media = (JArray)fr["parts"]!;
        media.Count.Should().Be(1);
        media[0]!["inline_data"]!["mime_type"]!.ToString().Should().Be("image/png");
        media[0]!["inline_data"]!["data"]!.ToString().Should().Be("AAA");
    }

    [Fact]
    public void ToolResult_DanglingWithoutAssistant_Fails()
    {
        var client = Client();
        var pb = Pb((new ChatMessage { Content = "orphan", id = "c1" }, PromptBuilder.From.tool_result));

        var act = () => client.BuildRequestBody(pb);

        act.Should().Throw<InvalidDataException>().WithMessage("*#0*");
    }

    [Fact]
    public void ToolResult_IdNotDeclaredByAssistant_Fails()
    {
        var client = Client();
        var assistant = new ChatMessage
        {
            Content = "",
            toolCalls = new List<ToolCall> { new() { name = "t", id = "call_0", arguments = "{}", index = 0 } }
        };

        var act = () => client.BuildRequestBody(Pb(
            (assistant, PromptBuilder.From.assistant),
            (new ChatMessage { Content = "r", id = "call_9" }, PromptBuilder.From.tool_result)));

        act.Should().Throw<InvalidDataException>().WithMessage("*call_9*");
    }

    // ------------------------------------------------------------------
    // 工具声明
    // ------------------------------------------------------------------

    [Fact]
    public void ToolDeclarations_UseOpenApiStyleSchema()
    {
        var tool = new Tool
        {
            name = "search",
            description = "run a search",
            parameters = new List<(ToolParameter, bool)>
            {
                (new ToolParameter { type = ParameterType.String, description = "query text", name = "query", @enum = new List<string> { "a", "b" } }, true),
                (new ToolParameter { type = ParameterType.Number, description = "limit", name = "limit" }, false)
            }
        };

        var declarations = GeminiChatCompletionClient.BuildToolDeclarations(new[] { tool });

        declarations.Count.Should().Be(1);
        var declaration = declarations[0]!;
        declaration["name"]!.ToString().Should().Be("search");
        declaration["description"]!.ToString().Should().Be("run a search");
        declaration["parameters"]!["type"]!.ToString().Should().Be("object");
        declaration["parameters"]!["properties"]!["query"]!["type"]!.ToString().Should().Be("string");
        declaration["parameters"]!["properties"]!["query"]!["enum"]!.Count().Should().Be(2);
        declaration["parameters"]!["properties"]!["limit"]!["type"]!.ToString().Should().Be("number");
        ((JArray)declaration["parameters"]!["required"]!).Count.Should().Be(1);
    }

    // ------------------------------------------------------------------
    // 响应解析
    // ------------------------------------------------------------------

    [Fact]
    public void Parse_FunctionCallWithStop_SynthesizesFunctionCallFinish()
    {
        // Gemini 用 STOP 表达工具调用轮；客户端必须合成 FunctionCall 供循环辨认
        var data = JObject.Parse("""
        {
          "candidates": [{
            "content": {"role": "model", "parts": [
              {"functionCall": {"name": "get_weather", "args": {"city": "Paris"}}, "thoughtSignature": "SIG_A"}
            ]},
            "finishReason": "STOP", "index": 0
          }],
          "usageMetadata": {"promptTokenCount": 10}
        }
        """);

        var chunk = GeminiChatCompletionClient.ParseResponseChunk(data, null);

        chunk.Should().NotBeNull();
        chunk!.FinishReason.Should().Be(FinishReason.FunctionCall);
        chunk.Body.Content.Should().Be("");
        chunk.Body.toolCalls.Should().ContainSingle();
        chunk.Body.toolCalls![0].name.Should().Be("get_weather");
        chunk.Body.toolCalls[0].arguments.Should().Be("{\"city\":\"Paris\"}");
        chunk.Body.ProviderData![GeminiChatCompletionClient.ThoughtSignatureKey].Should().Be("SIG_A");
    }

    [Fact]
    public void Parse_ThoughtParts_SplitIntoThinkingAndContent()
    {
        var data = JObject.Parse("""
        {
          "candidates": [{
            "content": {"role": "model", "parts": [
              {"text": "internal deliberation", "thought": true},
              {"text": "Final answer"}
            ]},
            "finishReason": "STOP", "index": 0
          }]
        }
        """);

        var chunk = GeminiChatCompletionClient.ParseResponseChunk(data, null);

        chunk!.Body.thinking.Should().Be("internal deliberation");
        chunk.Body.Content.Should().Be("Final answer");
        chunk.FinishReason.Should().Be(FinishReason.Stop);
    }

    [Fact]
    public void Parse_EmptyTextSignaturePart_CapturesSignature()
    {
        // 流式时签名可能藏在一个空文本 part 里，必须读满整流才能拿全
        var data = JObject.Parse("""
        {
          "candidates": [{
            "content": {"role": "model", "parts": [
              {"text": "", "thoughtSignature": "SIG_LATE"}
            ]},
            "finishReason": "STOP", "index": 0
          }]
        }
        """);

        var chunk = GeminiChatCompletionClient.ParseResponseChunk(data, null);

        chunk!.Body.ProviderData![GeminiChatCompletionClient.ThoughtSignatureKey].Should().Be("SIG_LATE");
        chunk.FinishReason.Should().Be(FinishReason.Stop);
    }

    [Fact]
    public void Parse_PromptBlocked_ReturnsContentFilterWithRawReason()
    {
        var logs = new List<string>();
        var data = JObject.Parse("""{"promptFeedback": {"blockReason": "SAFETY"}}""");

        var chunk = GeminiChatCompletionClient.ParseResponseChunk(data, logs.Add);

        chunk.Should().NotBeNull();
        chunk!.FinishReason.Should().Be(FinishReason.ContentFilter);
        chunk.Body.Content.Should().Be("");
        chunk.Body.ProviderData![GeminiChatCompletionClient.FinishReasonKey].Should().Be("SAFETY");
        logs.Should().ContainSingle();
    }

    [Fact]
    public void Parse_UsageOnlyChunk_IsSkipped()
    {
        var data = JObject.Parse("""{"usageMetadata": {"promptTokenCount": 42}}""");

        GeminiChatCompletionClient.ParseResponseChunk(data, null).Should().BeNull();
    }

    // ------------------------------------------------------------------
    // finishReason 四桶映射
    // ------------------------------------------------------------------

    [Fact]
    public void MapFinishReason_NormalBuckets()
    {
        GeminiChatCompletionClient.MapFinishReason("STOP", false).Should().Be(FinishReason.Stop);
        GeminiChatCompletionClient.MapFinishReason("MAX_TOKENS", false).Should().Be(FinishReason.Length);
        GeminiChatCompletionClient.MapFinishReason("CONTINUATION", false).Should().Be(FinishReason.Length);
        // Gemini 用 STOP 表达 FC 轮：有工具调用时一律合成 FunctionCall
        GeminiChatCompletionClient.MapFinishReason("STOP", true).Should().Be(FinishReason.FunctionCall);
        // 安全族 → ContentFilter，原始值由调用方写入 ProviderData
        GeminiChatCompletionClient.MapFinishReason("SAFETY", false).Should().Be(FinishReason.ContentFilter);
        GeminiChatCompletionClient.MapFinishReason("RECITATION", false).Should().Be(FinishReason.ContentFilter);
        GeminiChatCompletionClient.MapFinishReason("PROHIBITED_CONTENT", false).Should().Be(FinishReason.ContentFilter);
        // 委托桶：交给宿主现成的 ToolNotFound / MaxCallsPerRound 降级机器
        GeminiChatCompletionClient.MapFinishReason("UNEXPECTED_TOOL_CALL", false).Should().Be(FinishReason.FunctionCall);
        GeminiChatCompletionClient.MapFinishReason("TOO_MANY_TOOL_CALLS", false).Should().Be(FinishReason.FunctionCall);
        // 流式中间块
        GeminiChatCompletionClient.MapFinishReason(null, false).Should().Be(FinishReason.None);
        GeminiChatCompletionClient.MapFinishReason("", false).Should().Be(FinishReason.None);
    }

    [Fact]
    public void MapFinishReason_ContentFilter_LeavesTrace()
    {
        var logs = new List<string>();

        GeminiChatCompletionClient.MapFinishReason("SPII", false, logs.Add).Should().Be(FinishReason.ContentFilter);

        logs.Should().ContainSingle().Which.Should().Contain("SPII");
    }

    [Fact]
    public void MapFinishReason_ProtocolFailures_ThrowExplicitly()
    {
        var malformed = () => GeminiChatCompletionClient.MapFinishReason("MALFORMED_FUNCTION_CALL", false);
        malformed.Should().Throw<InvalidDataException>().WithMessage("*invalid function call*");

        var missingSig = () => GeminiChatCompletionClient.MapFinishReason("MISSING_THOUGHT_SIGNATURE", false);
        missingSig.Should().Throw<InvalidDataException>().WithMessage("*client defect*");

        var account = () => GeminiChatCompletionClient.MapFinishReason("PUP_LIMITED_DISABLED", false);
        account.Should().Throw<InvalidDataException>().WithMessage("*PUP_LIMITED_DISABLED*");

        var unknown = () => GeminiChatCompletionClient.MapFinishReason("SOME_FUTURE_VALUE", false);
        unknown.Should().Throw<InvalidDataException>().WithMessage("*SOME_FUTURE_VALUE*");
    }
}
