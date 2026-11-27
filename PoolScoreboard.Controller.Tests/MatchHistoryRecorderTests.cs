using System.IO;
using PoolScoreboard.Controller.Persistence;
using PoolScoreboard.Core;
using PoolScoreboard.Core.Enums;
using PoolScoreboard.Core.Persistence;

namespace PoolScoreboard.Controller.Tests;

/// <summary>
/// The recorder is what makes the history happen: a match is filed when it finishes, once — not once per event
/// after the win, and not again when a relaunch restores the same finished match.
/// </summary>
public sealed class MatchHistoryRecorderTests : IDisposable
{
    private static readonly DateTimeOffset At = new(2026, 11, 27, 21, 30, 0, TimeSpan.FromHours(1));

    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "poolscoreboard-recorder-tests", Guid.NewGuid().ToString("n"));

    private MatchHistoryStore Store => new(_root);

    [Fact]
    public void Recorder_FilesAFinishedMatch()
    {
        var manager = NewMatch();
        using var recorder = Start(manager, Store);

        Win(manager, homeWins: true);

        var record = Assert.Single(Store.Load().Matches);
        Assert.Equal("Home", record.HomeName);
        Assert.Equal("Away", record.AwayName);
        Assert.Equal(2, record.HomeScore);
        Assert.Equal(0, record.AwayScore);
        Assert.True(record.HomeWon);
        Assert.Equal(At, record.CompletedAt);
    }

    [Fact]
    public void Recorder_FilesNothingWhileTheMatchIsBeingPlayed()
    {
        var manager = NewMatch();
        using var recorder = Start(manager, Store);

        manager.AddPoint(isPlayer1: true);
        manager.ToggleCurrentShooter();

        Assert.Empty(Store.Load().Matches);
    }

    [Fact]
    public void Recorder_DoesNotFileTheSameMatchAgainWhenTheStateChanges()
    {
        var manager = NewMatch();
        using var recorder = Start(manager, Store);
        Win(manager, homeWins: true);

        // Everything an operator can still touch while the winner banner is up.
        manager.ToggleCurrentShooter();
        manager.PocketBall(3);
        manager.ResetBalls();
        manager.SubtractPoint(isPlayer1: false);

        Assert.Single(Store.Load().Matches);
    }

    [Fact]
    public void Recorder_FilesARematchThatEndsTheSameWay()
    {
        var manager = NewMatch();
        using var recorder = Start(manager, Store);
        Win(manager, homeWins: true);

        manager.ResetGame();
        manager.InitializeGame(GameType.EightBall, RaceToMode.Split);
        Win(manager, homeWins: true);

        var matches = Store.Load().Matches;
        Assert.Equal(2, matches.Count);
        Assert.NotEqual(matches[0].MatchId, matches[1].MatchId);
    }

    [Fact]
    public void Recorder_DoesNotFileAMatchThatWasAlreadyFiledBeforeItStarted()
    {
        // A relaunch: the finished match is restored and already in the history, so the recorder starts with no
        // memory of it and has to rely on the file's own rule.
        var manager = NewMatch();
        Win(manager, homeWins: true);
        Assert.NotNull(Store.TryArchive(manager.GetStateSnapshot(), At));

        using var recorder = Start(manager, new MatchHistoryStore(_root));
        manager.ToggleCurrentShooter();

        Assert.Single(Store.Load().Matches);
    }

    [Fact]
    public void Recorder_StopsWatchingWhenDisposed()
    {
        var manager = NewMatch();
        var recorder = Start(manager, Store);
        recorder.Dispose();

        Win(manager, homeWins: true);

        Assert.Empty(Store.Load().Matches);
    }

    /// <summary>Two named sides at a Race-To of 2, with the match started and nobody on a point yet.</summary>
    private static GameManager NewMatch()
    {
        var manager = new GameManager();
        manager.SetPlayerDetails(isHome: true, "Home", "Team H", 2, null);
        manager.SetPlayerDetails(isHome: false, "Away", "Team A", 2, null);
        manager.InitializeGame(GameType.EightBall, RaceToMode.Split);
        return manager;
    }

    private static void Win(GameManager manager, bool homeWins)
    {
        while (manager.GetStateSnapshot().Winner is null)
            manager.AddPoint(isPlayer1: homeWins);
    }

    private static MatchHistoryRecorder Start(GameManager manager, MatchHistoryStore store)
    {
        var recorder = new MatchHistoryRecorder(manager, store, () => At);
        recorder.Start();
        return recorder;
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
