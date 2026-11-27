using System.IO;
using PoolScoreboard.Controller.Persistence;
using PoolScoreboard.Core;
using PoolScoreboard.Core.Enums;
using PoolScoreboard.Core.Persistence;

namespace PoolScoreboard.Controller.Tests;

/// <summary>
/// Crash-safety used to be proven only by hand: the debounce, the delete-on-reset rule, resume and the unreadable
/// file all lived in <c>App</c>. <see cref="MatchAutosave"/> is that wiring on its own, driven here with a store that
/// counts calls (a real timer, a short delay) and with the real file store for the paths that touch disk.
/// </summary>
public sealed class MatchAutosaveTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "poolscoreboard-autosave-tests", Guid.NewGuid().ToString("n"));

    private string MatchPath => Path.Combine(_root, "match.json");

    private string CorruptMatchPath => Path.Combine(_root, "match.corrupt.json");

    [Fact]
    public void ABurstOfChanges_IsWrittenOnceAfterItSettles()
    {
        var manager = StartedGame();
        var store = new CountingStore();
        using var autosave = new MatchAutosave(manager, store, TimeSpan.FromMilliseconds(40));
        autosave.Restore();

        for (var point = 0; point < 20; point++)
            manager.AddPoint(isPlayer1: true);

        Assert.True(WaitUntil(() => store.Saves > 0), "the debounced snapshot was never written");
        Thread.Sleep(200);

        Assert.Equal(1, store.Saves);
        Assert.Equal(20, store.LastSaved?.HomeScore);
        Assert.Equal(0, store.Deletes);
    }

    [Fact]
    public void AReset_DeletesTheSnapshot_InsteadOfWritingABlankOne()
    {
        var manager = StartedGame();
        var store = new CountingStore();
        using var autosave = new MatchAutosave(manager, store, TimeSpan.FromMilliseconds(40));
        autosave.Restore();

        manager.AddPoint(isPlayer1: false);
        manager.ResetGame();

        Assert.True(WaitUntil(() => store.Deletes > 0), "the reset never deleted the saved match");
        Thread.Sleep(150);

        Assert.Equal(1, store.Deletes);
        Assert.Equal(0, store.Saves);
    }

    [Fact]
    public void SaveNow_DoesNotWaitForTheDebounce()
    {
        var manager = StartedGame();
        var store = new CountingStore();

        // A delay far longer than the test: only the exit path can produce a write here.
        using var autosave = new MatchAutosave(manager, store, TimeSpan.FromSeconds(30));
        autosave.Restore();
        manager.AddPoint(isPlayer1: true);
        autosave.SaveNow();

        Assert.Equal(1, store.Saves);
        Assert.Equal(1, store.LastSaved?.HomeScore);
    }

    [Fact]
    public void ChangesAfterDispose_AreIgnored()
    {
        var manager = StartedGame();
        var store = new CountingStore();
        var autosave = new MatchAutosave(manager, store, TimeSpan.FromMilliseconds(40));
        autosave.Restore();

        manager.AddPoint(isPlayer1: true);
        autosave.Dispose();
        Thread.Sleep(200);

        Assert.Equal(0, store.Saves);
        Assert.Equal(0, store.Deletes);
    }

    [Fact]
    public void ChangesBeforeRestore_AreNotWatched_SoTheMatchAboutToBeResumedSurvives()
    {
        // The order App uses: settings are applied — which fires the state event several times, still at setup —
        // before the saved match is loaded. A snapshot taken then would delete the file this launch is resuming.
        var store = new MatchStateStore(_root);
        Assert.True(store.TrySave(SavedMatch()));

        var manager = new GameManager();
        using var autosave = new MatchAutosave(manager, store, TimeSpan.FromMilliseconds(40));
        new OperatorProfile().ApplyTo(manager);

        Thread.Sleep(250);

        Assert.True(File.Exists(MatchPath), "a change before the resume deleted the match it was about to read");

        autosave.Restore();

        Assert.Equal(3, manager.GetStateSnapshot().Player1Score);
    }

    [Fact]
    public void Restore_PutsTheSavedMatchBack()
    {
        var store = new MatchStateStore(_root);
        Assert.True(store.TrySave(SavedMatch()));

        var manager = new GameManager();
        using var autosave = new MatchAutosave(manager, store);

        autosave.Restore();

        var state = manager.GetStateSnapshot();
        Assert.True(state.IsGameActive);
        Assert.Equal(GameType.EightBall, state.GameType);
        Assert.Equal(RaceToMode.Split, state.RaceToMode);
        Assert.Equal(3, state.Player1Score);
        Assert.Equal(1, state.Player2Score);
        Assert.Equal(BallGroup.Solids, state.Player1.BallGroup);
        Assert.Equal(BallGroup.Stripes, state.Player2.BallGroup);
        Assert.Equal(new[] { 2, 11 }, state.PocketedBalls.OrderBy(ball => ball));
    }

    [Fact]
    public void Restore_OnAFreshInstall_LeavesSetupAlone()
    {
        var manager = new GameManager();
        using var autosave = new MatchAutosave(manager, new MatchStateStore(_root));

        autosave.Restore();

        Assert.False(manager.GetStateSnapshot().IsGameActive);
    }

    [Fact]
    public void Restore_KeepsAnUnreadableMatchAside_WithoutBlockingStartup()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(MatchPath, "{ half a match");

        var manager = new GameManager();
        using var autosave = new MatchAutosave(manager, new MatchStateStore(_root));

        autosave.Restore();

        // Startup carries on at setup, and the file it could not read is still there to be looked at.
        Assert.False(manager.GetStateSnapshot().IsGameActive);
        Assert.False(File.Exists(MatchPath));
        Assert.True(File.Exists(CorruptMatchPath));
    }

    private static ResumableMatch SavedMatch() => new()
    {
        GameType = GameType.EightBall,
        RaceToMode = RaceToMode.Split,
        HomeScore = 3,
        AwayScore = 1,
        HomeIsCurrentShooter = false,
        HomeBallGroup = BallGroup.Solids,
        AwayBallGroup = BallGroup.Stripes,
        PocketedBalls = new List<int> { 2, 11 }
    };

    private static GameManager StartedGame()
    {
        var manager = new GameManager();
        manager.InitializeGame(GameType.NineBall, RaceToMode.Single);
        return manager;
    }

    private static bool WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition() && DateTime.UtcNow < deadline)
            Thread.Sleep(10);

        return condition();
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

    /// <summary>Counts what the autosave asked of the file, instead of watching a file's mtime.</summary>
    private sealed class CountingStore : IMatchSnapshotStore
    {
        private int _saves;
        private int _deletes;

        public int Saves => Volatile.Read(ref _saves);

        public int Deletes => Volatile.Read(ref _deletes);

        public ResumableMatch? LastSaved { get; private set; }

        public ResumableMatch? Load() => null;

        public bool TrySave(ResumableMatch match)
        {
            LastSaved = match;
            Interlocked.Increment(ref _saves);
            return true;
        }

        public void Delete() => Interlocked.Increment(ref _deletes);
    }
}
