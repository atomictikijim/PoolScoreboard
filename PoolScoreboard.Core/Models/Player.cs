using PoolScoreboard.Core.Enums;

namespace PoolScoreboard.Core.Models;

public class Player
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "";
    public string TeamName { get; set; } = "";
    public League League { get; set; }
    public int SkillLevel { get; set; }
}
