using LiteDB;
using ShimmerChat.Singletons;
using ShimmerChatLib;
using ShimmerChatLib.Interface;

namespace ShimmerChat.Tests;

/// <summary>
/// 消息顺序契约：store 按 <see cref="Message.Order"/> 排序，
/// 只有当会话里存在未分配顺序号（Order &lt; 0）的老数据时才回退到 timestamp。
/// 两种实现必须是同一套行为。
/// </summary>
public class FileMessageStoreOrderTests : MessageStoreOrderTestBase
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ShimmerChatTests", Guid.NewGuid().ToString("N"));

    protected override IMessageStoreService CreateStore() => new FileMessageStoreService(_root);

    public override void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, true);
    }
}

public class LiteDBMessageStoreOrderTests : MessageStoreOrderTestBase
{
    private readonly LiteDatabase _database = new(":memory:");

    protected override IMessageStoreService CreateStore() => new LiteDBMessageStoreService(_database);

    public override void Dispose() => _database.Dispose();
}

public abstract class MessageStoreOrderTestBase : IDisposable
{
    protected abstract IMessageStoreService CreateStore();

    public abstract void Dispose();

    private static Message NewMessage(string content, long order, DateTime timestamp) => new()
    {
        sender = Sender.User,
        timestamp = timestamp,
        Order = order,
        message = new SharperLLM.Util.ChatMessage { Content = content }
    };

    private static readonly DateTime BaseTime = new(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void GetMessages_SortsByOrder_NotTimestamp()
    {
        var store = CreateStore();
        var chatGuid = Guid.NewGuid();

        // 顺序号与时间戳完全相反：按 Order 排序的结果应等于插入顺序
        var first = NewMessage("first", 0, BaseTime.AddMinutes(10));
        var second = NewMessage("second", 1, BaseTime.AddMinutes(5));
        var third = NewMessage("third", 2, BaseTime);

        store.InsertMessage(chatGuid, first);
        store.InsertMessage(chatGuid, second);
        store.InsertMessage(chatGuid, third);

        var loaded = store.GetMessages(chatGuid, 0, 10);

        loaded.Select(m => m.message.Content).Should().Equal("first", "second", "third");
        loaded.Select(m => m.Order).Should().Equal(0, 1, 2);
    }

    [Fact]
    public void GetMessages_FallsBackToTimestamp_WhenAnyOrderUnassigned()
    {
        var store = CreateStore();
        var chatGuid = Guid.NewGuid();

        store.InsertMessage(chatGuid, NewMessage("early", -1, BaseTime));
        store.InsertMessage(chatGuid, NewMessage("late", -1, BaseTime.AddMinutes(1)));

        store.GetMessages(chatGuid, 0, 10)
            .Select(m => m.message.Content)
            .Should().Equal("early", "late");
    }

    [Fact]
    public void GetMessages_MixedState_FallsBackToTimestamp()
    {
        var store = CreateStore();
        var chatGuid = Guid.NewGuid();

        // 混合状态一律走回退，避免"一半有序一半乱"
        store.InsertMessage(chatGuid, NewMessage("assigned", 0, BaseTime.AddMinutes(5)));
        store.InsertMessage(chatGuid, NewMessage("unassigned", -1, BaseTime));

        store.GetMessages(chatGuid, 0, 10)
            .Select(m => m.message.Content)
            .Should().Equal("unassigned", "assigned");
    }

    [Fact]
    public void UpdateMessage_SyncsOrderAndTimestamp()
    {
        var store = CreateStore();
        var chatGuid = Guid.NewGuid();

        var a = NewMessage("a", 0, BaseTime);
        var b = NewMessage("b", 1, BaseTime.AddMinutes(1));
        store.InsertMessage(chatGuid, a);
        store.InsertMessage(chatGuid, b);

        // 重排：把 b 挪到前面，并显式改时间戳（两种实现必须表现一致）
        b.Order = 0;
        b.timestamp = BaseTime.AddMinutes(-5);
        a.Order = 1;
        a.timestamp = BaseTime.AddMinutes(30);
        store.UpdateMessage(chatGuid, b);
        store.UpdateMessage(chatGuid, a);

        store.GetMessages(chatGuid, 0, 10)
            .Select(m => m.message.Content)
            .Should().Equal("b", "a");
    }

    [Fact]
    public void GetLastMessagePreview_UsesOrder()
    {
        var store = CreateStore();
        var chatGuid = Guid.NewGuid();

        store.InsertMessage(chatGuid, NewMessage("old-by-order", 0, BaseTime.AddHours(1)));
        store.InsertMessage(chatGuid, NewMessage("last-by-order", 1, BaseTime));

        store.GetLastMessagePreview(chatGuid).Should().Be("last-by-order");
    }

    [Fact]
    public void InsertAfter_ThenRewrite_PreservesPosition()
    {
        var store = CreateStore();
        var chatGuid = Guid.NewGuid();

        store.InsertMessage(chatGuid, NewMessage("m1", 0, BaseTime));
        store.InsertMessage(chatGuid, NewMessage("m3", 2, BaseTime.AddMinutes(2)));

        // 模拟 Chat.InsertMessage：插入后整体重编号并全量回写
        store.DeleteAllMessages(chatGuid);
        store.InsertMessage(chatGuid, NewMessage("m1", 0, BaseTime));
        store.InsertMessage(chatGuid, NewMessage("m2", 1, BaseTime.AddSeconds(30)));
        store.InsertMessage(chatGuid, NewMessage("m3", 2, BaseTime.AddMinutes(2)));

        store.GetMessages(chatGuid, 0, 10)
            .Select(m => m.message.Content)
            .Should().Equal("m1", "m2", "m3");
    }
}
