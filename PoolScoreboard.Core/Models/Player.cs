using PoolScoreboard.Core.Enums;

namespace PoolScoreboard.Core.Models;

public class Player
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "";
    public string TeamName { get; set; } = "";
    public int RaceToTarget { get; set; } = 5;
    public BallGroup BallGroup { get; set; } = BallGroup.Unassigned;
}
