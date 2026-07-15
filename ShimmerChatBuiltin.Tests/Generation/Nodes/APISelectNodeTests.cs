using Newtonsoft.Json;
using SharperLLM.FunctionCalling;
using ShimmerChatBuiltin;
using ShimmerChatLib;

namespace ShimmerChatBuiltin.Tests.Generation.Nodes;

public class APISelectNodeTests : NodeTestBase
{
    private static string CreateApiSettingsJson()
    {
        var configs = new List<ApiConfig>
        {
            new() { Name = "OpenAI", Type = ApiConfigType.OpenAI, OpenAIUrl = "http://localhost", OpenAIApiKey = "k", OpenAIModel = "gpt" },
            new() { Name = "DeepSeek", Type = ApiConfigType.DeepSeek, DeepSeekUrl = "http://localhost", DeepSeekApiKey = "k", DeepSeekModel = "ds" },
        };
        return JsonConvert.SerializeObject(configs);
    }

    [Fact]
    public async Task NoSettings_ReturnsFailure()
    {
        KvMock.Setup(k => k.Read("ApiSettings", "apiSetting")).Returns((string?)null);
        var node = new APISelectNode();
        var result = await node.ExecuteAsync(CreateContext());
        result.Success.Should().BeFalse();
        result.Code.Should().Be(NodeErrorCodes.ApiUnavailable);
    }

    [Fact]
    public async Task APIIndex_Negative1_UsesGlobalSelected()
    {
        KvMock.Setup(k => k.Read("ApiSettings", "apiSetting")).Returns(CreateApiSettingsJson());
        KvMock.Setup(k => k.Read("ApiSettings", "selectedAPIIndex")).Returns("1");
        var node = new APISelectNode { APIIndex = -1 };
        var ctx = CreateContext();

        var result = await node.ExecuteAsync(ctx);

        result.Success.Should().BeTrue();
        ctx.Env.Transient.API.Should().NotBeNull();
        ctx.Env.Transient.API!.SupportsToolCalling.Should().BeTrue();
    }

    [Fact]
    public async Task APIIndex_Valid_UsesSpecific()
    {
        KvMock.Setup(k => k.Read("ApiSettings", "apiSetting")).Returns(CreateApiSettingsJson());
        KvMock.Setup(k => k.Read("ApiSettings", "selectedAPIIndex")).Returns("0");
        var node = new APISelectNode { APIIndex = 1 };
        var ctx = CreateContext();

        var result = await node.ExecuteAsync(ctx);

        result.Success.Should().BeTrue();
        ctx.Env.Transient.API.Should().NotBeNull();
    }

    [Fact]
    public async Task APIIndex_OutOfRange_FallsBackToZero()
    {
        KvMock.Setup(k => k.Read("ApiSettings", "apiSetting")).Returns(CreateApiSettingsJson());
        var node = new APISelectNode { APIIndex = 99 };
        var ctx = CreateContext();

        var result = await node.ExecuteAsync(ctx);

        result.Success.Should().BeTrue();
        ctx.Env.Transient.API.Should().NotBeNull();
    }

    [Fact]
    public async Task IsContinuation_OpenAI_SetsPrefix()
    {
        KvMock.Setup(k => k.Read("ApiSettings", "apiSetting")).Returns(CreateApiSettingsJson());
        KvMock.Setup(k => k.Read("ApiSettings", "selectedAPIIndex")).Returns("0");
        var node = new APISelectNode { APIIndex = -1 };
        var env = new PreGenerationEnv(CreatePersistentEnv());
        var msg = new Message
        {
            sender = Sender.AI,
            timestamp = DateTime.UtcNow,
            message = new SharperLLM.Util.ChatMessage { Content = "prefix text" }
        };
        env.Transient.SharedState["IsContinuation"] = true;
        env.Transient.SharedState["ChatMessages"] = new List<Message> { msg };
        var ctx = CreateContext(env);

        var result = await node.ExecuteAsync(ctx);

        result.Success.Should().BeTrue();
        msg.message.CustomProperties.Should().ContainKey("prefix");
        msg.message.CustomProperties!["prefix"].Should().Be(true);
    }

    [Fact]
    public async Task IsContinuation_NonOpenAI_Fails()
    {
        var configs = new List<ApiConfig>
        {
            new() { Name = "Pseudo", Type = ApiConfigType.Pseudo },
        };
        KvMock.Setup(k => k.Read("ApiSettings", "apiSetting"))
            .Returns(JsonConvert.SerializeObject(configs));
        KvMock.Setup(k => k.Read("ApiSettings", "selectedAPIIndex")).Returns("0");
        var node = new APISelectNode { APIIndex = -1 };
        var env = new PreGenerationEnv(CreatePersistentEnv());
        env.Transient.SharedState["IsContinuation"] = true;
        var ctx = CreateContext(env);

        var result = await node.ExecuteAsync(ctx);

        result.Success.Should().BeFalse();
        result.Code.Should().Be(NodeErrorCodes.ApiUnavailable);
    }
}
