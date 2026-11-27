using PoolScoreboard.Core.Enums;
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

    private static GameManager FinishedMatch(int homeTarget, int awayTarget, bool homeWins)
    {
        var manager = new GameManager();
        manager.SetPlayerDetails(isHome: true, "Home", "Team H", homeTarget, null);
        manager.SetPlayerDetails(isHome: false, "Away", "Team A", awayTarget, null);
        manager.InitializeGame(GameType.EightBall, RaceToMode.Split);

        while (manager.GetStateSnapshot().Winner is null)
            manager.AddPoint(isPlayer1: homeWins);

        return manager;
    }
}
