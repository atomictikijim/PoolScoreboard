using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PoolScoreboard.Core.Enums;
using PoolScoreboard.Core.Models;

namespace PoolScoreboard.Controller.ViewModels;

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
    private BallGroup ballGroup = BallGroup.Unassigned;

    [ObservableProperty]
    private int score = 0;

    public PlayerViewModel()
    {
        _player = new Player();
    }

    partial void OnTeamNameChanged(string value) => _player.TeamName = value;

    partial void OnPlayerNameChanged(string value) => _player.Name = value;

    partial void OnRaceToTargetChanged(int value) => _player.RaceToTarget = value;

    partial void OnBallGroupChanged(BallGroup value) => _player.BallGroup = value;

    [RelayCommand]
    public void IncrementScore()
    {
        Score++;
    }

    [RelayCommand]
    public void DecrementScore()
    {
        if (Score > 0)
            Score--;
    }

    public Player GetPlayer() => _player;
}
