using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace ShimmerChatLib.Tests.Components;

/// <summary>
/// 节点属性动态选项选择器：分级（分组）展示、搜索（含自定义额外搜索词）、
/// 副标题开关、空选项与「值已失效」的可见提示。
/// </summary>
public class PropertyOptionsSelectTests
{
    private static readonly NodePropertyOption[] GroupedOptions =
    [
        new("a", "Alpha", Group: "Group A", Description: "first"),
        new("b", "Beta", Group: "Group A", Description: "second"),
        new("c", "Gamma", Group: "Group B", Description: "third"),
    ];

    private static IRenderedComponent<PropertyOptionsSelect> Render(
        BunitContext ctx,
        IReadOnlyList<NodePropertyOption> options,
        string value = "",
        bool searchable = false,
        bool showDescription = false,
        string defaultLabel = "(none)",
        string emptyHint = "",
        Action<string>? changed = null)
    {
        ctx.Services.AddSingleton<ILocService>(new TestLoc());

        return ctx.Render<PropertyOptionsSelect>(ps =>
        {
            ps.Add(p => p.Options, options)
              .Add(p => p.Value, value)
              .Add(p => p.Searchable, searchable)
              .Add(p => p.ShowDescription, showDescription)
              .Add(p => p.DefaultLabel, defaultLabel)
              .Add(p => p.EmptyHint, emptyHint)
              .Add(p => p.ValueChanged, EventCallback.Factory.Create<string>(
                  new object(), v => changed?.Invoke(v)));
        });
    }

    [Fact]
    public void EmptyValue_ShowsDefaultLabel()
    {
        using var ctx = new BunitContext();
        var cut = Render(ctx, GroupedOptions, defaultLabel: "(use default)");

        cut.Find(".po-trigger-label").TextContent.Should().Be("(use default)");
    }

    [Fact]
    public void SelectedValue_ShowsOptionLabel()
    {
        using var ctx = new BunitContext();
        var cut = Render(ctx, GroupedOptions, value: "b");

        cut.Find(".po-trigger-label").TextContent.Should().Be("Beta");
    }

    [Fact]
    public void Trigger_Click_OpensPanelWithGroupedOptions()
    {
        using var ctx = new BunitContext();
        var cut = Render(ctx, GroupedOptions);

        cut.FindAll(".po-panel").Should().BeEmpty();
        cut.Find(".po-trigger").Click();

        // 一个空值项 + 三个选项
        cut.FindAll(".po-item").Count.Should().Be(4);
        cut.FindAll(".po-group").Select(g => g.TextContent).Should().Equal("Group A", "Group B");
    }

    [Fact]
    public void SelectingOption_RaisesCallbackWithValueAndCloses()
    {
        using var ctx = new BunitContext();
        string? picked = null;
        var cut = Render(ctx, GroupedOptions, changed: v => picked = v);

        cut.Find(".po-trigger").Click();
        cut.FindAll(".po-item").Single(i => i.TextContent.Contains("Gamma")).Click();

        picked.Should().Be("c");
        cut.FindAll(".po-panel").Should().BeEmpty();
        cut.Find(".po-trigger-label").TextContent.Should().Be("Gamma");
    }

    [Fact]
    public void Description_OnlyRenderedWhenEnabled()
    {
        using var ctxWithout = new BunitContext();
        var withoutDesc = Render(ctxWithout, GroupedOptions);
        withoutDesc.Find(".po-trigger").Click();
        withoutDesc.FindAll(".po-item-desc").Should().BeEmpty();
        // 关掉副标题时改用 title 提示，信息不丢
        withoutDesc.FindAll(".po-item")[1].GetAttribute("title").Should().Be("first");

        using var ctxWith = new BunitContext();
        var withDesc = Render(ctxWith, GroupedOptions, showDescription: true);
        withDesc.Find(".po-trigger").Click();
        withDesc.FindAll(".po-item-desc").Select(d => d.TextContent)
            .Should().Equal("first", "second", "third");
    }

    [Fact]
    public void SearchBox_HiddenForSmallListsEvenWhenSearchable()
    {
        using var ctx = new BunitContext();
        var cut = Render(ctx, GroupedOptions, searchable: true);

        cut.Find(".po-trigger").Click();

        cut.FindAll(".po-search").Should().BeEmpty();
    }

    [Fact]
    public void Search_FiltersByLabelAndCustomSearchTerms()
    {
        using var ctx = new BunitContext();
        var options = Enumerable.Range(0, 10)
            .Select(i => new NodePropertyOption($"v{i}", $"Label{i}", "G" + (i % 2),
                SearchTerms: i == 3 ? ["needle"] : null))
            .ToList();

        var cut = Render(ctx, options, searchable: true);
        cut.Find(".po-trigger").Click();
        OptionLabels(cut).Should().HaveCount(10);

        // 标签匹配
        cut.Find(".po-search").Input("label7");
        OptionLabels(cut).Should().Equal("Label7");

        // 自定义额外搜索词匹配：Label3 本身不含 needle
        cut.Find(".po-search").Input("needle");
        OptionLabels(cut).Should().Equal("Label3");

        // 无匹配时给出提示，而不是静默列出全部
        cut.Find(".po-search").Input("zzz");
        OptionLabels(cut).Should().BeEmpty();
        cut.Find(".po-empty").TextContent.Should().Be("node_editor.no_match");
    }

    /// <summary>排除始终存在的空值项（它不受搜索过滤）。</summary>
    private static List<string> OptionLabels(IRenderedComponent<PropertyOptionsSelect> cut) =>
        cut.FindAll(".po-item")
            .Where(i => !i.ClassList.Contains("po-item-default"))
            .Select(i => i.TextContent)
            .ToList();

    [Fact]
    public void NoOptions_ShowsEmptyHint()
    {
        using var ctx = new BunitContext();
        var cut = Render(ctx, [], emptyHint: "nothing to pick");

        cut.Find(".po-hint").TextContent.Should().Be("nothing to pick");
    }

    [Fact]
    public void MissingValue_ShowsWarningInsteadOfSilentFallback()
    {
        using var ctx = new BunitContext();
        var cut = Render(ctx, GroupedOptions, value: "deleted-id");

        cut.Find(".po-trigger-dangling").TextContent.Should().Be("deleted-id");
        cut.Find(".po-hint").TextContent.Should().Contain("deleted-id");
    }

    /// <summary>返回 Key 本身的本地化桩；带参格式化的 Key 回显参数，便于断言。</summary>
    private sealed class TestLoc : ILocService
    {
        public string CurrentCulture => "zh-CN";

        public IReadOnlyList<string> SupportedCultures => ["zh-CN", "en-US"];

        public string this[string key] => key;

        public string Format(string key, params object[] args) =>
            $"{key}:{string.Join(",", args)}";

        public string Format(string key, params (string name, object value)[] args) => key;

        public void SetCulture(string culture) { }
    }
}
