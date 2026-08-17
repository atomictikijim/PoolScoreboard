using PoolScoreboard.Core.Enums;
using PoolScoreboard.Core.Models;

namespace PoolScoreboard.Core;

public class GameManager
{
    private GameState _currentGame = new();

    public event EventHandler<GameStateChangedEventArgs>? GameStateChanged;

    public void InitializeGame(Player player1, Player player2, GameType gameType, RaceToMode raceToMode)
    {
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

        RaiseGameStateChanged();
    }

    public void AddPoint(bool isPlayer1)
    {
        if (!_currentGame.IsGameActive)
            return;

        _currentGame.CueBallSpin = null;

        if (isPlayer1)
            _currentGame.Player1Score++;
        else
            _currentGame.Player2Score++;

        UpdateWinner();
        RaiseGameStateChanged();
    }

    public void SubtractPoint(bool isPlayer1)
    {
        if (!_currentGame.IsGameActive)
            return;

        if (isPlayer1)
        {
            if (_currentGame.Player1Score > 0)
                _currentGame.Player1Score--;
        }
        else
        {
            if (_currentGame.Player2Score > 0)
                _currentGame.Player2Score--;
        }

        UpdateWinner();
        RaiseGameStateChanged();
    }

    public void SetMatchPreview(Player player1, Player player2, GameType gameType, RaceToMode raceToMode)
    {
        if (_currentGame.IsGameActive)
            return;

        _currentGame.Player1 = player1;
        _currentGame.Player2 = player2;
        _currentGame.GameType = gameType;
        _currentGame.RaceToMode = raceToMode;
        RaiseGameStateChanged();
    }

    public void SetCurrentShooter(Player player)
    {
        if (_currentGame.IsGameActive)
        {
            _currentGame.CurrentShooter = player;
            RaiseGameStateChanged();
        }
    }

    public void UndoPoint()
    {
        if (!_currentGame.IsGameActive)
            return;

        if (_currentGame.Player1Score > 0 || _currentGame.Player2Score > 0)
        {
            if (_currentGame.Player1Score > _currentGame.Player2Score)
                _currentGame.Player1Score--;
            else if (_currentGame.Player2Score > _currentGame.Player1Score)
                _currentGame.Player2Score--;

            UpdateWinner();
            RaiseGameStateChanged();
        }
    }

    public void AssignBallGroup(Player player, BallGroup group)
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

        RaiseGameStateChanged();
    }

    public void PocketBall(int ballNumber)
    {
        _currentGame.PocketedBalls.Add(ballNumber);
        RaiseGameStateChanged();
    }

    public void UnpocketBall(int ballNumber)
    {
        _currentGame.PocketedBalls.Remove(ballNumber);
        RaiseGameStateChanged();
    }

    public void ResetBalls()
    {
        _currentGame.PocketedBalls.Clear();
        RaiseGameStateChanged();
    }

    public void ResetGame()
    {
        _currentGame = new();
        RaiseGameStateChanged();
    }

    public void SetColorTheme(ColorTheme theme)
    {
        _currentGame.ColorTheme = theme;
        RaiseGameStateChanged();
    }

    public void SetScoreboardStyle(ScoreboardStyle style)
    {
        _currentGame.ScoreboardStyle = new ScoreboardStyle
        {
            CornerRoundness = Math.Clamp(style.CornerRoundness, 0, 100),
            OverallScale = Math.Clamp(style.OverallScale, 50, 200),
            GlossyFinish = style.GlossyFinish,
            EndCapStyle = style.EndCapStyle,
            ShooterIndicatorStyle = style.ShooterIndicatorStyle
        };
        RaiseGameStateChanged();
    }

    public void SetScoreBarVisible(bool visible)
    {
        _currentGame.Visibility.ScoreBarVisible = visible;
        RaiseGameStateChanged();
    }

    public void SetBallTrackerVisible(bool visible)
    {
        _currentGame.Visibility.BallTrackerVisible = visible;
        RaiseGameStateChanged();
    }

    public void SetWinnerBannerVisible(bool visible)
    {
        _currentGame.Visibility.WinnerBannerVisible = visible;
        RaiseGameStateChanged();
    }

    public void SetCueBallSpin(double x, double y)
    {
        _currentGame.CueBallSpin = new CueBallSpin
        {
            X = Math.Clamp(x, 0.0, 1.0),
            Y = Math.Clamp(y, 0.0, 1.0)
        };
        RaiseGameStateChanged();
    }

    public void ClearCueBallSpin()
    {
        _currentGame.CueBallSpin = null;
        RaiseGameStateChanged();
    }

    private void UpdateWinner()
    {
        if (_currentGame.Player1Score >= _currentGame.Player1.RaceToTarget)
            _currentGame.Winner = _currentGame.Player1;
        else if (_currentGame.Player2Score >= _currentGame.Player2.RaceToTarget)
            _currentGame.Winner = _currentGame.Player2;
        else
            _currentGame.Winner = null;
    }

    public GameState GetCurrentGameState() => _currentGame;

    private void RaiseGameStateChanged()
    {
        GameStateChanged?.Invoke(this, new GameStateChangedEventArgs { GameState = _currentGame });
    }
}

public class GameStateChangedEventArgs : EventArgs
{
    public GameState GameState { get; set; } = new();
}
