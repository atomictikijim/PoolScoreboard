using CommunityToolkit.Mvvm.ComponentModel;
using PoolScoreboard.Core;
using PoolScoreboard.Core.Models;

namespace PoolScoreboard.Controller.ViewModels;

/// <summary>
/// Match-setup fields for one player. The values are written into GameManager's state as they are
/// typed (see <see cref="GameManager.SetPlayerDetails" />) rather than into a <c>Player</c> this
/// screen owns, so the preview the overlay renders and the match that starts from these fields are
/// the same lock-protected players. Live-game state (score, ball group, current shooter) lives on
/// <see cref="GameViewModel"/>, mirrored from the authoritative GameManager once the match starts.
/// </summary>
public partial class PlayerViewModel : ObservableObject
{
    private readonly GameManager _gameManager;
    private readonly bool _isHome;
    private readonly Action? _persist;

    /// <summary>Suppresses the push-to-state handlers while the constructor seeds restored values.</summary>
    private bool _restoring;

    [ObservableProperty]
    private string teamName = string.Empty;

    [ObservableProperty]
    private string playerName = string.Empty;

    [ObservableProperty]
    private int raceToTarget = 5;

    [ObservableProperty]
    private string? endCapIconDataUri;

    public PlayerViewModel(GameManager gameManager, bool isHome, Player? restored = null, Action? persist = null)
    {
        _gameManager = gameManager;
        _isHome = isHome;
        _persist = persist;

        // These values are already in GameManager (restored from the saved profile), so the pushes are
        // suppressed rather than re-raised as no-op mutations.
        if (restored != null)
        {
            _restoring = true;
            try
            {
                TeamName = restored.TeamName;
                PlayerName = restored.Name;
                RaceToTarget = restored.RaceToTarget;
                EndCapIconDataUri = restored.EndCapIcon;
            }
            finally
            {
                _restoring = false;
            }
        }
    }

    partial void OnTeamNameChanged(string value) => PushToState();

    partial void OnPlayerNameChanged(string value) => PushToState();

    partial void OnRaceToTargetChanged(int value) => PushToState();

    partial void OnEndCapIconDataUriChanged(string? value) => PushToState();

    // Each edit re-sends all four fields; GameManager writes only what actually changed (the icon),
    // so re-sending the untouched ones doesn't churn the overlay's cached artwork.
    private void PushToState()
    {
        if (_restoring)
            return;

        _gameManager.SetPlayerDetails(_isHome, PlayerName, TeamName, RaceToTarget, EndCapIconDataUri);
        _persist?.Invoke();
    }
}
