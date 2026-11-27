using System.Threading;
using PoolScoreboard.Core;
using PoolScoreboard.Core.Persistence;

namespace PoolScoreboard.Controller.Persistence;

/// <summary>
/// Keeps <c>match.json</c> in step with the match in progress, so a crash or a forced restart mid-match costs
/// nothing but the last fraction of a second. Extracted from <see cref="App"/> so the parts that were only ever
/// proven by hand — the debounce, the delete-on-reset rule, resume and the unreadable file — can be asserted.
/// <para>
/// Every <see cref="GameManager.GameStateChanged"/> only re-arms a pool timer, never writing: the event arrives
/// inside <c>GameManager</c>'s lock, often from an HTTP control thread, so neither the UI thread nor the caller's
/// may do the write. A burst of points therefore costs one small write, and a state back at setup deletes the file
/// instead, so a reset or finished match is not resumed.
/// </para>
/// </summary>
internal sealed class MatchAutosave : IDisposable
{
    /// <summary>How long a burst of changes is allowed to settle before the snapshot is written.</summary>
    internal static readonly TimeSpan DefaultDelay = TimeSpan.FromMilliseconds(500);

    private readonly GameManager _gameManager;
    private readonly IMatchSnapshotStore _store;
    private readonly TimeSpan _delay;
    private readonly Timer _timer;

    internal MatchAutosave(GameManager gameManager, IMatchSnapshotStore store, TimeSpan? delay = null)
    {
        _gameManager = gameManager;
        _store = store;
        _delay = delay ?? DefaultDelay;
        _timer = new Timer(_ => SaveNow(), null, Timeout.Infinite, Timeout.Infinite);
    }

    /// <summary>
    /// Resumes the saved match, if there is one, before any window reads state, and starts watching for changes from
    /// there. Called at startup after the operator profile, which supplies the players and Race-To targets the scores
    /// are judged against.
    /// <para>
    /// Watching deliberately starts here rather than in the constructor: applying that profile fires the state event
    /// several times while the match is still at setup, and a snapshot taken then deletes the file this is about to
    /// read — a resume would be lost to its own startup if the timer got to run first.
    /// </para>
    /// </summary>
    public void Restore()
    {
        if (_store.Load() is { } match)
            _gameManager.RestoreMatch(match);

        _gameManager.GameStateChanged += OnGameStateChanged;
    }

    /// <summary>
    /// Writes the current snapshot, or deletes the file when the state is back at setup (a reset, or a fresh
    /// launch), so the next start resumes nothing. Called by the debounce timer and again on exit, where the last
    /// point scored has to survive even though the debounce has not run.
    /// </summary>
    public void SaveNow()
    {
        if (ResumableMatch.Capture(_gameManager.GetStateSnapshot()) is { } match)
            _store.TrySave(match);
        else
            _store.Delete();
    }

    private void OnGameStateChanged(object? sender, GameStateChangedEventArgs e)
    {
        try
        {
            _timer.Change(_delay, Timeout.InfiniteTimeSpan);
        }
        catch (ObjectDisposedException)
        {
            // A change that lands while the app is shutting down; there is nothing left to save it for.
        }
    }

    public void Dispose()
    {
        _gameManager.GameStateChanged -= OnGameStateChanged;
        _timer.Dispose();
    }
}
