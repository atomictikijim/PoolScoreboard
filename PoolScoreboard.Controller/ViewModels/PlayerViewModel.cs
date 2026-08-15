using CommunityToolkit.Mvvm.ComponentModel;
using PoolScoreboard.Core.Models;

namespace PoolScoreboard.Controller.ViewModels;

/// <summary>
/// Match-setup fields for one player. Live-game state (score, ball group, current shooter)
/// lives on <see cref="GameViewModel"/>, mirrored from the authoritative GameManager once the
/// match starts.
/// </summary>
public partial class PlayerViewModel : ObservableObject
{
    private readonly Player _player;

    [ObservableProperty]
    private string teamName = string.Empty;

    [ObservableProperty]
    private string playerName = string.Empty;

    [ObservableProperty]
    private int raceToTarget = 5;

    [ObservableProperty]
    private string? endCapIconDataUri;

    public PlayerViewModel()
    {
        _player = new Player();
    }

    partial void OnTeamNameChanged(string value) => _player.TeamName = value;

    partial void OnPlayerNameChanged(string value) => _player.Name = value;

    partial void OnRaceToTargetChanged(int value) => _player.RaceToTarget = value;

    partial void OnEndCapIconDataUriChanged(string? value) => _player.EndCapIcon = value;

    public Player GetPlayer() => _player;
}
