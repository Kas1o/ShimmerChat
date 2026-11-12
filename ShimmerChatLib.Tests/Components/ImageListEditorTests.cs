namespace ShimmerChatLib.Tests.Components;

using Microsoft.AspNetCore.Components.Forms;

/// <summary>
/// GenericNodeEditor 的 ImageList 编辑器接线：属性仍是 JSON 字符串，
/// 但表单渲染缩略图 / 删除 / 源码兜底，值变化通过 ValueChanged 写回节点。
/// </summary>
public class ImageListEditorTests
{
    private static IRenderedComponent<GenericNodeEditor<TestImageNode>> RenderEditor(
        BunitContext ctx,
        TestImageNode node)
    {
        ctx.Services.AddSingleton(EditorTestHelpers.CreateLocService());
        ctx.Services.AddSingleton(EditorTestHelpers.CreatePluginLoader());

        var attachments = new Mock<IImageAttachmentService>();
        attachments
            .Setup(s => s.GetDisplayUrl(It.IsAny<MessageImage>()))
            .Returns((MessageImage image) => image.Base64 is null
                ? null
                : $"data:{image.MimeType};base64,{image.Base64}");
        attachments
            .Setup(s => s.SaveAsync(It.IsAny<Stream>(), It.IsAny<string>()))
            .ReturnsAsync((Stream _, string contentType) =>
                MessageImage.FromFile("stored.png", contentType));
        ctx.Services.AddSingleton(attachments.Object);

        return ctx.Render<GenericNodeEditor<TestImageNode>>(ps => ps
            .Add(p => p.Node, node)
            .Add(p => p.Depth, 0)
            .AddCascadingValue(EditorTestHelpers.CreateEditorContext()));
    }

    [Fact]
    public void InlineBase64Images_RenderThumbnails()
    {
        using var ctx = new BunitContext();
        var node = new TestImageNode
        {
            ImagesJson = """[{"mimeType":"image/png","base64":"AAA"}]"""
        };

        var cut = RenderEditor(ctx, node);

        cut.FindAll(".ge-image-card").Should().ContainSingle();
        cut.Find(".ge-image-card img").GetAttribute("src")
            .Should().Be("data:image/png;base64,AAA");

        var input = cut.Find("input[type=file]");
        input.HasAttribute("multiple").Should().BeTrue();
        input.GetAttribute("accept").Should().Be("image/png,image/jpeg,image/webp,image/gif");
        cut.Find(".ge-image-add").GetAttribute("for").Should().Be(input.GetAttribute("id"));
    }

    [Fact]
    public void UploadingImage_SavesThroughServiceAndWritesFileNameJson()
    {
        using var ctx = new BunitContext();
        var node = new TestImageNode();
        var cut = RenderEditor(ctx, node);

        cut.FindComponent<InputFile>().UploadFiles(
            InputFileContent.CreateFromText("image-bytes", "test.png", contentType: "image/png"));

        cut.WaitForAssertion(() => node.ImagesJson.Should()
            .Be("""[{"mimeType":"image/png","fileName":"stored.png"}]"""));

        var service = ctx.Services.GetRequiredService<IImageAttachmentService>();
        Mock.Get(service).Verify(s => s.SaveAsync(It.IsAny<Stream>(), "image/png"), Times.Once);
    }

    [Fact]
    public void UploadingUnsupportedType_ShowsVisibleErrorAndDoesNotSave()
    {
        using var ctx = new BunitContext();
        var node = new TestImageNode();
        var cut = RenderEditor(ctx, node);

        cut.FindComponent<InputFile>().UploadFiles(
            InputFileContent.CreateFromText("text", "note.txt", contentType: "text/plain"));

        cut.WaitForAssertion(() => cut.Find(".ge-image-error").TextContent
            .Should().Contain("node_editor.image_type_unsupported"));

        var service = ctx.Services.GetRequiredService<IImageAttachmentService>();
        Mock.Get(service).Verify(s => s.SaveAsync(It.IsAny<Stream>(), It.IsAny<string>()), Times.Never);
        node.ImagesJson.Should().BeEmpty();
    }

    [Fact]
    public void RemoveButton_WritesEmptyJsonBackToTheNode()
    {
        using var ctx = new BunitContext();
        var node = new TestImageNode
        {
            ImagesJson = """[{"mimeType":"image/png","base64":"AAA"}]"""
        };
        var cut = RenderEditor(ctx, node);

        cut.Find(".ge-image-btn-danger").Click();

        cut.WaitForAssertion(() => node.ImagesJson.Should().Be("[]"));
        cut.FindAll(".ge-image-card").Should().BeEmpty();
        cut.Find(".ge-image-empty").Should().NotBeNull();
    }

    [Fact]
    public void MissingFileBackedImage_ShowsVisibleFallback()
    {
        using var ctx = new BunitContext();
        var node = new TestImageNode
        {
            ImagesJson = """[{"mimeType":"image/png","fileName":"gone.png"}]"""
        };

        var cut = RenderEditor(ctx, node);

        cut.Find(".ge-image-missing").TextContent.Should().Be("node_editor.image_missing");
    }

    [Fact]
    public void InvalidJson_ShowsErrorAndOpensSourceEditorInsteadOfSilentlyEmptying()
    {
        using var ctx = new BunitContext();
        var node = new TestImageNode { ImagesJson = "{ not an array" };

        var cut = RenderEditor(ctx, node);

        cut.Find(".ge-image-error").TextContent.Should().Contain("node_editor.image_invalid_json");
        cut.Find(".ge-image-json-text").Should().NotBeNull();
        // 原始值没有被改写
        node.ImagesJson.Should().Be("{ not an array");
    }

    [Fact]
    public void ApplyingSourceJson_NormalizesAndWritesBack()
    {
        using var ctx = new BunitContext();
        var node = new TestImageNode();
        var cut = RenderEditor(ctx, node);

        cut.Find(".ge-image-json-toggle").Click();
        cut.Find(".ge-image-json-text").Input("""["data:image/webp;base64,QUJD"]""");
        cut.Find(".ge-image-primary").Click();

        cut.WaitForAssertion(() => node.ImagesJson.Should()
            .Be("""[{"mimeType":"image/webp","base64":"QUJD"}]"""));
        cut.FindAll(".ge-image-card").Should().ContainSingle();
    }
}
