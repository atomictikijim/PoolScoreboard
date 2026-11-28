using System.Globalization;
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
            HomeText = state.ColorTheme.HomeText,
            AwayText = state.ColorTheme.AwayText
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
        },

        // Round-trip format with the offset, so the page's own clock reads the same instant wherever OBS and the
        // Controller sit. `Date.parse` accepts it; a bare local time would not.
        ShotClockSeconds = state.ShotClockSeconds,
        ShotClockDisplay = state.ShotClockDisplay.ToString(),
        ShotClockEndsAt = state.ShotClockEndsAt?.ToString("o", CultureInfo.InvariantCulture)
    };

    private static PlayerDto ToPlayerDto(Player player, int score) => new()
    {
        Name = player.Name,
        TeamName = player.TeamName,
        Score = score,
        RaceToTarget = player.RaceToTarget,
        BallGroup = player.BallGroup.ToString(),
        EndCapIconUrl = string.IsNullOrEmpty(player.EndCapIcon)
            ? null
            : $"/overlay/api/players/{player.Id}/icon?v={player.EndCapIconVersion}"
    };
}
