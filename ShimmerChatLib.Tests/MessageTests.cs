using FluentAssertions;
using Newtonsoft.Json;
using SharperLLM.Util;

namespace ShimmerChatLib.Tests;

public class MessageTests
{
    private static Message CreateTestMessage(string sender = Sender.AI, string content = "test content")
    {
        var msg = new Message
        {
            sender = sender,
            timestamp = DateTime.UtcNow,
        };
        msg.message = new ChatMessage { Content = content };
        return msg;
    }

    [Fact]
    public void NewMessage_HasUniqueGuid()
    {
        var msg1 = new Message { sender = Sender.AI, timestamp = DateTime.UtcNow };
        var msg2 = new Message { sender = Sender.AI, timestamp = DateTime.UtcNow };
        msg1.Id.Should().NotBe(msg2.Id);
    }

    [Fact]
    public void NewMessage_HasCompletedState()
    {
        var msg = CreateTestMessage();
        msg.GenerationState.Should().Be(MessageGenerationState.Completed);
    }

    [Fact]
    public void Message_SetFirstTime_CreatesVersion()
    {
        var msg = new Message { sender = Sender.AI, timestamp = DateTime.UtcNow };
        msg.message = new ChatMessage { Content = "hello" };

        msg.Versions.Should().HaveCount(1);
        msg.CurrentVersionIndex.Should().Be(0);
        msg.CurrentVersion!.Content.Should().Be("hello");
    }

    [Fact]
    public void Message_SetSecondTime_ReplacesCurrentVersion()
    {
        var msg = CreateTestMessage(content: "first");
        msg.message = new ChatMessage { Content = "second" };

        msg.Versions.Should().HaveCount(1);
        msg.CurrentVersion!.Content.Should().Be("second");
    }

    [Fact]
    public void Message_ContentChanged_TriggersEvent()
    {
        var msg = CreateTestMessage(content: "initial");
        using var monitor = msg.Monitor();

        msg.message = new ChatMessage { Content = "changed" };

        monitor.Should().Raise(nameof(Message.ContentChanged));
    }

    [Fact]
    public void Message_ContentSame_NoEvent()
    {
        var msg = CreateTestMessage(content: "same");
        using var monitor = msg.Monitor();

        msg.message = new ChatMessage { Content = "same" };

        monitor.Should().NotRaise(nameof(Message.ContentChanged));
    }

    [Fact]
    public void AddVersion_CreatesNewVersionAndSwitches()
    {
        var msg = CreateTestMessage(content: "v1");
        msg.AddVersion(new ChatMessage { Content = "v2" });

        msg.Versions.Should().HaveCount(2);
        msg.CurrentVersionIndex.Should().Be(1);
        msg.CurrentVersion!.Content.Should().Be("v2");
    }

    [Fact]
    public void AddVersion_TriggersVersionChanged()
    {
        var msg = CreateTestMessage(content: "v1");
        using var monitor = msg.Monitor();

        msg.AddVersion(new ChatMessage { Content = "v2" });

        monitor.Should().Raise(nameof(Message.VersionChanged));
    }

    [Fact]
    public void SwitchToVersion_ValidIndex_SwitchesCorrectly()
    {
        var msg = CreateTestMessage(content: "v1");
        msg.AddVersion(new ChatMessage { Content = "v2" });
        msg.AddVersion(new ChatMessage { Content = "v3" });

        msg.SwitchToVersion(0);

        msg.CurrentVersionIndex.Should().Be(0);
        msg.CurrentVersion!.Content.Should().Be("v1");
    }

    [Fact]
    public void SwitchToVersion_TriggersEvent()
    {
        var msg = CreateTestMessage(content: "v1");
        msg.AddVersion(new ChatMessage { Content = "v2" });
        using var monitor = msg.Monitor();

        msg.SwitchToVersion(0);

        monitor.Should().Raise(nameof(Message.VersionChanged));
    }

    [Fact]
    public void SwitchToVersion_InvalidIndex_NoChange()
    {
        var msg = CreateTestMessage(content: "v1");

        msg.SwitchToVersion(5);
        msg.CurrentVersionIndex.Should().Be(0);

        msg.SwitchToVersion(-1);
        msg.CurrentVersionIndex.Should().Be(0);
    }

    [Fact]
    public void SwitchToVersion_OutOfRange_DoesNotTriggerEvent()
    {
        var msg = CreateTestMessage(content: "v1");
        using var monitor = msg.Monitor();

        msg.SwitchToVersion(999);

        monitor.Should().NotRaise(nameof(Message.VersionChanged));
    }

    [Fact]
    public void RemoveVersion_RemovesCorrectly()
    {
        var msg = CreateTestMessage(content: "v1");
        msg.AddVersion(new ChatMessage { Content = "v2" });
        msg.AddVersion(new ChatMessage { Content = "v3" });
        msg.SwitchToVersion(1);

        msg.RemoveVersion(2);

        msg.Versions.Should().HaveCount(2);
        msg.CurrentVersionIndex.Should().Be(1);
        msg.CurrentVersion!.Content.Should().Be("v2");
        msg.Versions[0].Content.Should().Be("v1");
        msg.Versions[1].Content.Should().Be("v2");
    }

    [Fact]
    public void RemoveVersion_OnlyOneVersion_DoesNothing()
    {
        var msg = CreateTestMessage(content: "only");
        msg.RemoveVersion(0);

        msg.Versions.Should().HaveCount(1);
        msg.HasMultipleVersions.Should().BeFalse();
    }

    [Fact]
    public void RemoveVersion_CurrentVersion_AdjustsIndex()
    {
        var msg = CreateTestMessage(content: "v1");
        msg.AddVersion(new ChatMessage { Content = "v2" });
        msg.AddVersion(new ChatMessage { Content = "v3" });
        msg.SwitchToVersion(1);

        msg.RemoveVersion(1);

        msg.CurrentVersionIndex.Should().Be(0);
        msg.CurrentVersion!.Content.Should().Be("v1");
    }

    [Fact]
    public void HasMultipleVersions_True_WhenMoreThanOne()
    {
        var msg = CreateTestMessage(content: "v1");
        msg.HasMultipleVersions.Should().BeFalse();

        msg.AddVersion(new ChatMessage { Content = "v2" });
        msg.HasMultipleVersions.Should().BeTrue();
    }

    [Fact]
    public void GenerationState_Transition_TriggersEvent()
    {
        var msg = CreateTestMessage();
        using var monitor = msg.Monitor();

        msg.GenerationState = MessageGenerationState.Generating;

        monitor.Should().Raise(nameof(Message.GenerationStateChanged));
    }

    [Fact]
    public void GenerationState_AlwaysFiresEvent()
    {
        var msg = CreateTestMessage();
        using var monitor = msg.Monitor();

        msg.GenerationState = MessageGenerationState.Generating;

        monitor.Should().Raise(nameof(Message.GenerationStateChanged));
    }

    [Fact]
    public void IsStreaming_True_WhenGenerating()
    {
        var msg = CreateTestMessage();

        msg.GenerationState = MessageGenerationState.Generating;
        msg.IsStreaming.Should().BeTrue();
        msg.IsGenerating.Should().BeTrue();

        msg.GenerationState = MessageGenerationState.Regenerating;
        msg.IsStreaming.Should().BeTrue();
        msg.IsGenerating.Should().BeTrue();
    }

    [Fact]
    public void IsStreaming_False_WhenCompleted()
    {
        var msg = CreateTestMessage();
        msg.GenerationState = MessageGenerationState.Completed;

        msg.IsStreaming.Should().BeFalse();
        msg.IsGenerating.Should().BeFalse();
    }

    [Fact]
    public void IsStreaming_Set_True_WasCompleted()
    {
        var msg = CreateTestMessage();
        msg.IsStreaming = true;

        msg.GenerationState.Should().Be(MessageGenerationState.Generating);
    }

    [Fact]
    public void IsStreaming_Set_True_WasRegenerating_StaysRegenerating()
    {
        var msg = CreateTestMessage();
        msg.GenerationState = MessageGenerationState.Regenerating;
        msg.IsStreaming = true;

        msg.GenerationState.Should().Be(MessageGenerationState.Regenerating);
    }

    [Fact]
    public void IsStreaming_Set_False_ReturnsToCompleted()
    {
        var msg = CreateTestMessage();
        msg.GenerationState = MessageGenerationState.Generating;
        msg.IsStreaming = false;

        msg.GenerationState.Should().Be(MessageGenerationState.Completed);
    }

    [Fact]
    public void IsRegenerating_OnlyTrue_WhenRegenerating()
    {
        var msg = CreateTestMessage();
        msg.IsRegenerating.Should().BeFalse();

        msg.GenerationState = MessageGenerationState.Generating;
        msg.IsRegenerating.Should().BeFalse();

        msg.GenerationState = MessageGenerationState.Regenerating;
        msg.IsRegenerating.Should().BeTrue();
    }

    [Fact]
    public void StreamingStateChanged_BackwardCompat_Works()
    {
        var msg = CreateTestMessage();
        var eventFired = false;
        msg.StreamingStateChanged += (_, _) => eventFired = true;

        msg.GenerationState = MessageGenerationState.Generating;

        eventFired.Should().BeTrue();
    }

    [Fact]
    public void JsonSerialization_Roundtrip_PreservesData()
    {
        var msg = CreateTestMessage(content: "hello world");
        msg.AddVersion(new ChatMessage { Content = "v2" });
        msg.SwitchToVersion(0);
        msg.GenerationState = MessageGenerationState.Generating;
        msg.timestamp = new DateTime(2025, 6, 4, 12, 0, 0, DateTimeKind.Utc);

        var json = JsonConvert.SerializeObject(msg);
        var deserialized = JsonConvert.DeserializeObject<Message>(json)!;

        deserialized.Id.Should().Be(msg.Id);
        deserialized.sender.Should().Be(msg.sender);
        deserialized.timestamp.Should().Be(msg.timestamp);
        deserialized.CurrentVersion!.Content.Should().Be("hello world");
        deserialized.Versions.Should().HaveCount(2);
        deserialized.CurrentVersionIndex.Should().Be(0);
    }

    [Fact]
    public void IsContinuation_DefaultFalse()
    {
        var msg = CreateTestMessage();
        msg.IsContinuation.Should().BeFalse();
    }

    [Fact]
    public void CurrentVersion_EmptyMessage_ReturnsNull()
    {
        var msg = new Message { sender = Sender.AI, timestamp = DateTime.UtcNow };
        msg.CurrentVersion.Should().BeNull();
    }
}
