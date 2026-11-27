using PoolScoreboard.Core.Enums;
using PoolScoreboard.Core.Models;
using PoolScoreboard.Core.Persistence;

namespace PoolScoreboard.Core.Tests;

/// <summary>
/// A finished match has to be recorded as it was played, and only once it is finished: the archive is what says
/// who won an evening's games long after the overlay has been reset.
/// </summary>
public sealed class MatchArchiveTests
{
    private static readonly DateTimeOffset CompletedAt = new(2026, 11, 27, 21, 30, 0, TimeSpan.FromHours(1));

    [Fact]
    public void Archive_RecordsWhoPlayedAndHowItEnded()
    {
        var manager = FinishedMatch(homeTarget: 2, awayTarget: 2, homeWins: true);

        var record = new MatchArchive().Archive(manager.GetStateSnapshot(), CompletedAt);

        Assert.NotNull(record);
        Assert.Equal(CompletedAt, record.CompletedAt);
        Assert.Equal(GameType.EightBall, record.GameType);
        Assert.Equal(RaceToMode.Split, record.RaceToMode);
        Assert.Equal("Home", record.HomeName);
        Assert.Equal("Team H", record.HomeTeamName);
        Assert.Equal(2, record.HomeRaceTo);
        Assert.Equal(2, record.HomeScore);
        Assert.Equal("Away", record.AwayName);
        Assert.Equal("Team A", record.AwayTeamName);
        Assert.Equal(2, record.AwayRaceTo);
        Assert.Equal(0, record.AwayScore);
        Assert.True(record.HomeWon);
    }

    [Fact]
    public void Archive_RecordsEachSidesOwnTargetAndAnAwayWinner()
    {
        var manager = FinishedMatch(homeTarget: 7, awayTarget: 3, homeWins: false);

        var record = new MatchArchive().Archive(manager.GetStateSnapshot(), CompletedAt);

        Assert.NotNull(record);
        Assert.Equal(7, record.HomeRaceTo);
        Assert.Equal(3, record.AwayRaceTo);
        Assert.Equal(0, record.HomeScore);
        Assert.Equal(3, record.AwayScore);
        Assert.False(record.HomeWon);
    }

    [Fact]
    public void Archive_RefusesAMatchNobodyHasWon()
    {
        var manager = new GameManager();
        manager.SetPlayerDetails(isHome: true, "Home", "", 3, null);
        manager.SetPlayerDetails(isHome: false, "Away", "", 3, null);
        manager.InitializeGame(GameType.NineBall, RaceToMode.Single);
        manager.AddPoint(isPlayer1: true);

        var archive = new MatchArchive();

        Assert.Null(archive.Archive(manager.GetStateSnapshot(), CompletedAt));

        // A side still at setup is not a finished match either.
        Assert.Null(archive.Archive(new GameManager().GetStateSnapshot(), CompletedAt));
        Assert.Empty(archive.Matches);
    }

    [Fact]
    public void Archive_PutsTheNewestMatchFirst()
    {
        var archive = new MatchArchive();
        var earlier = FinishedMatch(2, 2, homeWins: true).GetStateSnapshot();
        var later = FinishedMatch(2, 2, homeWins: false).GetStateSnapshot();

        archive.Archive(earlier, CompletedAt);
        archive.Archive(later, CompletedAt.AddMinutes(20));

        Assert.Equal(2, archive.Matches.Count);
        Assert.Equal(CompletedAt.AddMinutes(20), archive.Matches[0].CompletedAt);
        Assert.False(archive.Matches[0].HomeWon);
        Assert.True(archive.Matches[1].HomeWon);
    }

    [Fact]
    public void Archive_KeepsTheNewestMatchesWhenTheHistoryIsFull()
    {
        var archive = new MatchArchive
        {
            Matches = Enumerable
                .Range(0, MatchArchive.MaxRecords)
                .Select(index => new MatchRecord { HomeName = $"Old {index}", CompletedAt = CompletedAt.AddDays(-1) })
                .ToList()
        };

        var record = archive.Archive(FinishedMatch(2, 2, homeWins: true).GetStateSnapshot(), CompletedAt);

        Assert.NotNull(record);
        Assert.Equal(MatchArchive.MaxRecords, archive.Matches.Count);
        Assert.Same(record, archive.Matches[0]);

        // The oldest went, not the newest: the list stays a window on recent play.
        Assert.DoesNotContain(archive.Matches, match => match.HomeName == $"Old {MatchArchive.MaxRecords - 1}");
    }

    [Fact]
    public void Archive_FilesTheSameMatchOnlyOnce()
    {
        var archive = new MatchArchive();
        var manager = FinishedMatch(2, 2, homeWins: true);

        Assert.NotNull(archive.Archive(manager.GetStateSnapshot(), CompletedAt));

        // The winner stays in state while the banner is up, so every later change arrives with it.
        Assert.Null(archive.Archive(manager.GetStateSnapshot(), CompletedAt.AddMinutes(1)));
        Assert.Single(archive.Matches);
    }

    [Fact]
    public void Archive_FilesARematchThatEndsTheSameWay()
    {
        var archive = new MatchArchive();
        var first = FinishedMatch(2, 2, homeWins: true);
        var second = FinishedMatch(2, 2, homeWins: true);

        archive.Archive(first.GetStateSnapshot(), CompletedAt);
        var record = archive.Archive(second.GetStateSnapshot(), CompletedAt.AddMinutes(30));

        // Same two people, same race, same score — a different match, because it has its own id.
        Assert.NotNull(record);
        Assert.Equal(2, archive.Matches.Count);
        Assert.NotEqual(archive.Matches[0].MatchId, archive.Matches[1].MatchId);
    }

    [Fact]
    public void Archive_StillFilesAStateThatCarriesNoMatchId()
    {
        // A hand-made or pre-history state has nothing to tell matches apart by; a possible duplicate record
        // is better than a silently missing win.
        var state = new GameState
        {
            Player1 = new Player { Name = "Home", RaceToTarget = 1 },
            Player2 = new Player { Name = "Away", RaceToTarget = 1 },
            Player1Score = 1,
            IsGameActive = true
        };
        state.Winner = state.Player1;

        var archive = new MatchArchive();

        Assert.NotNull(archive.Archive(state, CompletedAt));
        Assert.NotNull(archive.Archive(state, CompletedAt));
        Assert.Equal(2, archive.Matches.Count);
    }

    [Fact]
    public void Standings_CountTheWinsEachNameHas()
    {
        var archive = new MatchArchive();
        archive.Archive(FinishedMatch(2, 2, homeWins: true).GetStateSnapshot(), CompletedAt);
        archive.Archive(FinishedMatch(2, 2, homeWins: true).GetStateSnapshot(), CompletedAt.AddMinutes(10));
        archive.Archive(FinishedMatch(2, 2, homeWins: false).GetStateSnapshot(), CompletedAt.AddMinutes(20));

        var standings = archive.Standings();

        Assert.Equal(2, standings.Count);
        Assert.Equal("Home", standings[0].Name);
        Assert.Equal(2, standings[0].Wins);
        Assert.Equal("Away", standings[1].Name);
        Assert.Equal(1, standings[1].Wins);
    }

    [Fact]
    public void Standings_TreatACaseAndSpacingDifferenceAsTheSameName()
    {
        var archive = new MatchArchive();
        archive.Archive(FinishedMatch(2, 2, homeWins: true, "Alice", "Bob").GetStateSnapshot(), CompletedAt);
        archive.Archive(FinishedMatch(2, 2, homeWins: true, "alice ", "Bob").GetStateSnapshot(), CompletedAt.AddMinutes(5));

        var standing = Assert.Single(archive.Standings());

        // One player, two spellings: the wins add up, and the name shown is the most recent spelling — what the
        // operator last typed — because the archive is walked newest first.
        Assert.Equal("alice", standing.Name);
        Assert.Equal(2, standing.Wins);
    }

    [Fact]
    public void Standings_PutTheMostWinsFirst_ThenOrderByName()
    {
        var archive = new MatchArchive();
        archive.Archive(FinishedMatch(2, 2, homeWins: true, "Zed", "Opponent").GetStateSnapshot(), CompletedAt);
        archive.Archive(FinishedMatch(2, 2, homeWins: true, "Bea", "Opponent").GetStateSnapshot(), CompletedAt.AddMinutes(5));
        archive.Archive(FinishedMatch(2, 2, homeWins: true, "Zed", "Opponent").GetStateSnapshot(), CompletedAt.AddMinutes(10));
        archive.Archive(FinishedMatch(2, 2, homeWins: true, "Amy", "Opponent").GetStateSnapshot(), CompletedAt.AddMinutes(15));

        var names = archive.Standings().Select(standing => $"{standing.Name} {standing.Wins}").ToArray();

        Assert.Equal(new[] { "Zed 2", "Amy 1", "Bea 1" }, names);
    }

    [Fact]
    public void Standings_IgnoreASideWithNoName()
    {
        var archive = new MatchArchive();
        archive.Archive(FinishedMatch(2, 2, homeWins: true, string.Empty, "Away").GetStateSnapshot(), CompletedAt);

        // Nobody to credit, so no chip; the match itself is still recorded.
        Assert.Empty(archive.Standings());
        Assert.Single(archive.Matches);
    }

    private static GameManager FinishedMatch(
        int homeTarget,
        int awayTarget,
        bool homeWins,
        string home = "Home",
        string away = "Away")
    {
        var manager = new GameManager();
        manager.SetPlayerDetails(isHome: true, home, "Team H", homeTarget, null);
        manager.SetPlayerDetails(isHome: false, away, "Team A", awayTarget, null);
        manager.InitializeGame(GameType.EightBall, RaceToMode.Split);

        while (manager.GetStateSnapshot().Winner is null)
            manager.AddPoint(isPlayer1: homeWins);

        return manager;
    }
}
