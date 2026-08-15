using PoolScoreboard.Core.Enums;

namespace PoolScoreboard.Core.Models;

public class GameState
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public Player Player1 { get; set; } = new();
    public Player Player2 { get; set; } = new();
    public GameType GameType { get; set; }
    public RaceToMode RaceToMode { get; set; }
    public int Player1Score { get; set; }
    public int Player2Score { get; set; }
    public Player? CurrentShooter { get; set; }
    public bool IsGameActive { get; set; }
    public DateTime StartTime { get; set; }
    public Player? Winner { get; set; }
    public ColorTheme ColorTheme { get; set; } = new();
    public HashSet<int> PocketedBalls { get; set; } = new();
    public ScoreboardStyle ScoreboardStyle { get; set; } = new();
    public ScoreboardVisibility Visibility { get; set; } = new();
}
