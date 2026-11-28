using PoolScoreboard.Core.Enums;
using PoolScoreboard.Core.Models;

namespace PoolScoreboard.Core.Persistence;

/// <summary>
/// One named setup: the look, the four per-match choices that otherwise have to be retyped for every venue, and the
/// overlay port. The profile remembers the look for the whole app, but the game type, Race-To mode and both shot-clock
/// lengths are deliberately not in it — they belong to the match being set up — so an operator running two leagues
/// used to retype them, and their colours, at the start of every night.
/// <para>
/// The players are not in here. A preset describes how a venue is dressed and how a match is timed, not who is playing;
/// the roster already answers that, and baking names into a preset would hand the next player the last one's slot.
/// </para>
/// <para>
/// Kept in Core with no file access, so capture and the library rules are unit-testable; the Controller owns reading
/// and writing <c>presets.json</c>.
/// </para>
/// </summary>
public class SetupPreset
{
    /// <summary>The name the operator saves it under, trimmed. What a preset is looked up and replaced by.</summary>
    public string Name { get; set; } = string.Empty;

    public GameType GameType { get; set; } = GameType.NineBall;

    public RaceToMode RaceToMode { get; set; } = RaceToMode.Single;

    /// <summary>How long a shot gets under this preset, 5-300 seconds.</summary>
    public int ShotClockSeconds { get; set; } = GameState.DefaultShotClockSeconds;

    /// <summary>What calling an extension adds under this preset, 5-300 seconds.</summary>
    public int ShotClockExtensionSeconds { get; set; } = GameState.DefaultShotClockExtensionSeconds;

    /// <summary>
    /// The port this venue's overlay is served on. A preset rather than only a profile setting because the second
    /// venue is where something else is already sitting on 51234.
    /// </summary>
    public int OverlayPort { get; set; } = OperatorProfile.DefaultOverlayPort;

    public ColorTheme ColorTheme { get; set; } = new();

    public ScoreboardStyle ScoreboardStyle { get; set; } = new();

    public ScoreboardVisibility Visibility { get; set; } = new();

    public ShotClockDisplay ShotClockDisplay { get; set; } = ShotClockDisplay.Time;

    /// <summary>Whether the overlay marks itself when its stream dies, as the operator left it under this preset.</summary>
    public bool StaleCueEnabled { get; set; }

    /// <summary>
    /// Reads the setup on screen into a preset. Everything is copied — the theme, style and visibility are cloned the
    /// way a state snapshot clones them — so a preset saved tonight cannot change when the operator edits the colours
    /// afterwards. A port that could not be bound becomes the default, because a preset that cannot serve is worse
    /// than one that serves on the usual port.
    /// </summary>
    public static SetupPreset Capture(string name, GameState state, int overlayPort) => new()
    {
        Name = NormalizeName(name),
        GameType = state.GameType,
        RaceToMode = state.RaceToMode,
        ShotClockSeconds = state.ShotClockSeconds,
        ShotClockExtensionSeconds = state.ShotClockExtensionSeconds,
        OverlayPort = OperatorProfile.NormalizePort(overlayPort),
        ColorTheme = state.ColorTheme.Copy(),
        ScoreboardStyle = state.ScoreboardStyle.Copy(),
        Visibility = state.Visibility.Copy(),
        ShotClockDisplay = state.ShotClockDisplay,
        StaleCueEnabled = state.StaleCueEnabled
    };

    /// <summary>
    /// A preset's name as it is stored and matched: trimmed. A blank name is not a preset that can be saved, which is
    /// what lets the save button be disabled rather than writing an unnamed entry nobody can pick.
    /// </summary>
    public static string NormalizeName(string? name) => (name ?? string.Empty).Trim();

    /// <summary>
    /// Writes the whole preset into game state through <see cref="GameManager"/>'s own setters — the single route
    /// <see cref="OperatorProfile.ApplyTo"/> uses — so no setting reaches state by a way the screen's own edits do not,
    /// and what the overlay reads after a preset is exactly what the screen shows.
    /// <para>
    /// The three setup-only fields (game type, Race-To mode and both lengths) are ignored by <see cref="GameManager"/>
    /// while a match is running, which is what makes applying a preset mid-match safe: the look changes, the match does
    /// not. The overlay port is deliberately not here — it belongs to the host session, not to game state.
    /// </para>
    /// </summary>
    public void ApplyTo(GameManager manager)
    {
        manager.SetMatchPreview(GameType, RaceToMode);
        manager.SetShotClockLengths(ShotClockSeconds, ShotClockExtensionSeconds);
        manager.SetColorTheme(ColorTheme);
        manager.SetScoreboardStyle(ScoreboardStyle);
        manager.SetScoreBarVisible(Visibility.ScoreBarVisible);
        manager.SetBallTrackerVisible(Visibility.BallTrackerVisible);
        manager.SetWinnerBannerVisible(Visibility.WinnerBannerVisible);
        manager.SetShotClockDisplay(ShotClockDisplay);
        manager.SetStaleCue(StaleCueEnabled);
    }

}
