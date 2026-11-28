namespace PoolScoreboard.Overlay.Models;

/// <summary>
/// Wire-format snapshot of game state sent to the overlay pages. Kept separate from
/// <c>PoolScoreboard.Core.Models.GameState</c> so the overlay's JSON contract can evolve
/// independently of the internal game model.
/// </summary>
public class ScoreboardStateDto
{
    public required string GameType { get; init; }
    public required string RaceToMode { get; init; }
    public required PlayerDto Home { get; init; }
    public required PlayerDto Away { get; init; }
    public bool HomeIsCurrentShooter { get; init; }
    public bool IsGameActive { get; init; }
    public string? WinnerName { get; init; }
    public bool WinnerIsHome { get; init; }
    public required IReadOnlyList<int> PocketedBalls { get; init; }
    public required ColorThemeDto Colors { get; init; }
    public required ScoreboardStyleDto Style { get; init; }
    public required ScoreboardVisibilityDto Visibility { get; init; }

    /// <summary>How long a shot gets, so the clock can still say what it is counting down from.</summary>
    public int ShotClockSeconds { get; init; }

    /// <summary>
    /// How to draw the countdown: <c>Time</c> for the numbers, <c>Line</c> for a line that drains and turns red as
    /// the shot runs out. The page draws both; this is which one the operator asked for.
    /// </summary>
    public required string ShotClockDisplay { get; init; }

    /// <summary>
    /// When the current shot's clock runs out, as an ISO 8601 instant with its offset, or null when no clock is
    /// showing. A deadline rather than a remaining-second count, because the page counts down on its own between
    /// frames — exactly one frame is sent when the clock starts, not forty.
    /// </summary>
    public string? ShotClockEndsAt { get; init; }

    /// <summary>
    /// True while the clock is on screen at its full length without counting — the state a new rack leaves it in,
    /// waiting for the operator to start it. Mutually exclusive with <see cref="ShotClockEndsAt"/>.
    /// </summary>
    public bool ShotClockArmed { get; init; }

    /// <summary>
    /// Seconds left in the shot while the operator has the clock paused, or null when it is not paused. The page
    /// draws that time once and stops there — no counting — and badges it, because a frozen clock and an expired one
    /// would otherwise look identical from across a room. Mutually exclusive with the two above.
    /// </summary>
    public double? ShotClockPausedSeconds { get; init; }

    /// <summary>
    /// True when the operator asked the page to mark itself once its stream has been dead for a few seconds. The page
    /// keeps its last frame when the host dies, so without this a dead overlay shows confident, frozen numbers.
    /// </summary>
    public bool StaleCueEnabled { get; init; }
}

public class PlayerDto
{
    public required string Name { get; init; }
    public required string TeamName { get; init; }
    public int Score { get; init; }
    public int RaceToTarget { get; init; }
    public required string BallGroup { get; init; }

    /// <summary>
    /// Relative URL of this player's end-cap icon (team logo / flag), or null when none is set.
    /// A reference rather than the image itself: the bytes are served by
    /// <c>/overlay/api/players/{id}/icon</c> so a multi-megabyte base64 logo isn't re-sent inside
    /// every SSE snapshot. The <c>v</c> query changes with the artwork, which cache-busts the
    /// image only when the operator actually picks a new one.
    /// </summary>
    public string? EndCapIconUrl { get; init; }
}

public class ColorThemeDto
{
    public required string HomeBackground { get; init; }
    public required string AwayBackground { get; init; }
    public required string HomeAccent { get; init; }
    public required string AwayAccent { get; init; }
    public required string HomeText { get; init; }
    public required string AwayText { get; init; }
}

public class ScoreboardStyleDto
{
    public int CornerRoundness { get; init; }
    public int OverallScale { get; init; }
    public bool GlossyFinish { get; init; }
    public required string EndCapStyle { get; init; }
    public required string ShooterIndicatorStyle { get; init; }
}

public class ScoreboardVisibilityDto
{
    public bool ScoreBarVisible { get; init; }
    public bool BallTrackerVisible { get; init; }
    public bool WinnerBannerVisible { get; init; }
}
