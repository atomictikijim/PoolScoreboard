using PoolScoreboard.Core.Models;

namespace PoolScoreboard.Overlay.Models;

public static class ScoreboardStateMapper
{
    public static ScoreboardStateDto ToDto(GameState state) => new()
    {
        GameType = state.GameType.ToString(),
        RaceToMode = state.RaceToMode.ToString(),
        Home = ToPlayerDto(state.Player1, state.Player1Score),
        Away = ToPlayerDto(state.Player2, state.Player2Score),
        HomeIsCurrentShooter = ReferenceEquals(state.CurrentShooter, state.Player1),
        IsGameActive = state.IsGameActive,
        WinnerName = state.Winner?.Name,
        PocketedBalls = state.PocketedBalls.ToList(),
        Colors = new ColorThemeDto
        {
            Background = state.ColorTheme.Background,
            Accent = state.ColorTheme.Accent,
            Text = state.ColorTheme.Text
        }
    };

    private static PlayerDto ToPlayerDto(Player player, int score) => new()
    {
        Name = player.Name,
        TeamName = player.TeamName,
        Score = score,
        RaceToTarget = player.RaceToTarget,
        BallGroup = player.BallGroup.ToString()
    };
}
