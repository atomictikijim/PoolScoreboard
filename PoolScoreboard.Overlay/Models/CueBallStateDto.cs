namespace PoolScoreboard.Overlay.Models;

/// <summary>
/// Wire-format snapshot of the cue-ball contact point sent to the <c>/overlay/cueball</c> page.
/// </summary>
public class CueBallStateDto
{
    public double? X { get; init; }
    public double? Y { get; init; }
}
