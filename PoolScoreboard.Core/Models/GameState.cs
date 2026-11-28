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

    /// <summary>
    /// The ball that ends the rack and that the display is really counting towards: the 8 in 8-ball, the 9 in
    /// 9-ball, the 10 in 10-ball. Derived from <see cref="GameType"/> rather than stored, so the ball display and
    /// the shot clock cannot disagree about which ball it is.
    /// </summary>
    public int MoneyBallNumber => GameType switch
    {
        GameType.EightBall => 8,
        GameType.NineBall => 9,
        GameType.TenBall => 10,
        _ => 0
    };
    public ScoreboardStyle ScoreboardStyle { get; set; } = new();
    public ScoreboardVisibility Visibility { get; set; } = new();
    public CueBallSpin? CueBallSpin { get; set; }

    /// <summary>How the countdown is drawn while a clock is showing — numbers, or a line that drains.</summary>
    public ShotClockDisplay ShotClockDisplay { get; set; } = ShotClockDisplay.Time;

    /// <summary>
    /// Whether the overlay should say so when its stream has been dead for a few seconds. Off until the operator asks
    /// for it, because a mark on the overlay is their call; part of the look rather than the match, so it is carried
    /// and saved with the theme and the show/hide settings.
    /// </summary>
    public bool StaleCueEnabled { get; set; }

    /// <summary>How long a shot gets until Match Setup says otherwise.</summary>
    public const int DefaultShotClockSeconds = 40;

    /// <summary>What calling an extension adds, until Match Setup says otherwise.</summary>
    public const int DefaultShotClockExtensionSeconds = 30;

    /// <summary>
    /// What Match Setup accepts for either length, in seconds. Clamped rather than refused there, so a slipped
    /// digit still leaves a usable clock instead of a match nobody can time.
    /// </summary>
    public const int MinShotClockSeconds = 5;

    public const int MaxShotClockSeconds = 300;

    /// <summary>How long each shot gets, which is what the overlay counts down from.</summary>
    public int ShotClockSeconds { get; set; } = DefaultShotClockSeconds;

    /// <summary>How much time calling an extension adds to the shot the player is on.</summary>
    public int ShotClockExtensionSeconds { get; set; } = DefaultShotClockExtensionSeconds;

    /// <summary>
    /// Whether each side has already called their one extension this rack. Flags on the state rather than a count
    /// kept beside it because a rack ends in several ways and every one of them has to hand the extension back:
    /// they live and die with the state a new rack or a new match replaces.
    /// </summary>
    public bool HomeShotClockExtensionUsed { get; set; }

    public bool AwayShotClockExtensionUsed { get; set; }

    /// <summary>
    /// When the current shot's countdown runs out, or null when no clock is showing. A deadline rather than a
    /// remaining-second count: the overlay must keep ticking between state changes, and neither the Controller nor
    /// the overlay host should have to send a frame every second to make that happen. Null is also how the clock
    /// disappears — a new rack and a reset both clear it.
    /// </summary>
    public DateTimeOffset? ShotClockEndsAt { get; set; }

    /// <summary>
    /// True while the clock is on screen at its full length but not counting — what a new rack leaves behind, so
    /// the allowance is visible before the break and the operator starts it by hand. Mutually exclusive with
    /// <see cref="ShotClockEndsAt"/> by construction: starting clears this, and clearing, a reset or a won match
    /// leave neither, which is how the clock disappears.
    /// </summary>
    public bool ShotClockArmed { get; set; }

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
            ShotClockDisplay = ShotClockDisplay,
            StaleCueEnabled = StaleCueEnabled,
            CueBallSpin = CueBallSpin?.Copy(),
            ShotClockSeconds = ShotClockSeconds,
            ShotClockExtensionSeconds = ShotClockExtensionSeconds,
            HomeShotClockExtensionUsed = HomeShotClockExtensionUsed,
            AwayShotClockExtensionUsed = AwayShotClockExtensionUsed,
            ShotClockEndsAt = ShotClockEndsAt,
            ShotClockArmed = ShotClockArmed
        };
    }
}
