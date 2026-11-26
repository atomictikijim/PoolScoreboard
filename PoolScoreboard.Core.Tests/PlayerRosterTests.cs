using PoolScoreboard.Core.Persistence;

namespace PoolScoreboard.Core.Tests;

public class PlayerRosterTests
{
    private static string Id(string value) => value;

    [Fact]
    public void Remember_AddsANewPlayerAtTheFront()
    {
        var roster = new PlayerRoster();

        roster.Remember("Alice", "Blue Cuers", 7, "a");
        roster.Remember("Bob", "Red Rackers", 5, "b");

        Assert.Equal(new[] { "Bob", "Alice" }, roster.Players.Select(p => p.Name));
        Assert.Equal("Red Rackers", roster.Players[0].TeamName);
        Assert.Equal(5, roster.Players[0].RaceToTarget);
        Assert.Equal("b", roster.Players[0].Id);
    }

    [Fact]
    public void Remember_MatchesAReturningPlayerIgnoringCaseAndSpace_AndKeepsTheirId()
    {
        var roster = new PlayerRoster();
        var first = roster.Remember("Alice", "Blue Cuers", 7, "a");

        var again = roster.Remember("  aLiCe ", "Green Baize", 4, "should-not-be-used");

        Assert.NotNull(first);
        Assert.Same(first, again);
        Assert.Single(roster.Players);
        Assert.Equal("a", roster.Players[0].Id);
        // The most recent spelling wins, so a name typed in caps stays in caps.
        Assert.Equal("aLiCe", roster.Players[0].Name);
        Assert.NotNull(roster.FindByName("alice"));
        Assert.Equal("Green Baize", roster.Players[0].TeamName);
        Assert.Equal(4, roster.Players[0].RaceToTarget);
    }

    [Fact]
    public void Remember_MovesAReturningPlayerToTheFront()
    {
        var roster = new PlayerRoster();
        roster.Remember("Alice", "", 5, "a");
        roster.Remember("Bob", "", 5, "b");
        roster.Remember("Cara", "", 5, "c");

        roster.Remember("Alice", "", 6, "a");

        Assert.Equal(new[] { "Alice", "Cara", "Bob" }, roster.Players.Select(p => p.Name));
        Assert.Equal(6, roster.Players[0].RaceToTarget);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Remember_BlankName_AddsNothing(string? name)
    {
        var roster = new PlayerRoster();

        Assert.Null(roster.Remember(name, "Team", 5, "a"));
        Assert.Empty(roster.Players);
    }

    [Fact]
    public void Remember_WithoutAnId_AddsNothing()
    {
        var roster = new PlayerRoster();

        Assert.Null(roster.Remember("Alice", "", 5, "  "));
        Assert.Empty(roster.Players);
    }

    [Fact]
    public void Remember_TrimsTheTeamToo()
    {
        var roster = new PlayerRoster();

        roster.Remember(" Alice ", " Blue Cuers ", 5, "a");

        Assert.Equal("Alice", roster.Players[0].Name);
        Assert.Equal("Blue Cuers", roster.Players[0].TeamName);
    }

    [Fact]
    public void Remember_KeepsAtMostMaxEntries_DroppingTheOldest()
    {
        var roster = new PlayerRoster();
        for (var i = 0; i < PlayerRoster.MaxEntries; i++)
            roster.Remember($"Player {i}", "", 5, $"p{i}");

        roster.Remember("Newest", "", 5, "newest");

        Assert.Equal(PlayerRoster.MaxEntries, roster.Players.Count);
        Assert.Equal("Newest", roster.Players[0].Name);
        Assert.DoesNotContain(roster.Players, p => p.Name == "Player 0");
        Assert.Contains(roster.Players, p => p.Name == $"Player {PlayerRoster.MaxEntries - 1}");
    }

    [Fact]
    public void Find_LooksUpByIdAndByName()
    {
        var roster = new PlayerRoster();
        roster.Remember("Alice", "Blue Cuers", 5, "a");

        Assert.Equal("a", roster.Find("a")!.Id);
        Assert.Null(roster.Find("missing"));
        Assert.Null(roster.Find(null));
        Assert.Equal("a", roster.FindByName("alice")!.Id);
        Assert.Null(roster.FindByName("  "));
    }

    [Fact]
    public void Forget_RemovesOnlyThatPlayer()
    {
        var roster = new PlayerRoster();
        roster.Remember("Alice", "", 5, "a");
        roster.Remember("Bob", "", 5, "b");

        Assert.True(roster.Forget("a"));
        Assert.False(roster.Forget("a"));
        Assert.False(roster.Forget(null));
        Assert.Equal(new[] { "Bob" }, roster.Players.Select(p => p.Name));
    }

    [Fact]
    public void Search_MatchesNameOrTeamCaseInsensitively_AndKeepsRecencyOrder()
    {
        var roster = new PlayerRoster();
        roster.Remember("Alice", "Blue Cuers", 5, "a");
        roster.Remember("Bob", "Blue Rackers", 5, "b");
        roster.Remember("Cara", "Green Baize", 5, "c");

        Assert.Equal(new[] { "Bob", "Alice" }, roster.Search("blue").Select(p => p.Name));
        Assert.Equal(new[] { "Cara" }, roster.Search(" baize ").Select(p => p.Name));
        Assert.Equal(3, roster.Search(null).Count);
        Assert.Empty(roster.Search("nobody"));
    }

    [Fact]
    public void Players_KeepTheOrderTheyWereLoadedIn()
    {
        var roster = new PlayerRoster
        {
            Players =
            {
                new RosterEntry { Id = "z", Name = "Zoe", RaceToTarget = 9 },
                new RosterEntry { Id = "y", Name = "Yusuf", RaceToTarget = 3 }
            }
        };

        Assert.Equal(new[] { "Zoe", "Yusuf" }, roster.Players.Select(p => p.Name));
        Assert.Equal(9, roster.Players[0].RaceToTarget);
    }
}
