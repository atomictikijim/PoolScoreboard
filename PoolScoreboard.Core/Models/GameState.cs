using PoolScoreboard.Core.Enums;

namespace PoolScoreboard.Core.Models;

public class GameState
{
    public Player Player1 { get; set; } = new();
    public Player Player2 { get; set; } = new();
    public GameType GameType { get; set; }
    public RaceToMode RaceToMode { get; set; }
    public int Player1Score { get; set; }
    public int Player2Score { get; set; }
    public Player? CurrentShooter { get; set; }

    /// <summary>
    /// What tells one match from the next. Assigned when a match starts, kept across a restart, and carried
    /// into the match history, so the same match — whose winning state stays on screen, and which a relaunch
    /// can restore — is not recorded twice while a rematch, even one that ends with the same names and score,
    /// still is. Empty at setup, where there is no match.
    /// </summary>
    public string MatchId { get; set; } = string.Empty;

    public bool IsGameActive { get; set; }
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
            Player1 = player1,
            Player2 = player2,
            GameType = GameType,
            RaceToMode = RaceToMode,
            Player1Score = Player1Score,
            Player2Score = Player2Score,
            CurrentShooter = CurrentShooter == Player1 ? player1 : CurrentShooter == Player2 ? player2 : null,
            MatchId = MatchId,
            IsGameActive = IsGameActive,
            Winner = Winner == Player1 ? player1 : Winner == Player2 ? player2 : null,
            ColorTheme = ColorTheme.Copy(),
            PocketedBalls = new HashSet<int>(PocketedBalls),
            ScoreboardStyle = ScoreboardStyle.Copy(),
            Visibility = Visibility.Copy(),
            CueBallSpin = CueBallSpin?.Copy()
        };
    }
}
