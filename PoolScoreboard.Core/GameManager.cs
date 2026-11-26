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

    public void InitializeGame(Player player1, Player player2, GameType gameType, RaceToMode raceToMode)
    {
        lock (_sync)
        {
            _pointHistory.Clear();

            _currentGame = new GameState
            {
                Player1 = player1,
                Player2 = player2,
                GameType = gameType,
                RaceToMode = raceToMode,
                Player1Score = 0,
                Player2Score = 0,
                CurrentShooter = player1,
                IsGameActive = true,
                StartTime = DateTime.UtcNow,
                Winner = null
            };

            RaiseGameStateChanged(_currentGame.Clone());
        }
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

    public void SetMatchPreview(Player player1, Player player2, GameType gameType, RaceToMode raceToMode)
    {
        lock (_sync)
        {
            if (_currentGame.IsGameActive)
                return;

            _currentGame.Player1 = player1;
            _currentGame.Player2 = player2;
            _currentGame.GameType = gameType;
            _currentGame.RaceToMode = raceToMode;

            RaiseGameStateChanged(_currentGame.Clone());
        }
    }

    public void SetCurrentShooter(Player player)
    {
        lock (_sync)
        {
            if (!_currentGame.IsGameActive)
                return;

            _currentGame.CurrentShooter = player;
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

    public void ResetGame()
    {
        lock (_sync)
        {
            _pointHistory.Clear();
            _currentGame = new();
            RaiseGameStateChanged(_currentGame.Clone());
        }
    }

    public void SetColorTheme(ColorTheme theme)
    {
        lock (_sync)
        {
            _currentGame.ColorTheme = theme;
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

    /// <summary>
    /// The live instance, for reading single fields (tests mostly). It is not a point-in-time view:
    /// anything that copies collections, serializes, or runs on another thread should use
    /// <see cref="GetStateSnapshot"/> instead.
    /// </summary>
    public GameState GetCurrentGameState() => _currentGame;

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
