using FluentAssertions;
using Moq;
using Newtonsoft.Json;
using ShimmerChatLib.Interface;
using ShimmerChatLib.Models;
using ShimmerChatLib.Storage;
using System.Text;

namespace ShimmerChatLib.Tests;

public class AgentTests
{
    private readonly Mock<IKVDataService> _kvDataMock = new();

    [Fact]
    public void Create_HasUniqueGuid()
    {
        var a1 = Agent.Create("Agent1", "desc");
        var a2 = Agent.Create("Agent2", "desc");
        a1.Guid.Should().NotBe(a2.Guid);
    }

    [Fact]
    public void Create_SetsProperties()
    {
        var agent = Agent.Create("TestAgent", "Test description", "Hello!", new List<string> { "Hi", "Hey" });

        agent.Name.Should().Be("TestAgent");
        agent.UserIntro.Should().Be("Test description");
        agent.Greeting.Should().Be("Hello!");
        agent.AlternativeGreetings.Should().BeEquivalentTo(["Hi", "Hey"]);
        agent.ChatGuids.Should().BeEmpty();
    }

    [Fact]
    public void Create_NullAlternativeGreetings_CreatesEmptyList()
    {
        var agent = Agent.Create("A", "d");
        agent.AlternativeGreetings.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public void AddChatGuid_InsertsAtBeginning()
    {
        var agent = Agent.Create("A", "d");
        var g1 = Guid.NewGuid();
        var g2 = Guid.NewGuid();

        agent.AddChatGuid(g1);
        agent.AddChatGuid(g2);

        agent.ChatGuids.Should().Equal(g2, g1); // newest first
    }

    [Fact]
    public void AddChatGuid_Duplicate_NotAdded()
    {
        var agent = Agent.Create("A", "d");
        var guid = Guid.NewGuid();
        agent.AddChatGuid(guid);
        agent.AddChatGuid(guid);

        agent.ChatGuids.Should().HaveCount(1);
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

        agent.ChatGuids.Should().Equal(g2);
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

        agent.ChatGuids.Should().Equal(g2, g3, g1);
    }

    [Fact]
    public void MoveChatToTop_NotFound_DoesNothing()
    {
        var agent = Agent.Create("A", "d");
        var g1 = Guid.NewGuid();
        agent.AddChatGuid(g1);

        agent.MoveChatToTop(Guid.NewGuid());

        agent.ChatGuids.Should().Equal(g1);
    }

    [Fact]
    public void Save_StoresJson()
    {
        var agent = Agent.Create("SaveTest", "desc");
        string? writtenJson = null;
        _kvDataMock.Setup(k => k.Write("Agents", agent.Guid.ToString(), It.IsAny<string>()))
            .Callback<string, string, string>((_, _, json) => writtenJson = json);

        agent.Save(_kvDataMock.Object);

        writtenJson.Should().NotBeNull();
        var deserialized = JsonConvert.DeserializeObject<Agent>(writtenJson!);
        deserialized.Should().NotBeNull();
        deserialized!.Guid.Should().Be(agent.Guid);
        deserialized.Name.Should().Be(agent.Name);
    }

    [Fact]
    public void Load_ReturnsAgent()
    {
        var agent = Agent.Create("LoadTest", "desc");
        var json = JsonConvert.SerializeObject(agent);
        _kvDataMock.Setup(k => k.Read("Agents", agent.Guid.ToString())).Returns(json);

        var loaded = Agent.Load(agent.Guid, _kvDataMock.Object);

        loaded.Guid.Should().Be(agent.Guid);
        loaded.Name.Should().Be(agent.Name);
    }

    [Fact]
    public void Load_NotFound_Throws()
    {
        _kvDataMock.Setup(k => k.Read("Agents", It.IsAny<string>())).Returns((string?)null);

        var act = () => Agent.Load(Guid.NewGuid(), _kvDataMock.Object);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Deserialize_LegacyImageGuids_MigratesToPngPaths()
    {
        var avatarGuid = Guid.NewGuid();
        var backgroundGuid = Guid.NewGuid();
        var json = JsonConvert.SerializeObject(new
        {
            Guid = Guid.NewGuid(),
            Name = "Legacy",
            AvatarGuid = avatarGuid,
            BackgroundGuid = backgroundGuid
        });

        var agent = JsonConvert.DeserializeObject<Agent>(json)!;

        agent.AvatarPath.Should().Be($"{avatarGuid}.png");
        agent.BackgroundPath.Should().Be($"{backgroundGuid}.png");
    }

    [Fact]
    public void Deserialize_NewPath_IsKeptAndTakesPrecedenceOverLegacyGuid()
    {
        var json = JsonConvert.SerializeObject(new
        {
            Guid = Guid.NewGuid(),
            Name = "New",
            AvatarPath = "avatars/new.png",
            AvatarGuid = Guid.NewGuid()
        });

        var agent = JsonConvert.DeserializeObject<Agent>(json)!;

        agent.AvatarPath.Should().Be("avatars/new.png");
    }

    [Fact]
    public void Serialize_WritesPathAndOmitsLegacyGuidFields()
    {
        var agent = Agent.Create("PathSave", "desc");
        agent.AvatarPath = "avatars/avatar.png";
        agent.BackgroundPath = "backgrounds/bg.png";

        var json = JsonConvert.SerializeObject(agent);

        json.Should().Contain("\"AvatarPath\":\"avatars/avatar.png\"");
        json.Should().Contain("\"BackgroundPath\":\"backgrounds/bg.png\"");
        json.Should().NotContain("AvatarGuid");
        json.Should().NotContain("BackgroundGuid");
    }

    [Fact]
    public void Export_ReadsImageFromPathAndEmbedsBase64()
    {
        var root = Path.Combine(Path.GetTempPath(), "shimmer-agent-export", Guid.NewGuid().ToString("N"));
        var previous = DataPaths.Current;
        try
        {
            DataPaths.Configure(new DataPaths(root));
            var imageBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
            Directory.CreateDirectory(Path.Combine(root, "UserUploadImage", "avatars"));
            File.WriteAllBytes(Path.Combine(root, "UserUploadImage", "avatars", "avatar.png"), imageBytes);

            var agent = Agent.Create("ExportImage", "desc");
            agent.AvatarPath = "avatars/avatar.png";

            var structure = JsonConvert.DeserializeObject<AgentExportStructure>(agent.Export())!;

            structure.AvatarBase64.Should().Be(Convert.ToBase64String(imageBytes));
            structure.Agent!.AvatarPath.Should().Be("avatars/avatar.png");
        }
        finally
        {
            DataPaths.Configure(previous);
        }
    }

    [Fact]
    public void Export_LegacyImageGuid_IsMigratedAndNotWrittenBack()
    {
        var avatarGuid = Guid.NewGuid();
        var json = JsonConvert.SerializeObject(new
        {
            Guid = Guid.NewGuid(),
            Name = "LegacyExport",
            AvatarGuid = avatarGuid
        });
        var agent = JsonConvert.DeserializeObject<Agent>(json)!;

        var export = agent.Export();

        export.Should().Contain($"\"AvatarPath\":\"{avatarGuid}.png\"");
        export.Should().NotContain("AvatarGuid");
    }

    [Fact]
    public void Import_InvalidAvatarBase64_Throws()
    {
        var json = JsonConvert.SerializeObject(new AgentExportStructure(
            Agent.Create("ImportBadBase64", "desc"),
            AvatarBase64: "not-base64",
            BackgroundBase64: null));

        var act = () => Agent.Import(json);

        act.Should().Throw<InvalidDataException>().WithMessage("*avatar*Base64*");
    }

    [Fact]
    public void Import_UnsupportedAvatarBytes_Throws()
    {
        var json = JsonConvert.SerializeObject(new AgentExportStructure(
            Agent.Create("ImportBadImage", "desc"),
            AvatarBase64: Convert.ToBase64String(Encoding.UTF8.GetBytes("hello")),
            BackgroundBase64: null));

        var act = () => Agent.Import(json);

        act.Should().Throw<InvalidDataException>().WithMessage("*avatar*not a supported image*");
    }

    [Fact]
    public void Import_ValidImage_WritesNewFileAndSetsRelativePath()
    {
        var root = Path.Combine(Path.GetTempPath(), "shimmer-agent-import", Guid.NewGuid().ToString("N"));
        var previous = DataPaths.Current;
        try
        {
            DataPaths.Configure(new DataPaths(root));
            var imageBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
            var json = JsonConvert.SerializeObject(new AgentExportStructure(
                Agent.Create("ImportImage", "desc"),
                AvatarBase64: Convert.ToBase64String(imageBytes),
                BackgroundBase64: null));

            var imported = Agent.Import(json);

            imported.AvatarPath.Should().NotBeNull();
            imported.AvatarPath!.Should().EndWith(".png");
            File.Exists(Path.Combine(root, "UserUploadImage", imported.AvatarPath!)).Should().BeTrue();
        }
        finally
        {
            DataPaths.Configure(previous);
        }
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
        list.Should().Contain(agent.Guid);
    }

    [Fact]
    public void AddAgentToAll_AlreadyPresent_DoesNotDuplicate()
    {
        var agent = Agent.Create("A", "d");
        var existingList = new List<Guid> { agent.Guid };
        _kvDataMock.Setup(k => k.Read("Agents", "__AllAgents__")).Returns(JsonConvert.SerializeObject(existingList));

        Agent.AddAgentToAll(agent, _kvDataMock.Object);

        _kvDataMock.Verify(k => k.Write("Agents", "__AllAgents__", It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public void RemoveAgentFromAll_RemovesCorrectly()
    {
        var agent = Agent.Create("A", "d");
        var existingList = new List<Guid> { agent.Guid, Guid.NewGuid() };
        _kvDataMock.Setup(k => k.Read("Agents", "__AllAgents__")).Returns(JsonConvert.SerializeObject(existingList));
        string? saved = null;
        _kvDataMock.Setup(k => k.Write("Agents", "__AllAgents__", It.IsAny<string>()))
            .Callback<string, string, string>((_, _, json) => saved = json);

        Agent.RemoveAgentFromAll(agent, _kvDataMock.Object);

        var list = JsonConvert.DeserializeObject<List<Guid>>(saved!);
        list.Should().NotContain(agent.Guid);
    }

    [Fact]
    public void Equals_SameGuid_ReturnsTrue()
    {
        var a1 = Agent.Create("A", "d");
        var a2 = Agent.Create("B", "d2");
        typeof(Agent).GetProperty(nameof(Agent.Guid))!.SetValue(a2, a1.Guid);

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
        agent.GetHashCode().Should().Be(agent.Guid.GetHashCode());
    }

    [Fact]
    public void Export_SerializesAgent()
    {
        var agent = Agent.Create("ExportTest", "desc");
        var json = agent.Export(clearChat: true);

        var importStructure = JsonConvert.DeserializeObject<AgentExportStructure>(json);
        importStructure.Should().NotBeNull();
        importStructure!.Agent!.Name.Should().Be("ExportTest");
        importStructure.Agent.ChatGuids.Should().BeEmpty();
    }

    [Fact]
    public void Export_ClearChat_False_PreservesChats()
    {
        var agent = Agent.Create("ExportTest", "desc");
        var chatGuid = Guid.NewGuid();
        agent.AddChatGuid(chatGuid);

        var json = agent.Export(clearChat: false);

        var importStructure = JsonConvert.DeserializeObject<AgentExportStructure>(json);
        importStructure!.Agent!.ChatGuids.Should().Contain(chatGuid);
    }

    [Fact]
    public void Import_CreatesAgent()
    {
        var agent = Agent.Create("ImportTest", "intro");
        var json = agent.Export(clearChat: true);

        var imported = Agent.Import(json);

        imported.Name.Should().Be("ImportTest");
        imported.UserIntro.Should().Be("intro");
        imported.Guid.Should().Be(agent.Guid);
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
