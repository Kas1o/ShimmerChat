using FluentAssertions;
using Moq;
using Newtonsoft.Json;
using ShimmerChatLib.Interface;
using ShimmerChatLib.Models;

namespace ShimmerChatLib.Tests;

public class AgentTests
{
    private readonly Mock<IKVDataService> _kvDataMock = new();

    [Fact]
    public void Create_HasUniqueGuid()
    {
        var a1 = Agent.Create("Agent1", "desc");
        var a2 = Agent.Create("Agent2", "desc");
        a1.guid.Should().NotBe(a2.guid);
    }

    [Fact]
    public void Create_SetsProperties()
    {
        var agent = Agent.Create("TestAgent", "Test description", "Hello!", new List<string> { "Hi", "Hey" });

        agent.name.Should().Be("TestAgent");
        agent.description.Should().Be("Test description");
        agent.greeting.Should().Be("Hello!");
        agent.alternativeGreetings.Should().BeEquivalentTo(["Hi", "Hey"]);
        agent.chatGuids.Should().BeEmpty();
        agent.CustomToolNames.Should().BeEmpty();
    }

    [Fact]
    public void Create_NullAlternativeGreetings_CreatesEmptyList()
    {
        var agent = Agent.Create("A", "d");
        agent.alternativeGreetings.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public void AddChatGuid_InsertsAtBeginning()
    {
        var agent = Agent.Create("A", "d");
        var g1 = Guid.NewGuid();
        var g2 = Guid.NewGuid();

        agent.AddChatGuid(g1);
        agent.AddChatGuid(g2);

        agent.chatGuids.Should().Equal(g2, g1); // newest first
    }

    [Fact]
    public void AddChatGuid_Duplicate_NotAdded()
    {
        var agent = Agent.Create("A", "d");
        var guid = Guid.NewGuid();
        agent.AddChatGuid(guid);
        agent.AddChatGuid(guid);

        agent.chatGuids.Should().HaveCount(1);
    }

    [Fact]
    public void RemoveChatGuid_RemovesCorrectly()
    {
        var agent = Agent.Create("A", "d");
        var g1 = Guid.NewGuid();
        var g2 = Guid.NewGuid();
        agent.AddChatGuid(g1);
        agent.AddChatGuid(g2);

        agent.RemoveChatGuid(g1);

        agent.chatGuids.Should().Equal(g2);
    }

    [Fact]
    public void MoveChatToTop_MovesToFront()
    {
        var agent = Agent.Create("A", "d");
        var g1 = Guid.NewGuid();
        var g2 = Guid.NewGuid();
        var g3 = Guid.NewGuid();
        agent.AddChatGuid(g1);
        agent.AddChatGuid(g2);
        agent.AddChatGuid(g3); // order: g3, g2, g1

        agent.MoveChatToTop(g2);

        agent.chatGuids.Should().Equal(g2, g3, g1);
    }

    [Fact]
    public void MoveChatToTop_NotFound_DoesNothing()
    {
        var agent = Agent.Create("A", "d");
        var g1 = Guid.NewGuid();
        agent.AddChatGuid(g1);

        agent.MoveChatToTop(Guid.NewGuid());

        agent.chatGuids.Should().Equal(g1);
    }

    [Fact]
    public void Save_StoresJson()
    {
        var agent = Agent.Create("SaveTest", "desc");
        string? writtenJson = null;
        _kvDataMock.Setup(k => k.Write("Agents", agent.guid.ToString(), It.IsAny<string>()))
            .Callback<string, string, string>((_, _, json) => writtenJson = json);

        agent.Save(_kvDataMock.Object);

        writtenJson.Should().NotBeNull();
        var deserialized = JsonConvert.DeserializeObject<Agent>(writtenJson!);
        deserialized.Should().NotBeNull();
        deserialized!.guid.Should().Be(agent.guid);
        deserialized.name.Should().Be(agent.name);
    }

    [Fact]
    public void Load_ReturnsAgent()
    {
        var agent = Agent.Create("LoadTest", "desc");
        var json = JsonConvert.SerializeObject(agent);
        _kvDataMock.Setup(k => k.Read("Agents", agent.guid.ToString())).Returns(json);

        var loaded = Agent.Load(agent.guid, _kvDataMock.Object);

        loaded.guid.Should().Be(agent.guid);
        loaded.name.Should().Be(agent.name);
    }

    [Fact]
    public void Load_NotFound_Throws()
    {
        _kvDataMock.Setup(k => k.Read("Agents", It.IsAny<string>())).Returns((string?)null);

        var act = () => Agent.Load(Guid.NewGuid(), _kvDataMock.Object);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void GetAllAgentGuids_Empty_ReturnsEmptyList()
    {
        _kvDataMock.Setup(k => k.Read("Agents", "__AllAgents__")).Returns((string?)null);

        var guids = Agent.GetAllAgentGuids(_kvDataMock.Object);

        guids.Should().BeEmpty();
    }

    [Fact]
    public void GetAllAgentGuids_HasData_ReturnsList()
    {
        var guids = new List<Guid> { Guid.NewGuid(), Guid.NewGuid() };
        _kvDataMock.Setup(k => k.Read("Agents", "__AllAgents__")).Returns(JsonConvert.SerializeObject(guids));

        var result = Agent.GetAllAgentGuids(_kvDataMock.Object);

        result.Should().BeEquivalentTo(guids);
    }

    [Fact]
    public void AddAgentToAll_AddsIfNotPresent()
    {
        var agent = Agent.Create("A", "d");
        _kvDataMock.Setup(k => k.Read("Agents", "__AllAgents__")).Returns("[]");
        string? saved = null;
        _kvDataMock.Setup(k => k.Write("Agents", "__AllAgents__", It.IsAny<string>()))
            .Callback<string, string, string>((_, _, json) => saved = json);

        Agent.AddAgentToAll(agent, _kvDataMock.Object);

        saved.Should().NotBeNull();
        var list = JsonConvert.DeserializeObject<List<Guid>>(saved!);
        list.Should().Contain(agent.guid);
    }

    [Fact]
    public void AddAgentToAll_AlreadyPresent_DoesNotDuplicate()
    {
        var agent = Agent.Create("A", "d");
        var existingList = new List<Guid> { agent.guid };
        _kvDataMock.Setup(k => k.Read("Agents", "__AllAgents__")).Returns(JsonConvert.SerializeObject(existingList));

        Agent.AddAgentToAll(agent, _kvDataMock.Object);

        _kvDataMock.Verify(k => k.Write("Agents", "__AllAgents__", It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public void RemoveAgentFromAll_RemovesCorrectly()
    {
        var agent = Agent.Create("A", "d");
        var existingList = new List<Guid> { agent.guid, Guid.NewGuid() };
        _kvDataMock.Setup(k => k.Read("Agents", "__AllAgents__")).Returns(JsonConvert.SerializeObject(existingList));
        string? saved = null;
        _kvDataMock.Setup(k => k.Write("Agents", "__AllAgents__", It.IsAny<string>()))
            .Callback<string, string, string>((_, _, json) => saved = json);

        Agent.RemoveAgentFromAll(agent, _kvDataMock.Object);

        var list = JsonConvert.DeserializeObject<List<Guid>>(saved!);
        list.Should().NotContain(agent.guid);
    }

    [Fact]
    public void Equals_SameGuid_ReturnsTrue()
    {
        var a1 = Agent.Create("A", "d");
        var a2 = Agent.Create("B", "d2");
        typeof(Agent).GetProperty(nameof(Agent.guid))!.SetValue(a2, a1.guid);

        a1.Equals(a2).Should().BeTrue();
    }

    [Fact]
    public void Equals_DifferentGuid_ReturnsFalse()
    {
        var a1 = Agent.Create("A", "d");
        var a2 = Agent.Create("A", "d");

        a1.Equals(a2).Should().BeFalse();
    }

    [Fact]
    public void GetHashCode_BasedOnGuid()
    {
        var agent = Agent.Create("A", "d");
        agent.GetHashCode().Should().Be(agent.guid.GetHashCode());
    }

    [Fact]
    public void Export_SerializesAgent()
    {
        var agent = Agent.Create("ExportTest", "desc");
        var json = agent.Export(clearChat: true);

        var importStructure = JsonConvert.DeserializeObject<AgentExportStructure>(json);
        importStructure.Should().NotBeNull();
        importStructure!.Agent!.name.Should().Be("ExportTest");
        importStructure.Agent.chatGuids.Should().BeEmpty();
    }

    [Fact]
    public void Export_ClearChat_False_PreservesChats()
    {
        var agent = Agent.Create("ExportTest", "desc");
        var chatGuid = Guid.NewGuid();
        agent.AddChatGuid(chatGuid);

        var json = agent.Export(clearChat: false);

        var importStructure = JsonConvert.DeserializeObject<AgentExportStructure>(json);
        importStructure!.Agent!.chatGuids.Should().Contain(chatGuid);
    }

    [Fact]
    public void Import_CreatesAgent()
    {
        var agent = Agent.Create("ImportTest", "desc");
        var json = agent.Export(clearChat: true);

        var imported = Agent.Import(json);

        imported.name.Should().Be("ImportTest");
        imported.description.Should().Be("desc");
        imported.guid.Should().Be(agent.guid);
    }

    [Fact]
    public void GetChatCount_ReturnsCorrect()
    {
        var agent = Agent.Create("A", "d");
        agent.GetChatCount().Should().Be(0);

        agent.AddChatGuid(Guid.NewGuid());
        agent.AddChatGuid(Guid.NewGuid());

        agent.GetChatCount().Should().Be(2);
    }

    [Fact]
    public void GetChat_ValidGuid_ReturnsChat()
    {
        var agent = Agent.Create("A", "d");
        var chat = new Chat { Name = "TestChat" };
        var chatJson = JsonConvert.SerializeObject(chat);
        agent.AddChatGuid(chat.Guid);
        _kvDataMock.Setup(k => k.Read("Chats", chat.Guid.ToString())).Returns(chatJson);

        var result = agent.GetChat(chat.Guid, _kvDataMock.Object);

        result.Name.Should().Be("TestChat");
    }

    [Fact]
    public void GetChat_InvalidGuid_Throws()
    {
        var agent = Agent.Create("A", "d");
        var act = () => agent.GetChat(Guid.NewGuid(), _kvDataMock.Object);

        act.Should().Throw<InvalidOperationException>();
    }
}
