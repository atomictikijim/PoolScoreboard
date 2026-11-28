using PoolScoreboard.Core.Enums;
using PoolScoreboard.Core.Models;

namespace PoolScoreboard.Core.Persistence;

/// <summary>
/// What the app remembers between runs: the operator's look for the overlay, and who played last.
/// Kept in Core, with no file access, so the capture/apply mapping is unit-testable; the Controller
/// owns reading and writing it.
/// </summary>
public class OperatorProfile
{
    /// <summary>The overlay port this app has always used, and the one a profile without a usable choice gets.</summary>
    public const int DefaultOverlayPort = 51234;

    /// <summary>Below this Windows needs elevation to bind, so it is not offered to the operator.</summary>
    public const int MinOverlayPort = 1024;

    public const int MaxOverlayPort = 65535;

    public ColorTheme ColorTheme { get; set; } = new();
    public ScoreboardStyle ScoreboardStyle { get; set; } = new();
    public ScoreboardVisibility Visibility { get; set; } = new();

    /// <summary>
    /// How the running clock is drawn — the numbers, or a line that drains. Part of the look rather than of a match,
    /// so it is remembered with the theme and the show/hide settings and comes back on the next launch.
    /// </summary>
    public ShotClockDisplay ShotClockDisplay { get; set; } = ShotClockDisplay.Time;

    /// <summary>
    /// Whether the overlay marks itself when its stream dies. Remembered with the rest of the look, so an operator who
    /// wants the cue does not have to turn it on again every launch.
    /// </summary>
    public bool StaleCueEnabled { get; set; }
    public PlayerProfile HomePlayer { get; set; } = new();
    public PlayerProfile AwayPlayer { get; set; } = new();

    /// <summary>
    /// The port the overlay and the Stream Deck control API are served on. Saved here because it is the
    /// operator's choice for this machine rather than part of a match — the reason it is editable at all is that
    /// another program holding the usual port used to end the overlay for the whole session.
    /// </summary>
    public int OverlayPort { get; set; } = DefaultOverlayPort;

    /// <summary>Whether a port is one the overlay can be bound to without elevation.</summary>
    public static bool IsUsablePort(int port) => port is >= MinOverlayPort and <= MaxOverlayPort;

    /// <summary>A saved or typed port when it can be bound, and the default when it cannot.</summary>
    public static int NormalizePort(int port) => IsUsablePort(port) ? port : DefaultOverlayPort;

    /// <summary>
    /// Reads the parts of a state snapshot worth keeping. Scores, ball groups, pocketed balls and the
    /// current shooter are deliberately left out: they belong to one match, not to the setup. The overlay port is
    /// not in the snapshot either — whoever owns the host fills it in before saving.
    /// </summary>
    public static OperatorProfile Capture(GameState state) => new()
    {
        ColorTheme = state.ColorTheme.Copy(),
        ScoreboardStyle = state.ScoreboardStyle.Copy(),
        Visibility = state.Visibility.Copy(),
        ShotClockDisplay = state.ShotClockDisplay,
        StaleCueEnabled = state.StaleCueEnabled,
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
        manager.SetShotClockDisplay(ShotClockDisplay);
        manager.SetStaleCue(StaleCueEnabled);

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
