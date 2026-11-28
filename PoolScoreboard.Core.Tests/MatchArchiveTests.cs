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

    [Fact]
    public void Forget_RemovesOnlyTheMatchWithThatId()
    {
        var archive = new MatchArchive();
        var newest = archive.Archive(FinishedMatch(2, 2, homeWins: true).GetStateSnapshot(), CompletedAt);
        archive.Archive(FinishedMatch(2, 2, homeWins: false).GetStateSnapshot(), CompletedAt.AddMinutes(10));

        Assert.True(archive.Forget(newest!.MatchId));

        // The other match keeps its place and its chip; only the named one goes.
        var kept = Assert.Single(archive.Matches);
        Assert.False(kept.HomeWon);
        Assert.False(archive.Contains(newest.MatchId));
        Assert.Equal(new[] { "Away 1" }, archive.Standings().Select(standing => $"{standing.Name} {standing.Wins}"));
    }

    [Fact]
    public void Forget_SaysWhenItHeldNothing()
    {
        var archive = new MatchArchive();
        archive.Archive(FinishedMatch(2, 2, homeWins: true).GetStateSnapshot(), CompletedAt);

        Assert.False(archive.Forget("not-a-match"));
        Assert.False(archive.Forget(null));
        Assert.False(archive.Forget(string.Empty));
        Assert.Single(archive.Matches);
    }

    [Fact]
    public void Contains_AnswersOnlyForAMatchItHolds()
    {
        var archive = new MatchArchive();
        var record = archive.Archive(FinishedMatch(2, 2, homeWins: true).GetStateSnapshot(), CompletedAt);

        Assert.True(archive.Contains(record!.MatchId));
        Assert.False(archive.Contains("not-a-match"));
        Assert.False(archive.Contains(null));
        Assert.False(archive.Contains(string.Empty));
    }

    [Fact]
    public void PlayerRecords_CountsEveryNameThatPlayed_WonAndLost()
    {
        var archive = new MatchArchive();

        // Newest first, as the archive stores them: Bob beat Alice, Alice beat Bob, Carol beat Alice.
        archive.Matches.Add(Record("Bob", "Alice", homeWon: true, CompletedAt.AddMinutes(40)));
        archive.Matches.Add(Record("Alice", "Bob", homeWon: true, CompletedAt.AddMinutes(20)));
        archive.Matches.Add(Record("Alice", "Carol", homeWon: false, CompletedAt));

        var records = archive.PlayerRecords();

        // A loss puts a name in the table too — the chips in the History window only ever counted winners.
        Assert.Equal(new[] { "Alice", "Bob", "Carol" }, records.Select(record => record.Name));

        var alice = records[0];
        Assert.Equal(3, alice.Played);
        Assert.Equal(1, alice.Wins);
        Assert.Equal(2, alice.Losses);
        Assert.Equal(1d / 3, alice.WinRate, 3);

        // Her latest match, which is the one where she lost to Bob.
        Assert.Equal(CompletedAt.AddMinutes(40), alice.LastPlayed);

        Assert.Equal(2, records[1].Played);
        Assert.Equal(1, records[1].Wins);
        Assert.Equal(1, records[2].Played);
        Assert.Equal(1, records[2].Wins);
    }

    [Fact]
    public void PlayerRecords_IgnoresABlankSide_AndCountsOneNameOnBothSidesOnce()
    {
        var archive = new MatchArchive();
        archive.Matches.Add(Record("Alice", "alice", homeWon: true, CompletedAt));
        archive.Matches.Add(Record("Bob", "   ", homeWon: true, CompletedAt.AddMinutes(-5)));
        archive.Matches.Add(Record("  ", "  ", homeWon: true, CompletedAt.AddMinutes(-9)));

        var records = archive.PlayerRecords();

        // One person on both sides is one match, not two — anything else would make every count wrong.
        Assert.Equal(new[] { "Alice", "Bob" }, records.Select(record => record.Name));
        Assert.Equal(1, records[0].Played);
        Assert.Equal(1, records[0].Wins);
        Assert.Equal(1, records[1].Played);
    }

    [Fact]
    public void PlayerRecords_ShowsTheNameAsItWasLastTyped()
    {
        var archive = new MatchArchive();
        archive.Matches.Add(Record("alice", "Bob", homeWon: true, CompletedAt));
        archive.Matches.Add(Record("Alice", "Bob", homeWon: false, CompletedAt.AddMinutes(-10)));

        var player = archive.PlayerRecords().Single(record =>
            string.Equals(record.Name, "Alice", StringComparison.OrdinalIgnoreCase));

        // Ignoring case is identity; the spelling is the one from the newest match, as the chips do it.
        Assert.Equal("alice", player.Name);
        Assert.Equal(2, player.Played);
        Assert.Equal(1, player.Wins);
    }

    [Fact]
    public void Players_ListsEveryoneWhoPlayed_Alphabetically()
    {
        var archive = new MatchArchive();
        // Newest first: Alice's latest spelling is the plain one, and the older misspelling is the same person.
        archive.Matches.Add(Record("Zed", "Alice", homeWon: true, CompletedAt));
        archive.Matches.Add(Record("Bob", "alice", homeWon: false, CompletedAt.AddMinutes(-5)));

        Assert.Equal(new[] { "Alice", "Bob", "Zed" }, archive.Players());
    }

    [Fact]
    public void HeadToHead_CountsOnlyTheMatchesThoseTwoPlayedEachOther()
    {
        var archive = new MatchArchive();
        archive.Matches.Add(Record("Bob", "Alice", homeWon: true, CompletedAt.AddMinutes(30)));
        archive.Matches.Add(Record("Alice", "Bob", homeWon: true, CompletedAt.AddMinutes(20)));
        archive.Matches.Add(Record("Alice", "Carol", homeWon: true, CompletedAt.AddMinutes(10)));
        archive.Matches.Add(Record("Alice", "Bob", homeWon: false, CompletedAt));

        var headToHead = archive.HeadToHead("alice", "bob");

        Assert.NotNull(headToHead);
        Assert.Equal(3, headToHead!.Matches);

        // Whose win it is follows the side each name played, not the side the asker named first.
        Assert.Equal(1, headToHead.FirstWins);
        Assert.Equal(2, headToHead.SecondWins);
        Assert.Equal(CompletedAt.AddMinutes(30), headToHead.LastMeeting!.CompletedAt);
    }

    [Fact]
    public void HeadToHead_RefusesAQuestionThatIsNotOne()
    {
        var archive = new MatchArchive();
        archive.Matches.Add(Record("Alice", "Bob", homeWon: true, CompletedAt));

        Assert.Null(archive.HeadToHead(null, "Bob"));
        Assert.Null(archive.HeadToHead("Alice", "   "));

        // Nobody plays themselves, and PlayerRecords already answers what Alice has done.
        Assert.Null(archive.HeadToHead("Alice", "alice"));
    }

    [Fact]
    public void HeadToHead_ForTwoWhoHaveNotMet_IsAnEmptyRecordRatherThanNothing()
    {
        var archive = new MatchArchive();
        archive.Matches.Add(Record("Alice", "Carol", homeWon: true, CompletedAt));
        archive.Matches.Add(Record("Bob", "Dave", homeWon: true, CompletedAt.AddMinutes(-5)));

        var headToHead = archive.HeadToHead("Alice", "Bob");

        Assert.NotNull(headToHead);
        Assert.Equal(0, headToHead!.Matches);
        Assert.Equal(0, headToHead.FirstWins);
        Assert.Null(headToHead.LastMeeting);
    }

    private static MatchRecord Record(string home, string away, bool homeWon, DateTimeOffset when) => new()
    {
        MatchId = Guid.NewGuid().ToString("n"),
        CompletedAt = when,
        GameType = GameType.EightBall,
        RaceToMode = RaceToMode.Split,
        HomeName = home,
        HomeTeamName = "Team H",
        HomeRaceTo = 2,
        HomeScore = homeWon ? 2 : 1,
        AwayName = away,
        AwayTeamName = "Team A",
        AwayRaceTo = 2,
        AwayScore = homeWon ? 1 : 2,
        HomeWon = homeWon
    };
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
