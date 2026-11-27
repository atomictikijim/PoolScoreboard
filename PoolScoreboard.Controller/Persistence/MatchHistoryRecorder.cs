using PoolScoreboard.Core;

namespace PoolScoreboard.Controller.Persistence;

/// <summary>
/// Files a match into the history when it finishes, and only then. Extracted from <see cref="App"/> the way
/// <see cref="MatchAutosave"/> was, so the rule that matters — one record per match — is asserted rather than
/// assumed.
/// <para>
/// The trigger is the state's own winner, which stays set while the banner is up: a ball toggled afterwards, a
/// rack reset, or a relaunch restoring the finished match all arrive as further changes to a match that has
/// already been won. Two things stop that becoming a second record. In memory, the match id just filed is
/// remembered, so the common case costs nothing. On disk, <see cref="MatchArchive.Archive"/> refuses an id it
/// already holds, which is what covers a restart, where this object's memory starts empty.
/// </para>
/// <para>
/// Like the autosave, this runs inside <c>GameManager</c>'s lock, often on an HTTP control thread. The write is
/// therefore the only one a match costs — not one per event — and it is a small file the store rewrites whole.
/// </para>
/// </summary>
internal sealed class MatchHistoryRecorder : IDisposable
{
    private readonly GameManager _gameManager;
    private readonly MatchHistoryStore _store;
    private readonly Func<DateTimeOffset> _clock;

    /// <summary>The match this session has filed, so repeated events for it do not reach the disk.</summary>
    private string? _archivedMatchId;

    internal MatchHistoryRecorder(GameManager gameManager, MatchHistoryStore store, Func<DateTimeOffset>? clock = null)
    {
        _gameManager = gameManager;
        _store = store;
        _clock = clock ?? (() => DateTimeOffset.Now);
    }

    /// <summary>
    /// Starts watching, after the match in progress (if any) has been restored: a restored match that already
    /// finished is still there, winner and all, and must not be filed again on the first change it sees.
    /// </summary>
    public void Start() => _gameManager.GameStateChanged += OnGameStateChanged;

    public void Dispose() => _gameManager.GameStateChanged -= OnGameStateChanged;

    private void OnGameStateChanged(object? sender, GameStateChangedEventArgs e)
    {
        var state = e.GameState;
        if (state.Winner is null)
            return;

        if (!string.IsNullOrEmpty(state.MatchId) && state.MatchId == _archivedMatchId)
            return;

        if (_store.TryArchive(state, _clock()) is { } record)
            _archivedMatchId = record.MatchId;
    }
}
