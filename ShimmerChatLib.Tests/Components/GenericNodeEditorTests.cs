namespace ShimmerChatLib.Tests.Components;

/// <summary>
/// GenericNodeEditor 的数字属性接线：int / long / double 都必须渲染成可编辑的
/// number input 并把值写回节点，否则节点上的像素预算等配置形同虚设。
/// </summary>
public class GenericNodeEditorTests
{
    private static IRenderedComponent<GenericNodeEditor<TestNumbersNode>> RenderEditor(
        BunitContext ctx, TestNumbersNode node)
    {
        ctx.Services.AddSingleton(EditorTestHelpers.CreateLocService());
        ctx.Services.AddSingleton(EditorTestHelpers.CreatePluginLoader());

        return ctx.Render<GenericNodeEditor<TestNumbersNode>>(ps => ps
            .Add(p => p.Node, node)
            .Add(p => p.Depth, 0)
            .AddCascadingValue(EditorTestHelpers.CreateEditorContext()));
    }

    [Fact]
    public void NumberProperties_RenderEditableInputsInsteadOfReadOnlyText()
    {
        using var ctx = new BunitContext();
        var node = new TestNumbersNode();

        var cut = RenderEditor(ctx, node);

        var inputs = cut.FindAll("input.ge-input-num");
        inputs.Should().HaveCount(3, "int / long / double 各一个可编辑 number input");

        inputs[0].GetAttribute("value").Should().Be("4194304");
        inputs[1].GetAttribute("value").Should().Be("85");
        inputs[2].GetAttribute("value").Should().Be("0.7");
        inputs[2].GetAttribute("step").Should().Be("0.1", "浮点属性允许小数步进");
    }

    [Fact]
    public void EditingPixelBudget_WritesTheLongValueBackToTheNode()
    {
        // long 属性曾掉进只读兜底：像素预算在节点编辑器里看得见改不了
        using var ctx = new BunitContext();
        var node = new TestNumbersNode();
        var cut = RenderEditor(ctx, node);

        cut.FindAll("input.ge-input-num")[0].Change("1024000");

        node.PixelBudget.Should().Be(1_024_000);
    }

    [Fact]
    public void EditingIntAndDoubleProperties_WritesThemBack()
    {
        using var ctx = new BunitContext();
        var node = new TestNumbersNode();
        var cut = RenderEditor(ctx, node);

        // 每次 Change 会触发重渲染：必须重新查找，旧元素上的事件绑定已失效
        cut.FindAll("input.ge-input-num")[1].Change("70");
        cut.FindAll("input.ge-input-num")[2].Change("1.5");

        node.Quality.Should().Be(70);
        node.Scale.Should().Be(1.5);
    }
}
