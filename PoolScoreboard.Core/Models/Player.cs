using PoolScoreboard.Core.Enums;

namespace PoolScoreboard.Core.Models;

public class Player
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "";
    public string TeamName { get; set; } = "";
    public int RaceToTarget { get; set; } = 5;
    public BallGroup BallGroup { get; set; } = BallGroup.Unassigned;

    /// <summary>
    /// Optional end-cap image (team logo, country flag, etc.) as a data URI. Null means the
    /// overlay falls back to the accent-colored dot.
    /// </summary>
    public string? EndCapIcon { get; set; }
}
