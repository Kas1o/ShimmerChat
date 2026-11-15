using Microsoft.Extensions.Logging.Abstractions;
using ShimmerChatLib;
using ShimmerChatLib.Interface;
using ShimmerChat.Singletons;

namespace ShimmerChat.Tests;

/// <summary>
/// 统一的对话创建服务：首页快捷入口与 Agent 页面共用它，
/// 因此成功路径（含问候语）与失败路径都必须有明确行为。
/// </summary>
public class ChatCreationServiceTests
{
    private static (ChatCreationService Service, MemoryKV KV, Mock<IMessageStoreService> Store, Agent Agent) CreateSut(
        string? greeting = null,
        List<string>? alternativeGreetings = null)
    {
        var kv = new MemoryKV();
        var store = new Mock<IMessageStoreService>();
        store.Setup(s => s.GetMessage(It.IsAny<Guid>(), It.IsAny<Guid>())).Returns((Message?)null);
        store.Setup(s => s.GetMessageCount(It.IsAny<Guid>())).Returns(0);
        store.Setup(s => s.GetLastMessagePreview(It.IsAny<Guid>())).Returns((string?)null);

        var agent = Agent.Create("Test Agent", "intro", greeting, alternativeGreetings);
        agent.Save(kv);
        Agent.AddAgentToAll(agent, kv);

        var service = new ChatCreationService(kv, store.Object, NullLogger<ChatCreationService>.Instance);
        return (service, kv, store, agent);
    }

    [Fact]
    public async Task CreateChat_CreatesAndLinksTheChatWithoutGreeting()
    {
        var (service, kv, store, agent) = CreateSut();

        var result = await service.CreateChatAsync(agent.Guid);

        result.Success.Should().BeTrue();
        result.ChatGuid.Should().NotBeEmpty();

        var chat = Chat.Load(result.ChatGuid, kv);
        chat.Name.Should().NotBeNullOrWhiteSpace();

        var reloaded = Agent.Load(agent.Guid, kv);
        reloaded.ChatGuids.Should().ContainSingle().Which.Should().Be(result.ChatGuid);

        // 没有问候语时不应写入任何消息。
        store.Verify(s => s.InsertMessage(It.IsAny<Guid>(), It.IsAny<Message>()), Times.Never);
    }

    [Fact]
    public async Task CreateChat_AddsGreetingAndAlternativeGreetingsAsVersions()
    {
        Message? inserted = null;
        var (service, _, store, agent) = CreateSut("hello", ["hi", "hey"]);
        store.Setup(s => s.InsertMessage(It.IsAny<Guid>(), It.IsAny<Message>()))
            .Callback<Guid, Message>((_, message) => inserted = message);
        store.Setup(s => s.GetMessageCount(It.IsAny<Guid>())).Returns(1);
        store.Setup(s => s.GetLastMessagePreview(It.IsAny<Guid>())).Returns("hello");

        var result = await service.CreateChatAsync(agent.Guid);

        result.Success.Should().BeTrue();
        inserted.Should().NotBeNull();
        inserted!.sender.Should().Be(Sender.AI);
        inserted.CurrentVersion!.Content.Should().Be("hello");
        inserted.Versions.Should().HaveCount(3);
        inserted.Versions.Select(v => v.Content).Should().Equal("hello", "hi", "hey");
    }

    [Fact]
    public async Task CreateChat_UnknownAgent_ReturnsExplicitFailure()
    {
        var (service, _, _, _) = CreateSut();

        var result = await service.CreateChatAsync(Guid.NewGuid());

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ChatCreationErrorCodes.AgentNotFound);
    }

    [Fact]
    public async Task CreateChat_EmptyAgentGuid_ReturnsExplicitFailure()
    {
        var (service, _, _, _) = CreateSut();

        var result = await service.CreateChatAsync(Guid.Empty);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ChatCreationErrorCodes.InvalidAgent);
    }

    [Fact]
    public async Task CreateChat_StorageFailure_ReturnsExplicitFailure()
    {
        var (service, kv, _, agent) = CreateSut();
        kv.ThrowOnWrite = true;

        var result = await service.CreateChatAsync(agent.Guid);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ChatCreationErrorCodes.StorageFailure);
    }

    [Fact]
    public async Task CreateChat_RequestingCancellation_Throws()
    {
        var (service, _, _, agent) = CreateSut();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => service.CreateChatAsync(agent.Guid, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    private sealed class MemoryKV : IKVDataService
    {
        private readonly Dictionary<(string Space, string Key), string> _data = new();

        public bool ThrowOnWrite { get; set; }

        public string? Read(string spaceId, string key)
            => _data.TryGetValue((spaceId, key), out var value) ? value : null;

        public void Write(string spaceId, string key, string value)
        {
            if (ThrowOnWrite)
                throw new IOException("write failed");

            _data[(spaceId, key)] = value;
        }
    }
}
