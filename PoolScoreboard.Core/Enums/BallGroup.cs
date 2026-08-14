namespace PoolScoreboard.Core.Enums;

/// <summary>
/// Which group of balls a player is shooting in 8-ball. Not meaningful for 9-ball/10-ball,
/// and never assigned to the 8-ball itself.
/// </summary>
public enum BallGroup
{
    Unassigned,
    Solids,
    Stripes
}
