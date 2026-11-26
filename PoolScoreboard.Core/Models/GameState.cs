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
    public CueBallSpin? CueBallSpin { get; set; }

    /// <summary>
    /// A deep-enough copy for handing to subscribers and HTTP responses: players, theme, style,
    /// visibility and the pocketed-ball set are cloned, and <see cref="CurrentShooter"/>
    /// /<see cref="Winner"/> are remapped onto the copied players so reference checks still work.
    /// </summary>
    public GameState Clone()
    {
        var player1 = Player1.Copy();
        var player2 = Player2.Copy();

        return new GameState
        {
            Id = Id,
            Player1 = player1,
            Player2 = player2,
            GameType = GameType,
            RaceToMode = RaceToMode,
            Player1Score = Player1Score,
            Player2Score = Player2Score,
            CurrentShooter = CopyPlayerReference(CurrentShooter, player1, player2),
            IsGameActive = IsGameActive,
            StartTime = StartTime,
            Winner = CopyPlayerReference(Winner, player1, player2),
            ColorTheme = ColorTheme.Copy(),
            PocketedBalls = new HashSet<int>(PocketedBalls),
            ScoreboardStyle = ScoreboardStyle.Copy(),
            Visibility = Visibility.Copy(),
            CueBallSpin = CueBallSpin?.Copy()
        };
    }

    private Player? CopyPlayerReference(Player? source, Player player1Copy, Player player2Copy) =>
        source == null ? null
        : ReferenceEquals(source, Player1) ? player1Copy
        : ReferenceEquals(source, Player2) ? player2Copy
        : null;
}
