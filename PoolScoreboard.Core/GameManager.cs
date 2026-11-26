using PoolScoreboard.Core.Enums;
using PoolScoreboard.Core.Models;

namespace PoolScoreboard.Core;

/// <summary>
/// Orchestrator for all game state. Mutations can arrive from different threads — the WPF UI, its
/// keyboard shortcuts and the local HTTP control API the Stream Deck drives all call into the same
/// instance — so every mutation runs under one internal lock. <see cref="GameStateChanged"/> is
/// raised inside that lock with a snapshot copied at mutation time, which is what keeps deliveries
/// in mutation order: a later change can't notify subscribers before an earlier one, so the UI and
/// the overlay streams never apply a stale snapshot last. Subscribers must therefore not block;
/// the current ones only marshal to a dispatcher or enqueue for an SSE stream.
/// </summary>
public class GameManager
{
    private readonly object _sync = new();

    private GameState _currentGame = new();

    /// <summary>
    /// Which player scored each point, in order, so <see cref="UndoPoint"/> can reverse the most
    /// recent point rather than inferring it from the scores. Internal bookkeeping only — not part
    /// of <see cref="GameState"/>/the overlay payload — and cleared whenever a match starts or is
    /// reset.
    /// </summary>
    private readonly List<bool> _pointHistory = new();

    public event EventHandler<GameStateChangedEventArgs>? GameStateChanged;

    /// <summary>
    /// Starts the match with the two players already in state — the ones the setup screen has been
    /// writing through <see cref="SetPlayerDetails" />, so the screen never holds a copy of them.
    /// </summary>
    public void InitializeGame(GameType gameType, RaceToMode raceToMode)
    {
        lock (_sync)
        {
            _pointHistory.Clear();

            var next = new GameState
            {
                Player1 = _currentGame.Player1,
                Player2 = _currentGame.Player2,
                GameType = gameType,
                RaceToMode = raceToMode,
                CurrentShooter = _currentGame.Player1,
                IsGameActive = true
            };
            CarryOperatorStyling(_currentGame, next);

            _currentGame = next;
            RaiseGameStateChanged(_currentGame.Clone());
        }
    }

    /// <summary>
    /// Moves the operator's look — colour theme, scoreboard style, show/hide settings — onto a new
    /// state, as copies. Both ways of leaving a match (starting one and resetting one) keep these,
    /// because they describe the show rather than the score, and the HTTP control API paths have no
    /// screen behind them to re-apply anything.
    /// </summary>
    private static void CarryOperatorStyling(GameState from, GameState to)
    {
        to.ColorTheme = from.ColorTheme.Copy();
        to.ScoreboardStyle = from.ScoreboardStyle.Copy();
        to.Visibility = from.Visibility.Copy();
    }

    public void AddPoint(bool isPlayer1)
    {
        lock (_sync)
        {
            if (!_currentGame.IsGameActive)
                return;

            _currentGame.CueBallSpin = null;

            if (isPlayer1)
            {
                _currentGame.Player1Score++;
                _pointHistory.Add(true);
                if (_currentGame.Player1Score >= _currentGame.Player1.RaceToTarget)
                    EndGame(_currentGame.Player1);
            }
            else
            {
                _currentGame.Player2Score++;
                _pointHistory.Add(false);
                if (_currentGame.Player2Score >= _currentGame.Player2.RaceToTarget)
                    EndGame(_currentGame.Player2);
            }

            RaiseGameStateChanged(_currentGame.Clone());
        }
    }

    /// <summary>
    /// Mirrors the setup screen's game type and Race-To mode into the preview state. It takes no
    /// players because the screen writes those into state itself, under this same lock, through
    /// <see cref="SetPlayerDetails" />.
    /// </summary>
    public void SetMatchPreview(GameType gameType, RaceToMode raceToMode)
    {
        lock (_sync)
        {
            if (_currentGame.IsGameActive)
                return;

            _currentGame.GameType = gameType;
            _currentGame.RaceToMode = raceToMode;

            RaiseGameStateChanged(_currentGame.Clone());
        }
    }

    /// <summary>
    /// Writes one side's Match Setup fields — name, team, Race-To target and end-cap icon — into the
    /// state the overlay previews. The setup screen goes through here instead of holding a
    /// <see cref="Player" /> of its own, so these writes take the same lock as every other mutation
    /// and a snapshot being serialized for an overlay never catches a half-updated player (a new
    /// icon version paired with the previous artwork, say).
    /// </summary>
    public void SetPlayerDetails(bool isHome, string name, string teamName, int raceToTarget, string? endCapIcon)
    {
        lock (_sync)
        {
            var player = isHome ? _currentGame.Player1 : _currentGame.Player2;

            player.Name = name;
            player.TeamName = teamName;
            player.RaceToTarget = raceToTarget;

            // Only a real artwork change counts as one: assigning the icon on every keystroke would
            // bump EndCapIconVersion and make every overlay re-download an unchanged icon.
            if (!string.Equals(player.EndCapIcon, endCapIcon, StringComparison.Ordinal))
                player.EndCapIcon = endCapIcon;

            RaiseGameStateChanged(_currentGame.Clone());
        }
    }

    /// <summary>
    /// Switches to the other player as one atomic step, so two overlapping button presses can't both
    /// act on the same stale read.
    /// </summary>
    public void ToggleCurrentShooter()
    {
        lock (_sync)
        {
            if (!_currentGame.IsGameActive)
                return;

            _currentGame.CurrentShooter = ReferenceEquals(_currentGame.CurrentShooter, _currentGame.Player1)
                ? _currentGame.Player2
                : _currentGame.Player1;

            RaiseGameStateChanged(_currentGame.Clone());
        }
    }

    public void UndoPoint()
    {
        lock (_sync)
        {
            if (_pointHistory.Count == 0)
                return;

            var wasPlayer1 = _pointHistory[^1];
            _pointHistory.RemoveAt(_pointHistory.Count - 1);

            if (wasPlayer1)
                _currentGame.Player1Score--;
            else
                _currentGame.Player2Score--;

            // A point that ended the match is undoable too, so a mis-clicked winning point doesn't
            // force a full Reset Match. (An empty history is the only "nothing to undo" case, and
            // ResetGame clears it, so this can't resurrect an already-reset match.)
            if (_currentGame.Winner != null)
            {
                _currentGame.Winner = null;
                _currentGame.IsGameActive = true;
            }

            RaiseGameStateChanged(_currentGame.Clone());
        }
    }

    /// <summary>
    /// Assigns one side's 8-ball group, flipping the other side to the complement. Side-based rather
    /// than player-based so a caller never has to read the current players (and possibly act on a
    /// player that a concurrent reset has already replaced).
    /// </summary>
    public void AssignBallGroup(bool isHome, BallGroup group)
    {
        lock (_sync)
        {
            if (_currentGame.GameType != GameType.EightBall)
                return;

            var player = isHome ? _currentGame.Player1 : _currentGame.Player2;
            var other = isHome ? _currentGame.Player2 : _currentGame.Player1;

            player.BallGroup = group;
            other.BallGroup = group switch
            {
                BallGroup.Solids => BallGroup.Stripes,
                BallGroup.Stripes => BallGroup.Solids,
                _ => BallGroup.Unassigned
            };

            RaiseGameStateChanged(_currentGame.Clone());
        }
    }

    public void PocketBall(int ballNumber)
    {
        lock (_sync)
        {
            _currentGame.PocketedBalls.Add(ballNumber);
            RaiseGameStateChanged(_currentGame.Clone());
        }
    }

    public void UnpocketBall(int ballNumber)
    {
        lock (_sync)
        {
            _currentGame.PocketedBalls.Remove(ballNumber);
            RaiseGameStateChanged(_currentGame.Clone());
        }
    }

    /// <summary>
    /// Flips a ball's pocketed state as one atomic step, for the HTTP control API's per-ball buttons
    /// (a separate read-then-write would let two overlapping calls both see "not pocketed").
    /// </summary>
    public void ToggleBall(int ballNumber)
    {
        lock (_sync)
        {
            if (!_currentGame.PocketedBalls.Remove(ballNumber))
                _currentGame.PocketedBalls.Add(ballNumber);

            RaiseGameStateChanged(_currentGame.Clone());
        }
    }

    public void ResetBalls()
    {
        lock (_sync)
        {
            _currentGame.PocketedBalls.Clear();
            RaiseGameStateChanged(_currentGame.Clone());
        }
    }

    /// <summary>
    /// Clears the match — scores, undo history, pocketed balls, winner — and returns to the setup
    /// screen. What the operator configured rather than played is carried over: the two players
    /// (they are the ones the setup screen writes through <see cref="SetPlayerDetails" />, so
    /// replacing them would blank the names typed in whenever the reset comes from the HTTP control
    /// API, which has no screen state to restore them from), the chosen game type and Race-To mode
    /// (so the overlay's preview still matches the setup screen), and the colour theme, scoreboard
    /// style and show/hide settings (a reset ends a match, it doesn't restyle the show). Their ball
    /// groups are cleared, so the next 8-ball rack starts unassigned exactly as a brand-new state
    /// would.
    /// </summary>
    public void ResetGame()
    {
        lock (_sync)
        {
            var player1 = _currentGame.Player1;
            var player2 = _currentGame.Player2;

            player1.BallGroup = BallGroup.Unassigned;
            player2.BallGroup = BallGroup.Unassigned;
            _pointHistory.Clear();

            var next = new GameState
            {
                Player1 = player1,
                Player2 = player2,

                // Setup, not play. Without this the reset state fell back to the enum defaults —
                // 8-ball, single Race-To — while the setup screen still showed what was selected, so
                // a reset over HTTP (which has no screen to re-push anything) left the overlay
                // previewing 8-ball until the operator started a new match.
                GameType = _currentGame.GameType,
                RaceToMode = _currentGame.RaceToMode
            };
            CarryOperatorStyling(_currentGame, next);

            _currentGame = next;
            RaiseGameStateChanged(_currentGame.Clone());
        }
    }

    public void SetColorTheme(ColorTheme theme)
    {
        lock (_sync)
        {
            // Copied like the scoreboard style, so a caller that keeps its own reference can't go on
            // mutating state — off the lock, with no change event — after this returns.
            _currentGame.ColorTheme = theme.Copy();
            RaiseGameStateChanged(_currentGame.Clone());
        }
    }

    public void SetScoreboardStyle(ScoreboardStyle style)
    {
        lock (_sync)
        {
            _currentGame.ScoreboardStyle = new ScoreboardStyle
            {
                CornerRoundness = Math.Clamp(style.CornerRoundness, 0, 100),
                OverallScale = Math.Clamp(style.OverallScale, 50, 200),
                GlossyFinish = style.GlossyFinish,
                EndCapStyle = style.EndCapStyle,
                ShooterIndicatorStyle = style.ShooterIndicatorStyle
            };
            RaiseGameStateChanged(_currentGame.Clone());
        }
    }

    public void SetScoreBarVisible(bool visible)
    {
        lock (_sync)
        {
            _currentGame.Visibility.ScoreBarVisible = visible;
            RaiseGameStateChanged(_currentGame.Clone());
        }
    }

    public void SetBallTrackerVisible(bool visible)
    {
        lock (_sync)
        {
            _currentGame.Visibility.BallTrackerVisible = visible;
            RaiseGameStateChanged(_currentGame.Clone());
        }
    }

    public void SetWinnerBannerVisible(bool visible)
    {
        lock (_sync)
        {
            _currentGame.Visibility.WinnerBannerVisible = visible;
            RaiseGameStateChanged(_currentGame.Clone());
        }
    }

    public void SetCueBallSpin(double x, double y)
    {
        lock (_sync)
        {
            _currentGame.CueBallSpin = new CueBallSpin
            {
                X = Math.Clamp(x, 0.0, 1.0),
                Y = Math.Clamp(y, 0.0, 1.0)
            };
            RaiseGameStateChanged(_currentGame.Clone());
        }
    }

    public void ClearCueBallSpin()
    {
        lock (_sync)
        {
            _currentGame.CueBallSpin = null;
            RaiseGameStateChanged(_currentGame.Clone());
        }
    }

    /// <summary>A copied view of the current state, safe to read or serialize on any thread.</summary>
    public GameState GetStateSnapshot()
    {
        lock (_sync)
        {
            return _currentGame.Clone();
        }
    }

    private void EndGame(Player winner)
    {
        _currentGame.Winner = winner;
        _currentGame.IsGameActive = false;
    }

    private void RaiseGameStateChanged(GameState snapshot)
    {
        GameStateChanged?.Invoke(this, new GameStateChangedEventArgs { GameState = snapshot });
    }
}

public class GameStateChangedEventArgs : EventArgs
{
    public GameState GameState { get; set; } = new();
}
