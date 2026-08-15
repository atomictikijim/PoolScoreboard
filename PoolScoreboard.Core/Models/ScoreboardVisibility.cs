namespace PoolScoreboard.Core.Models;

/// <summary>
/// Operator-controlled live show/hide state for the overlay's individually toggleable elements.
/// </summary>
public class ScoreboardVisibility
{
    public bool ScoreBarVisible { get; set; } = true;
    public bool BallTrackerVisible { get; set; } = true;
    public bool WinnerBannerVisible { get; set; } = true;
}
