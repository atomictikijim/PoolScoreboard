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

        if (isPlayer1)
        {
            _currentGame.Player1Score++;
            if (_currentGame.Player1Score >= _currentGame.Player1.RaceToTarget)
            {
                EndGame(_currentGame.Player1);
                return;
            }
        }
        else
        {
            _currentGame.Player2Score++;
            if (_currentGame.Player2Score >= _currentGame.Player2.RaceToTarget)
            {
                EndGame(_currentGame.Player2);
                return;
            }
        }

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

            _currentGame.Winner = null;
            _currentGame.IsGameActive = true;
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

    private void EndGame(Player winner)
    {
        _currentGame.Winner = winner;
        _currentGame.IsGameActive = false;
        RaiseGameStateChanged();
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
