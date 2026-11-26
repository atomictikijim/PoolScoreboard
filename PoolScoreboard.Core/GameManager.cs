using PoolScoreboard.Core.Enums;
using PoolScoreboard.Core.Models;

namespace PoolScoreboard.Core;

/// <summary>
/// Orchestrator for all game state. Mutations can arrive from different threads — the WPF UI,
/// its keyboard shortcuts, and the local HTTP control API the Stream Deck drives all call into the
/// same instance — so every mutation runs under one internal lock. <see cref="GameStateChanged"/>
/// carries a snapshot copied under that lock rather than the live instance, so subscribers (the WPF
/// view model, the overlay's SSE serializers) can hold and read it off-thread without ever seeing a
/// collection mutated underneath them.
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
        GameState snapshot;
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

            snapshot = _currentGame.Clone();
        }

        RaiseGameStateChanged(snapshot);
    }

    public void AddPoint(bool isPlayer1)
    {
        GameState snapshot;
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

            snapshot = _currentGame.Clone();
        }

        RaiseGameStateChanged(snapshot);
    }

    public void SetMatchPreview(Player player1, Player player2, GameType gameType, RaceToMode raceToMode)
    {
        GameState snapshot;
        lock (_sync)
        {
            if (_currentGame.IsGameActive)
                return;

            _currentGame.Player1 = player1;
            _currentGame.Player2 = player2;
            _currentGame.GameType = gameType;
            _currentGame.RaceToMode = raceToMode;
            snapshot = _currentGame.Clone();
        }

        RaiseGameStateChanged(snapshot);
    }

    public void SetCurrentShooter(Player player)
    {
        GameState snapshot;
        lock (_sync)
        {
            if (!_currentGame.IsGameActive)
                return;

            _currentGame.CurrentShooter = player;
            snapshot = _currentGame.Clone();
        }

        RaiseGameStateChanged(snapshot);
    }

    /// <summary>
    /// Switches to the other player as one atomic step. The HTTP control API uses this rather than
    /// reading the shooter and calling <see cref="SetCurrentShooter"/> itself, so two overlapping
    /// button presses can't both act on the same stale read.
    /// </summary>
    public void ToggleCurrentShooter()
    {
        GameState snapshot;
        lock (_sync)
        {
            if (!_currentGame.IsGameActive)
                return;

            _currentGame.CurrentShooter = ReferenceEquals(_currentGame.CurrentShooter, _currentGame.Player1)
                ? _currentGame.Player2
                : _currentGame.Player1;
            snapshot = _currentGame.Clone();
        }

        RaiseGameStateChanged(snapshot);
    }

    public void UndoPoint()
    {
        GameState snapshot;
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

            snapshot = _currentGame.Clone();
        }

        RaiseGameStateChanged(snapshot);
    }

    public void AssignBallGroup(Player player, BallGroup group)
    {
        GameState snapshot;
        lock (_sync)
        {
            if (_currentGame.GameType != GameType.EightBall)
                return;

            Player other;
            if (ReferenceEquals(player, _currentGame.Player1))
                other = _currentGame.Player2;
            else if (ReferenceEquals(player, _currentGame.Player2))
                other = _currentGame.Player1;
            else
                return;

            player.BallGroup = group;
            other.BallGroup = group switch
            {
                BallGroup.Solids => BallGroup.Stripes,
                BallGroup.Stripes => BallGroup.Solids,
                _ => BallGroup.Unassigned
            };

            snapshot = _currentGame.Clone();
        }

        RaiseGameStateChanged(snapshot);
    }

    public void PocketBall(int ballNumber)
    {
        GameState snapshot;
        lock (_sync)
        {
            _currentGame.PocketedBalls.Add(ballNumber);
            snapshot = _currentGame.Clone();
        }

        RaiseGameStateChanged(snapshot);
    }

    public void UnpocketBall(int ballNumber)
    {
        GameState snapshot;
        lock (_sync)
        {
            _currentGame.PocketedBalls.Remove(ballNumber);
            snapshot = _currentGame.Clone();
        }

        RaiseGameStateChanged(snapshot);
    }

    /// <summary>
    /// Flips a ball's pocketed state as one atomic step, for the HTTP control API's per-ball
    /// buttons (a separate read-then-write would let two overlapping calls both see "not pocketed").
    /// </summary>
    public void ToggleBall(int ballNumber)
    {
        GameState snapshot;
        lock (_sync)
        {
            if (!_currentGame.PocketedBalls.Remove(ballNumber))
                _currentGame.PocketedBalls.Add(ballNumber);

            snapshot = _currentGame.Clone();
        }

        RaiseGameStateChanged(snapshot);
    }

    public void ResetBalls()
    {
        GameState snapshot;
        lock (_sync)
        {
            _currentGame.PocketedBalls.Clear();
            snapshot = _currentGame.Clone();
        }

        RaiseGameStateChanged(snapshot);
    }

    public void ResetGame()
    {
        GameState snapshot;
        lock (_sync)
        {
            _pointHistory.Clear();
            _currentGame = new();
            snapshot = _currentGame.Clone();
        }

        RaiseGameStateChanged(snapshot);
    }

    public void SetColorTheme(ColorTheme theme)
    {
        GameState snapshot;
        lock (_sync)
        {
            _currentGame.ColorTheme = theme;
            snapshot = _currentGame.Clone();
        }

        RaiseGameStateChanged(snapshot);
    }

    public void SetScoreboardStyle(ScoreboardStyle style)
    {
        GameState snapshot;
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
            snapshot = _currentGame.Clone();
        }

        RaiseGameStateChanged(snapshot);
    }

    public void SetScoreBarVisible(bool visible)
    {
        GameState snapshot;
        lock (_sync)
        {
            _currentGame.Visibility.ScoreBarVisible = visible;
            snapshot = _currentGame.Clone();
        }

        RaiseGameStateChanged(snapshot);
    }

    public void SetBallTrackerVisible(bool visible)
    {
        GameState snapshot;
        lock (_sync)
        {
            _currentGame.Visibility.BallTrackerVisible = visible;
            snapshot = _currentGame.Clone();
        }

        RaiseGameStateChanged(snapshot);
    }

    public void SetWinnerBannerVisible(bool visible)
    {
        GameState snapshot;
        lock (_sync)
        {
            _currentGame.Visibility.WinnerBannerVisible = visible;
            snapshot = _currentGame.Clone();
        }

        RaiseGameStateChanged(snapshot);
    }

    public void SetCueBallSpin(double x, double y)
    {
        GameState snapshot;
        lock (_sync)
        {
            _currentGame.CueBallSpin = new CueBallSpin
            {
                X = Math.Clamp(x, 0.0, 1.0),
                Y = Math.Clamp(y, 0.0, 1.0)
            };
            snapshot = _currentGame.Clone();
        }

        RaiseGameStateChanged(snapshot);
    }

    public void ClearCueBallSpin()
    {
        GameState snapshot;
        lock (_sync)
        {
            _currentGame.CueBallSpin = null;
            snapshot = _currentGame.Clone();
        }

        RaiseGameStateChanged(snapshot);
    }

    /// <summary>
    /// The live instance, for callers that only read single fields or pass its player references
    /// back into a mutating method. Use <see cref="GetStateSnapshot"/> when copying collections or
    /// serializing, so a concurrent mutation can't interfere.
    /// </summary>
    public GameState GetCurrentGameState()
    {
        lock (_sync)
        {
            return _currentGame;
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

    /// <summary>
    /// Raises the event outside the lock (so a subscriber can call back into this instance without
    /// deadlocking) with the snapshot taken while the mutation was applied.
    /// </summary>
    private void RaiseGameStateChanged(GameState snapshot)
    {
        GameStateChanged?.Invoke(this, new GameStateChangedEventArgs { GameState = snapshot });
    }
}

public class GameStateChangedEventArgs : EventArgs
{
    public GameState GameState { get; set; } = new();
}
