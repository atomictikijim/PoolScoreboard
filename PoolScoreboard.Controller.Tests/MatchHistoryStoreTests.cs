using System.IO;
using System.Text.Json;
using PoolScoreboard.Controller.Persistence;
using PoolScoreboard.Core;
using PoolScoreboard.Core.Enums;
using PoolScoreboard.Core.Models;
using PoolScoreboard.Core.Persistence;

namespace PoolScoreboard.Controller.Tests;

/// <summary>
/// Exercises <see cref="MatchHistoryStore"/> against a throwaway folder: real files, no WPF, and nothing near the
/// operator's own <c>%AppData%</c>.
/// </summary>
public sealed class MatchHistoryStoreTests : IDisposable
{
    private static readonly DateTimeOffset CompletedAt = new(2026, 11, 27, 21, 30, 0, TimeSpan.FromHours(1));

    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "poolscoreboard-history-tests", Guid.NewGuid().ToString("n"));

    private string HistoryPath => Path.Combine(_root, "history.json");

    private string CorruptHistoryPath => Path.Combine(_root, "history.corrupt.json");

    private MatchHistoryStore Store => new(_root);

    [Fact]
    public void Load_ReturnsAnEmptyHistoryWhenNothingWasPlayed()
    {
        var archive = Store.Load();

        Assert.Empty(archive.Matches);
        Assert.False(File.Exists(HistoryPath));
    }

    [Fact]
    public void TryArchive_WritesTheFinishedMatch()
    {
        var record = Store.TryArchive(FinishedState(), CompletedAt);

        Assert.NotNull(record);
        Assert.True(File.Exists(HistoryPath));

        var loaded = Store.Load();
        var saved = Assert.Single(loaded.Matches);
        Assert.Equal("Home", saved.HomeName);
        Assert.Equal("Away", saved.AwayName);
        Assert.Equal(2, saved.HomeScore);
        Assert.Equal(0, saved.AwayScore);
        Assert.True(saved.HomeWon);
        Assert.Equal(CompletedAt, saved.CompletedAt);

        // The write goes through a temporary file, so a crash mid-save cannot truncate the previous history.
        Assert.False(File.Exists(HistoryPath + ".tmp"));
    }

    [Fact]
    public void TryArchive_KeepsEarlierMatches_NewestFirst()
    {
        Store.TryArchive(FinishedState(homeWins: true), CompletedAt);
        Store.TryArchive(FinishedState(homeWins: false), CompletedAt.AddMinutes(30));

        var loaded = Store.Load();

        Assert.Equal(2, loaded.Matches.Count);
        Assert.False(loaded.Matches[0].HomeWon);
        Assert.True(loaded.Matches[1].HomeWon);
    }

    [Fact]
    public void TryArchive_WritesNothingWhileTheMatchIsStillBeingPlayed()
    {
        var manager = new GameManager();
        manager.SetPlayerDetails(isHome: true, "Home", "", 3, null);
        manager.SetPlayerDetails(isHome: false, "Away", "", 3, null);
        manager.InitializeGame(GameType.TenBall, RaceToMode.Single);
        manager.AddPoint(isPlayer1: true);

        Assert.Null(Store.TryArchive(manager.GetStateSnapshot(), CompletedAt));

        // Nothing to record must not leave an empty history behind for the next read to trust.
        Assert.False(File.Exists(HistoryPath));
        Assert.Empty(Store.Load().Matches);
    }

    [Fact]
    public void TryArchive_LeavesTheWholeHistoryOnDisk_SoAFreshReadLoadsIt()
    {
        Assert.NotNull(Store.TryArchive(FinishedState(), CompletedAt));

        // Read the published file through a handle of its own, with no sharing: what a machine that lost power
        // has to find is a name pointing at every byte the serializer produced, not at an empty or short file.
        var expected = JsonSerializer.Serialize(Store.Load(), PersistenceJson.Options);
        using (var stream = new FileStream(HistoryPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.Equal(System.Text.Encoding.UTF8.GetByteCount(expected), (int)stream.Length);

            using var reader = new StreamReader(stream);
            var reloaded = JsonSerializer.Deserialize<MatchArchive>(reader.ReadToEnd(), PersistenceJson.Options);
            Assert.NotNull(reloaded);
            Assert.Single(reloaded.Matches);
        }
    }

    [Fact]
    public void TryArchive_DropsTheOldestMatchOnceTheHistoryIsFull()
    {
        var full = new MatchArchive
        {
            Matches = Enumerable
                .Range(0, MatchArchive.MaxRecords)
                .Select(index => new MatchRecord { HomeName = $"Old {index}" })
                .ToList()
        };
        Assert.True(Store.TrySave(full));

        var record = Store.TryArchive(FinishedState(), CompletedAt);

        Assert.NotNull(record);
        var loaded = Store.Load();
        Assert.Equal(MatchArchive.MaxRecords, loaded.Matches.Count);
        Assert.Equal("Home", loaded.Matches[0].HomeName);
        Assert.DoesNotContain(loaded.Matches, match => match.HomeName == $"Old {MatchArchive.MaxRecords - 1}");
    }

    [Fact]
    public void Load_KeepsAnUnreadableHistoryAside()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(HistoryPath, "{ not json");

        Assert.Empty(Store.Load().Matches);

        Assert.False(File.Exists(HistoryPath));
        Assert.Equal("{ not json", File.ReadAllText(CorruptHistoryPath));
    }

    [Fact]
    public void Load_IgnoresAHistoryWrittenByAnotherVersion_AndLeavesItInPlace()
    {
        Directory.CreateDirectory(_root);
        var future = new MatchArchive { FileVersion = MatchArchive.Version + 1 };
        future.Matches.Add(new MatchRecord { HomeName = "Home" });
        File.WriteAllText(HistoryPath, JsonSerializer.Serialize(future, PersistenceJson.Options));

        Assert.Empty(Store.Load().Matches);

        // Not moved aside: a newer build may be the one that can read it.
        Assert.True(File.Exists(HistoryPath));
    }

    [Fact]
    public void TryArchive_DoesNotRecordTheSameMatchTwice()
    {
        var state = FinishedState();

        Assert.NotNull(Store.TryArchive(state, CompletedAt));

        // The winner stays in state, so a later change (or a relaunch that restores the match) arrives with it.
        Assert.Null(Store.TryArchive(state, CompletedAt.AddMinutes(2)));
        Assert.Single(Store.Load().Matches);

        // The rule is on the file, not in the caller's memory: a fresh store sees the same one record.
        Assert.Single(new MatchHistoryStore(_root).Load().Matches);
    }

    [Fact]
    public void TryDelete_RemovesThatMatchAndKeepsTheRest()
    {
        var newest = Store.TryArchive(FinishedState(homeWins: true), CompletedAt);
        Store.TryArchive(FinishedState(homeWins: false), CompletedAt.AddMinutes(10));

        Assert.True(Store.TryDelete(newest!.MatchId));

        var kept = Assert.Single(Store.Load().Matches);
        Assert.False(kept.HomeWon);

        // A fresh instance reads the removal back: it is on the file, not just in the caller.
        Assert.Single(new MatchHistoryStore(_root).Load().Matches);
    }

    [Fact]
    public void TryDelete_ReportsFalseForAMatchItDoesNotHold()
    {
        Store.TryArchive(FinishedState(), CompletedAt);

        Assert.False(Store.TryDelete("not-a-match"));
        Assert.False(Store.TryDelete(null));
        Assert.False(Store.TryDelete(string.Empty));
        Assert.Single(Store.Load().Matches);
    }

    [Fact]
    public void TryDelete_OnAnEmptyFolder_LeavesNoFile()
    {
        // Nothing to remove means nothing to write: a History window opened before any match was saved should not
        // create the file just because the store was asked.
        Assert.False(Store.TryDelete("not-a-match"));
        Assert.False(File.Exists(Path.Combine(_root, "history.json")));
    }

    /// <summary>A real 8-ball match played to its target, so the state carries a winner the way the app's does.</summary>
    private static GameState FinishedState(bool homeWins = true)
    {
        var manager = new GameManager();
        manager.SetPlayerDetails(isHome: true, "Home", "Team H", 2, null);
        manager.SetPlayerDetails(isHome: false, "Away", "Team A", 2, null);
        manager.InitializeGame(GameType.EightBall, RaceToMode.Split);

        while (manager.GetStateSnapshot().Winner is null)
            manager.AddPoint(isPlayer1: homeWins);

        return manager.GetStateSnapshot();
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp folder is not worth failing a passing test over.
        }
    }
}
