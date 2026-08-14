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
    public required IReadOnlyList<int> PocketedBalls { get; init; }
    public required ColorThemeDto Colors { get; init; }
}

public class PlayerDto
{
    public required string Name { get; init; }
    public required string TeamName { get; init; }
    public int Score { get; init; }
    public int RaceToTarget { get; init; }
    public required string BallGroup { get; init; }
}

public class ColorThemeDto
{
    public required string Background { get; init; }
    public required string Accent { get; init; }
    public required string Text { get; init; }
}
