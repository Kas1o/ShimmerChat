using FluentAssertions;
using Moq;
using Newtonsoft.Json;
using ShimmerChatLib.Interface;

namespace ShimmerChatLib.Tests;

public class ChatTests
{
    private readonly Mock<IKVDataService> _kvDataMock = new();
    private readonly Mock<IMessageStoreService> _msgStoreMock = new();

    [Fact]
    public void NewChat_HasUniqueGuid()
    {
        var c1 = new Chat { Name = "Chat1" };
        var c2 = new Chat { Name = "Chat2" };
        c1.Guid.Should().NotBe(c2.Guid);
    }

    [Fact]
    public void NewChat_MessagesInitialized()
    {
        var chat = new Chat { Name = "Test" };
        chat.Messages.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public void Save_StoresJson()
    {
        var chat = new Chat { Name = "SaveTest" };
        string? writtenJson = null;
        _kvDataMock.Setup(k => k.Write("Chats", chat.Guid.ToString(), It.IsAny<string>()))
            .Callback<string, string, string>((_, _, json) => writtenJson = json);

        chat.Save(_kvDataMock.Object);

        writtenJson.Should().NotBeNull();
        var deserialized = JsonConvert.DeserializeObject<Chat>(writtenJson!);
        deserialized!.Name.Should().Be("SaveTest");
    }

    [Fact]
    public void Load_ReturnsChat()
    {
        var chat = new Chat { Name = "LoadTest" };
        var json = JsonConvert.SerializeObject(chat);
        _kvDataMock.Setup(k => k.Read("Chats", chat.Guid.ToString())).Returns(json);

        var loaded = Chat.Load(chat.Guid, _kvDataMock.Object);

        loaded.Guid.Should().Be(chat.Guid);
        loaded.Name.Should().Be("LoadTest");
        loaded.Messages.Should().NotBeNull();
    }

    [Fact]
    public void Load_NotFound_Throws()
    {
        _kvDataMock.Setup(k => k.Read("Chats", It.IsAny<string>())).Returns((string?)null);

        var act = () => Chat.Load(Guid.NewGuid(), _kvDataMock.Object);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void AddMessage_AddsToCollection()
    {
        var chat = new Chat { Name = "Test" };
        var msg = new Message { sender = Sender.User, timestamp = DateTime.UtcNow };
        msg.message = new SharperLLM.Util.ChatMessage { Content = "hello" };

        chat.AddMessage(msg);

        chat.Messages.Should().ContainSingle().Which.Should().Be(msg);
    }

    [Fact]
    public void LoadAllMessages_LoadsFromStore()
    {
        var chat = new Chat { Name = "Test" };
        var messages = new List<Message>
        {
            new() { sender = Sender.User, timestamp = DateTime.UtcNow, Id = Guid.NewGuid() }
        };
        _msgStoreMock.Setup(m => m.GetMessageCount(chat.Guid)).Returns(1);
        _msgStoreMock.Setup(m => m.GetMessages(chat.Guid, 0, 1)).Returns(messages);
        _msgStoreMock.Setup(m => m.GetLastMessagePreview(chat.Guid)).Returns("hello");

        chat.LoadAllMessages(_msgStoreMock.Object);

        chat.Messages.Should().HaveCount(1);
        chat.MessageCount.Should().Be(1);
        chat.LastMessagePreview.Should().Be("hello");
    }

    [Fact]
    public void LoadAllMessages_ReplacesExisting()
    {
        var chat = new Chat { Name = "Test" };
        var oldMsg = new Message { sender = Sender.User, timestamp = DateTime.UtcNow };
        chat.AddMessage(oldMsg);

        var newMessages = new List<Message>
        {
            new() { sender = Sender.AI, timestamp = DateTime.UtcNow, Id = Guid.NewGuid() }
        };
        _msgStoreMock.Setup(m => m.GetMessageCount(chat.Guid)).Returns(1);
        _msgStoreMock.Setup(m => m.GetMessages(chat.Guid, 0, 1)).Returns(newMessages);
        _msgStoreMock.Setup(m => m.GetLastMessagePreview(chat.Guid)).Returns((string?)null);

        chat.LoadAllMessages(_msgStoreMock.Object);

        chat.Messages.Should().HaveCount(1);
        chat.Messages[0].sender.Should().Be(Sender.AI);
    }

    [Fact]
    public void SaveMessage_NewMessage_Inserts()
    {
        var chat = new Chat { Name = "Test" };
        var msg = new Message { sender = Sender.User, timestamp = DateTime.UtcNow, Id = Guid.NewGuid() };
        _msgStoreMock.Setup(m => m.GetMessage(chat.Guid, msg.Id)).Returns((Message?)null);
        _msgStoreMock.Setup(m => m.GetMessageCount(chat.Guid)).Returns(1);
        _msgStoreMock.Setup(m => m.GetLastMessagePreview(chat.Guid)).Returns("preview");

        chat.SaveMessage(_msgStoreMock.Object, msg);

        _msgStoreMock.Verify(m => m.InsertMessage(chat.Guid, msg), Times.Once);
        _msgStoreMock.Verify(m => m.UpdateMessage(chat.Guid, msg), Times.Never);
        chat.MessageCount.Should().Be(1);
    }

    [Fact]
    public void SaveMessage_ExistingMessage_Updates()
    {
        var chat = new Chat { Name = "Test" };
        var msg = new Message { sender = Sender.User, timestamp = DateTime.UtcNow, Id = Guid.NewGuid() };
        _msgStoreMock.Setup(m => m.GetMessage(chat.Guid, msg.Id)).Returns(msg);
        _msgStoreMock.Setup(m => m.GetMessageCount(chat.Guid)).Returns(1);
        _msgStoreMock.Setup(m => m.GetLastMessagePreview(chat.Guid)).Returns("preview");

        chat.SaveMessage(_msgStoreMock.Object, msg);

        _msgStoreMock.Verify(m => m.UpdateMessage(chat.Guid, msg), Times.Once);
        _msgStoreMock.Verify(m => m.InsertMessage(chat.Guid, msg), Times.Never);
    }

    [Fact]
    public void DeleteMessage_RemovesAndDeletes()
    {
        var chat = new Chat { Name = "Test" };
        var msg = new Message { sender = Sender.User, timestamp = DateTime.UtcNow };
        chat.AddMessage(msg);
        _msgStoreMock.Setup(m => m.GetMessageCount(chat.Guid)).Returns(0);
        _msgStoreMock.Setup(m => m.GetLastMessagePreview(chat.Guid)).Returns((string?)null);

        chat.DeleteMessage(_msgStoreMock.Object, msg);

        _msgStoreMock.Verify(m => m.DeleteMessage(chat.Guid, msg), Times.Once);
        chat.Messages.Should().BeEmpty();
        chat.MessageCount.Should().Be(0);
    }

    [Fact]
    public void SaveAllMessages_SavesAll()
    {
        var chat = new Chat { Name = "Test" };
        var m1 = new Message { sender = Sender.User, timestamp = DateTime.UtcNow };
        var m2 = new Message { sender = Sender.AI, timestamp = DateTime.UtcNow };
        chat.AddMessage(m1);
        chat.AddMessage(m2);

        chat.SaveAllMessages(_msgStoreMock.Object);

        _msgStoreMock.Verify(m => m.DeleteAllMessages(chat.Guid), Times.Once);
        _msgStoreMock.Verify(m => m.InsertMessage(chat.Guid, m1), Times.Once);
        _msgStoreMock.Verify(m => m.InsertMessage(chat.Guid, m2), Times.Once);
        chat.MessageCount.Should().Be(2);
    }

    [Fact]
    public void LoadAndMigrate_LegacyData_MigratesToMessageStore()
    {
        var chat = new Chat { Name = "LegacyChat" };
        var legacyMsg = new Message
        {
            Id = Guid.Empty,
            sender = Sender.User,
            timestamp = DateTime.UtcNow,
        };
        legacyMsg.message = new SharperLLM.Util.ChatMessage { Content = "legacy content" };

        var legacyChatData = new
        {
            Name = "LegacyChat",
            Guid = chat.Guid,
            Messages = new List<Message> { legacyMsg },
            CreateTime = DateTime.UtcNow,
            LastModifyTime = DateTime.UtcNow,
            LastMessagePreview = "",
            MessageCount = 0
        };
        var jsonWithMessages = JsonConvert.SerializeObject(legacyChatData);

        _kvDataMock.Setup(k => k.Read("Chats", chat.Guid.ToString())).Returns(jsonWithMessages);
        _msgStoreMock.Setup(m => m.GetMessageCount(chat.Guid)).Returns(0);
        _msgStoreMock.Setup(m => m.GetLastMessagePreview(chat.Guid)).Returns("preview");

        var result = Chat.LoadAndMigrate(chat.Guid, _kvDataMock.Object, _msgStoreMock.Object);

        result.Guid.Should().Be(chat.Guid);
        _msgStoreMock.Verify(m => m.InsertMessage(chat.Guid, It.Is<Message>(x => x.Id != Guid.Empty)), Times.Once);
    }

    [Fact]
    public void LoadAndMigrate_AlreadyMigrated_Skips()
    {
        var chat = new Chat { Name = "ExistingChat" };
        var json = JsonConvert.SerializeObject(chat);
        _kvDataMock.Setup(k => k.Read("Chats", chat.Guid.ToString())).Returns(json);
        _msgStoreMock.Setup(m => m.GetMessageCount(chat.Guid)).Returns(5);
        _msgStoreMock.Setup(m => m.GetLastMessagePreview(chat.Guid)).Returns("preview");

        var result = Chat.LoadAndMigrate(chat.Guid, _kvDataMock.Object, _msgStoreMock.Object);

        result.MessageCount.Should().Be(5);
        _msgStoreMock.Verify(m => m.InsertMessage(It.IsAny<Guid>(), It.IsAny<Message>()), Times.Never);
    }

    [Fact]
    public void CreateTime_DefaultsToMinValue()
    {
        var chat = new Chat { Name = "Test" };
        chat.CreateTime.Should().Be(default);
    }

    [Fact]
    public void LastMessagePreview_DefaultsEmpty()
    {
        var chat = new Chat { Name = "Test" };
        chat.LastMessagePreview.Should().BeEmpty();
    }

    [Fact]
    public void MessageCount_DefaultsZero()
    {
        var chat = new Chat { Name = "Test" };
        chat.MessageCount.Should().Be(0);
    }
}
