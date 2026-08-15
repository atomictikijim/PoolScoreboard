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
        WinnerIsHome = state.Winner != null && ReferenceEquals(state.Winner, state.Player1),
        PocketedBalls = state.PocketedBalls.ToList(),
        Colors = new ColorThemeDto
        {
            HomeBackground = state.ColorTheme.HomeBackground,
            AwayBackground = state.ColorTheme.AwayBackground,
            HomeAccent = state.ColorTheme.HomeAccent,
            AwayAccent = state.ColorTheme.AwayAccent,
            Text = state.ColorTheme.Text
        },
        Style = new ScoreboardStyleDto
        {
            CornerRoundness = state.ScoreboardStyle.CornerRoundness,
            OverallScale = state.ScoreboardStyle.OverallScale,
            GlossyFinish = state.ScoreboardStyle.GlossyFinish,
            EndCapStyle = state.ScoreboardStyle.EndCapStyle.ToString(),
            ShooterIndicatorStyle = state.ScoreboardStyle.ShooterIndicatorStyle.ToString()
        },
        Visibility = new ScoreboardVisibilityDto
        {
            ScoreBarVisible = state.Visibility.ScoreBarVisible,
            BallTrackerVisible = state.Visibility.BallTrackerVisible,
            WinnerBannerVisible = state.Visibility.WinnerBannerVisible
        }
    };

    private static PlayerDto ToPlayerDto(Player player, int score) => new()
    {
        Name = player.Name,
        TeamName = player.TeamName,
        Score = score,
        RaceToTarget = player.RaceToTarget,
        BallGroup = player.BallGroup.ToString(),
        EndCapIcon = player.EndCapIcon
    };
}
