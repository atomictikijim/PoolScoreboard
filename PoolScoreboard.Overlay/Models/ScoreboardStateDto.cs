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
}

public class PlayerDto
{
    public required string Name { get; init; }
    public required string TeamName { get; init; }
    public int Score { get; init; }
    public int RaceToTarget { get; init; }
    public required string BallGroup { get; init; }
    public string? EndCapIcon { get; init; }
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
