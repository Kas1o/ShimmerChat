using Newtonsoft.Json;
using SharperLLM.Util;

namespace ShimmerChatLib.Tests;

/// <summary>
/// 请求侧的图像内容块 <see cref="ChatImage"/>：只承载 Base64，不做任何文件读写。
/// </summary>
public class ChatImageTests
{
    [Fact]
    public void GetDataUrl_UsesBase64AndMimeType()
    {
        var image = ChatImage.FromBase64("QUJD", "image/webp");

        image.GetDataUrl().Should().Be("data:image/webp;base64,QUJD");
    }

    [Fact]
    public void GetDataUrl_DefaultsToPngWhenMimeTypeMissing()
    {
        var image = ChatImage.FromBase64("QUJD", null!);

        image.GetDataUrl().Should().Be("data:image/png;base64,QUJD");
    }

    [Fact]
    public void GetDataUrl_ThrowsWhenImageCarriesNoData()
    {
        // 缺数据属于数据错误：不能悄悄生成一个空图像块交给 API
        var image = new ChatImage { MimeType = "image/png", Base64 = "" };

        var act = () => image.GetDataUrl();

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Clone_CopiesEveryFieldWithoutSharingState()
    {
        var image = ChatImage.FromBase64("QUJD", "image/png");

        var clone = (ChatImage)image.Clone();

        clone.Should().NotBeSameAs(image);
        clone.Base64.Should().Be("QUJD");
        clone.MimeType.Should().Be("image/png");
    }
}

/// <summary>
/// <see cref="ChatMessage"/> 持有多个图像内容块，并保留旧版单图字段的读写兼容。
/// </summary>
public class ChatMessageImageTests
{
    [Fact]
    public void Clone_KeepsEveryImageAsASeparateInstance()
    {
        var message = new ChatMessage
        {
            Content = "two images",
            Images = new List<ChatImage>
            {
                ChatImage.FromBase64("AAA", "image/png"),
                ChatImage.FromBase64("BBB", "image/jpeg")
            }
        };

        var clone = (ChatMessage)message.Clone();

        clone.Images.Should().HaveCount(2);
        clone.Images![0].Base64.Should().Be("AAA");
        clone.Images[1].MimeType.Should().Be("image/jpeg");
        clone.Images[0].Should().NotBeSameAs(message.Images![0]);
    }

    [Fact]
    public void LegacyImageBase64_ReadsAndWritesTheFirstImage()
    {
        var message = new ChatMessage { Content = "legacy" };

        message.ImageBase64 = "QUJD";
        message.ImageBase64.Should().Be("QUJD");
        message.Images.Should().ContainSingle();

        message.ImageBase64 = null;
        message.Images.Should().BeNull();
    }

    [Fact]
    public void ToString_ReportsImageCount()
    {
        var message = new ChatMessage
        {
            Content = "body",
            Images = new List<ChatImage> { ChatImage.FromBase64("A", "image/png"), ChatImage.FromBase64("B", "image/png") }
        };

        message.ToString().Should().Be("[2 images]body");
    }
}

/// <summary>
/// 消息持久化的图像附件 <see cref="MessageImage"/>：存储方式可变，但必须随消息一起落盘。
/// </summary>
public class MessageImageTests
{
    [Fact]
    public void Message_SerializesAndReloadsBothStorageShapes()
    {
        var message = new Message
        {
            sender = Sender.User,
            timestamp = DateTime.Now,
            message = new ChatMessage { Content = "两张图" },
            Images = new List<MessageImage>
            {
                MessageImage.FromFile("abc123.png", "image/png"),
                MessageImage.FromBase64("QUJD", "image/jpeg")
            }
        };

        var reloaded = JsonConvert.DeserializeObject<Message>(JsonConvert.SerializeObject(message));

        reloaded!.Images.Should().HaveCount(2);
        reloaded.Images![0].FileName.Should().Be("abc123.png");
        reloaded.Images[0].Base64.Should().BeNull();
        reloaded.Images[1].Base64.Should().Be("QUJD");
        reloaded.Images[1].MimeType.Should().Be("image/jpeg");
    }

    [Fact]
    public void Message_WithoutImages_StaysNull()
    {
        var message = new Message
        {
            sender = Sender.User,
            timestamp = DateTime.Now,
            message = new ChatMessage { Content = "纯文本" }
        };

        var reloaded = JsonConvert.DeserializeObject<Message>(JsonConvert.SerializeObject(message));

        reloaded!.Images.Should().BeNull();
    }
}
