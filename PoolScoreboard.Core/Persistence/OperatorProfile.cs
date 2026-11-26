using PoolScoreboard.Core.Models;

namespace PoolScoreboard.Core.Persistence;

/// <summary>
/// What the app remembers between runs: the operator's look for the overlay, and who played last.
/// Kept in Core, with no file access, so the capture/apply mapping is unit-testable; the Controller
/// owns reading and writing it.
/// </summary>
public class OperatorProfile
{
    public ColorTheme ColorTheme { get; set; } = new();
    public ScoreboardStyle ScoreboardStyle { get; set; } = new();
    public ScoreboardVisibility Visibility { get; set; } = new();
    public PlayerProfile HomePlayer { get; set; } = new();
    public PlayerProfile AwayPlayer { get; set; } = new();

    /// <summary>
    /// Reads the parts of a state snapshot worth keeping. Scores, ball groups, pocketed balls and the
    /// current shooter are deliberately left out: they belong to one match, not to the setup.
    /// </summary>
    public static OperatorProfile Capture(GameState state) => new()
    {
        ColorTheme = state.ColorTheme.Copy(),
        ScoreboardStyle = state.ScoreboardStyle.Copy(),
        Visibility = state.Visibility.Copy(),
        HomePlayer = PlayerProfile.From(state.Player1),
        AwayPlayer = PlayerProfile.From(state.Player2)
    };

    /// <summary>
    /// Writes the profile back through <see cref="GameManager"/>'s normal mutation paths, so the
    /// overlay sees these values and a caller never edits state around the lock. End-cap icons are
    /// passed separately because they are stored as files by whoever owns the disk, not in here.
    /// </summary>
    public void ApplyTo(GameManager manager, string? homeEndCapIcon = null, string? awayEndCapIcon = null)
    {
        manager.SetColorTheme(ColorTheme);
        manager.SetScoreboardStyle(ScoreboardStyle);
        manager.SetScoreBarVisible(Visibility.ScoreBarVisible);
        manager.SetBallTrackerVisible(Visibility.BallTrackerVisible);
        manager.SetWinnerBannerVisible(Visibility.WinnerBannerVisible);

        HomePlayer.ApplyTo(manager, isHome: true, homeEndCapIcon);
        AwayPlayer.ApplyTo(manager, isHome: false, awayEndCapIcon);
    }
}

/// <summary>One side's Match Setup fields, as remembered between runs.</summary>
public class PlayerProfile
{
    public string Name { get; set; } = string.Empty;
    public string TeamName { get; set; } = string.Empty;
    public int RaceToTarget { get; set; } = 5;

    public static PlayerProfile From(Player player) => new()
    {
        Name = player.Name,
        TeamName = player.TeamName,
        RaceToTarget = player.RaceToTarget
    };

    public void ApplyTo(GameManager manager, bool isHome, string? endCapIcon) =>
        manager.SetPlayerDetails(isHome, Name, TeamName, RaceToTarget, endCapIcon);
}
