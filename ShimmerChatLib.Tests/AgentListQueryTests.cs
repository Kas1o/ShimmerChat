using FluentAssertions;

namespace ShimmerChatLib.Tests;

public class AgentListQueryTests
{
    private static Agent MakeAgent(
        string name,
        string intro = "",
        string? greeting = null,
        IEnumerable<string>? tags = null,
        int chatCount = 0)
    {
        var agent = Agent.Create(name, intro, greeting);
        if (tags != null)
            agent.Tags = tags.ToList();
        for (var i = 0; i < chatCount; i++)
            agent.AddChatGuid(Guid.NewGuid());
        return agent;
    }

    [Fact]
    public void FilterAndSort_SearchMatchesNameIntroGreetingAndTags()
    {
        var byName = MakeAgent("Rainbow Dash", "fast flyer");
        var byIntro = MakeAgent("Twilight", "studies friendship magic");
        var byGreeting = MakeAgent("Pinkie", "party planner", "Hi there, best friend!");
        var byTag = MakeAgent("Applejack", "honest worker", tags: ["farming"]);
        var agents = new[] { byName, byIntro, byGreeting, byTag };

        AgentListQuery.FilterAndSort(agents, "rainbow", null, AgentSortMode.Default)
            .Should().Equal(byName);
        AgentListQuery.FilterAndSort(agents, "friendship", null, AgentSortMode.Default)
            .Should().Equal(byIntro);
        AgentListQuery.FilterAndSort(agents, "best friend", null, AgentSortMode.Default)
            .Should().Equal(byGreeting);
        AgentListQuery.FilterAndSort(agents, "farm", null, AgentSortMode.Default)
            .Should().Equal(byTag);
    }

    [Fact]
    public void FilterAndSort_MultipleTags_MatchesAnyTagIgnoringCase()
    {
        var magic = MakeAgent("A", tags: ["Magic"]);
        var farming = MakeAgent("B", tags: ["farming"]);
        var both = MakeAgent("C", tags: ["magic", "farming"]);
        var none = MakeAgent("D", tags: ["weather"]);
        var agents = new[] { magic, farming, both, none };

        var result = AgentListQuery.FilterAndSort(agents, null, ["MAGIC", "FARMING"], AgentSortMode.Default);

        result.Should().Equal(magic, farming, both);
    }

    [Fact]
    public void FilterAndSort_ExcludeMode_HidesAgentsMatchingAnyTag()
    {
        var magic = MakeAgent("A", tags: ["Magic"]);
        var farming = MakeAgent("B", tags: ["farming"]);
        var both = MakeAgent("C", tags: ["magic", "farming"]);
        var weather = MakeAgent("D", tags: ["weather"]);
        var untagged = MakeAgent("E");
        var agents = new[] { magic, farming, both, weather, untagged };

        var result = AgentListQuery.FilterAndSort(
            agents,
            null,
            ["MAGIC", "farming"],
            AgentSortMode.Default,
            AgentTagMatchMode.Exclude);

        result.Should().Equal(weather, untagged);
    }

    [Fact]
    public void FilterAndSort_ExcludeMode_CombinesWithSearch()
    {
        var luna = MakeAgent("Luna", tags: ["storytelling"]);
        var lunaCoding = MakeAgent("Luna Code", tags: ["coding"]);
        var other = MakeAgent("Mochi", tags: ["storytelling"]);

        var result = AgentListQuery.FilterAndSort(
            [luna, lunaCoding, other],
            "luna",
            ["storytelling"],
            AgentSortMode.Default,
            AgentTagMatchMode.Exclude);

        result.Should().Equal(lunaCoding);
    }

    [Fact]
    public void FilterAndSort_TagMatching_TrimsAgentTagWhitespace()
    {
        var spaced = MakeAgent("A", tags: ["  Magic  "]);
        var other = MakeAgent("B", tags: ["farming"]);

        AgentListQuery.FilterAndSort([spaced, other], null, ["magic"], AgentSortMode.Default)
            .Should().Equal(spaced);

        AgentListQuery.FilterAndSort([spaced, other], null, ["magic"], AgentSortMode.Default, AgentTagMatchMode.Exclude)
            .Should().Equal(other);
    }

    [Fact]
    public void FilterAndSort_ExcludeMode_KeepsAgentsWithNullTags()
    {
        var untagged = MakeAgent("A", tags: null);
        untagged.Tags = null!;
        var tagged = MakeAgent("B", tags: ["x"]);

        var result = AgentListQuery.FilterAndSort(
            [untagged, tagged],
            null,
            ["x"],
            AgentSortMode.Default,
            AgentTagMatchMode.Exclude);

        result.Should().Equal(untagged);
    }

    [Fact]
    public void FilterAndSort_BlankSearchAndTags_ReturnsAllAgents()
    {
        var agents = new[] { MakeAgent("A"), MakeAgent("B") };

        var result = AgentListQuery.FilterAndSort(agents, "   ", ["  "], AgentSortMode.Default);

        result.Should().Equal(agents);
    }

    [Fact]
    public void FilterAndSort_NoMatch_ReturnsEmpty()
    {
        var agents = new[] { MakeAgent("A"), MakeAgent("B") };

        var result = AgentListQuery.FilterAndSort(agents, "zzz", null, AgentSortMode.Default);

        result.Should().BeEmpty();
    }

    [Fact]
    public void FilterAndSort_NullTagList_DoesNotThrow()
    {
        var agents = new[] { MakeAgent("A", tags: null), MakeAgent("B", tags: ["x"]) };
        agents[0].Tags = null!;

        var result = AgentListQuery.FilterAndSort(agents, null, ["x"], AgentSortMode.Default);

        result.Should().Equal(agents[1]);
    }

    [Fact]
    public void FilterAndSort_NameSort_IgnoresCase()
    {
        var charlie = MakeAgent("charlie");
        var alice = MakeAgent("Alice");
        var bob = MakeAgent("BOB");
        var agents = new[] { charlie, alice, bob };

        AgentListQuery.FilterAndSort(agents, null, null, AgentSortMode.NameAscending)
            .Should().Equal(alice, bob, charlie);
        AgentListQuery.FilterAndSort(agents, null, null, AgentSortMode.NameDescending)
            .Should().Equal(charlie, bob, alice);
    }

    [Fact]
    public void FilterAndSort_ChatSort_UsesChatCount()
    {
        var none = MakeAgent("none");
        var one = MakeAgent("one", chatCount: 1);
        var three = MakeAgent("three", chatCount: 3);

        AgentListQuery.FilterAndSort([none, one, three], null, null, AgentSortMode.ChatsDescending)
            .Should().Equal(three, one, none);
        AgentListQuery.FilterAndSort([three, none, one], null, null, AgentSortMode.ChatsAscending)
            .Should().Equal(none, one, three);
    }

    [Fact]
    public void FilterAndSort_Default_PreservesInputOrder()
    {
        var first = MakeAgent("Zulu");
        var second = MakeAgent("Alpha");

        AgentListQuery.FilterAndSort([first, second], null, null, AgentSortMode.Default)
            .Should().Equal(first, second);
    }

    [Fact]
    public void TryParseSortMode_KnownValue_ReturnsTrue()
    {
        AgentListQuery.TryParseSortMode("NameAscending", out var mode).Should().BeTrue();
        mode.Should().Be(AgentSortMode.NameAscending);
    }

    [Fact]
    public void TryParseSortMode_UnknownValue_ReturnsFalseAndDefault()
    {
        AgentListQuery.TryParseSortMode("Sideways", out var mode).Should().BeFalse();
        mode.Should().Be(AgentSortMode.Default);

        AgentListQuery.TryParseSortMode(null, out mode).Should().BeFalse();
        mode.Should().Be(AgentSortMode.Default);
    }

    [Fact]
    public void TryParseTagMatchMode_KnownValue_ReturnsTrue()
    {
        AgentListQuery.TryParseTagMatchMode("Exclude", out var mode).Should().BeTrue();
        mode.Should().Be(AgentTagMatchMode.Exclude);
    }

    [Fact]
    public void TryParseTagMatchMode_UnknownValue_ReturnsFalseAndInclude()
    {
        AgentListQuery.TryParseTagMatchMode("Backwards", out var mode).Should().BeFalse();
        mode.Should().Be(AgentTagMatchMode.Include);

        AgentListQuery.TryParseTagMatchMode(null, out mode).Should().BeFalse();
        mode.Should().Be(AgentTagMatchMode.Include);
    }

    [Fact]
    public void CollectTagCounts_CountsCaseInsensitively_AndSorts()
    {
        var agents = new[]
        {
            MakeAgent("A", tags: ["Magic", "farming", "  "]),
            MakeAgent("B", tags: ["magic", "Farming", "magic"]),
            MakeAgent("C", tags: null),
            MakeAgent("D", tags: ["Magic"]),
        };
        agents[2].Tags = null!;

        var result = AgentListQuery.CollectTagCounts(agents);

        result.Should().HaveCount(2);
        result.Should().ContainInOrder(
            new AgentTagCount("Magic", 3),
            new AgentTagCount("farming", 2));
        result.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void CollectTagCounts_EmptyInput_ReturnsEmpty()
    {
        AgentListQuery.CollectTagCounts([]).Should().BeEmpty();
    }

    [Fact]
    public void FilterAndSort_NullAgents_Throws()
    {
        var act = () => AgentListQuery.FilterAndSort(null!, null, null, AgentSortMode.Default);

        act.Should().Throw<ArgumentNullException>();
    }
}
