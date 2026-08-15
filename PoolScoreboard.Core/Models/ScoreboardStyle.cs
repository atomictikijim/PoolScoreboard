using PoolScoreboard.Core.Enums;

namespace PoolScoreboard.Core.Models;

/// <summary>
/// Operator-set visual styling for the overlay's score bar. Defaults reproduce the overlay's
/// original hardcoded look, so an unstyled/pre-match <see cref="GameState"/> renders unchanged.
/// </summary>
public class ScoreboardStyle
{
    public int CornerRoundness { get; set; } = 100;
    public int OverallScale { get; set; } = 100;
    public bool GlossyFinish { get; set; } = true;
    public EndCapStyle EndCapStyle { get; set; } = EndCapStyle.Dot;
    public ShooterIndicatorStyle ShooterIndicatorStyle { get; set; } = ShooterIndicatorStyle.Glow;
}
