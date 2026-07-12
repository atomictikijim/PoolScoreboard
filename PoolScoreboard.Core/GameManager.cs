using PoolScoreboard.Core.Enums;
using PoolScoreboard.Core.Models;
using PoolScoreboard.Core.Rules;

namespace PoolScoreboard.Core;

public class GameManager
{
    private GameState _currentGame = new();
    private RaceRules? _raceRules;

    public event EventHandler<GameStateChangedEventArgs>? GameStateChanged;

    public void InitializeGame(Player player1, Player player2, GameType gameType)
    {
        _currentGame = new GameState
        {
            Player1 = player1,
            Player2 = player2,
            GameType = gameType,
            Player1Score = 0,
            Player2Score = 0,
            CurrentBreak = player1,
            IsGameActive = true,
            StartTime = DateTime.UtcNow,
            Winner = null
        };

        _raceRules = new RaceRules(player1.League, gameType);
        RaiseGameStateChanged();
    }

    public void AddPoint(bool isPlayer1)
    {
        if (!_currentGame.IsGameActive || _raceRules == null)
            return;

        if (isPlayer1)
        {
            _currentGame.Player1Score++;
            if (_raceRules.IsPlayerWinner(_currentGame.Player1, _currentGame.Player1Score, _currentGame.Player2Score))
            {
                EndGame(_currentGame.Player1);
                return;
            }
        }
        else
        {
            _currentGame.Player2Score++;
            if (_raceRules.IsPlayerWinner(_currentGame.Player2, _currentGame.Player2Score, _currentGame.Player1Score))
            {
                EndGame(_currentGame.Player2);
                return;
            }
        }

        RaiseGameStateChanged();
    }

    public void SetBreak(Player player)
    {
        if (_currentGame.IsGameActive)
        {
            _currentGame.CurrentBreak = player;
            RaiseGameStateChanged();
        }
    }

    public void UndoPoint()
    {
        if (!_currentGame.IsGameActive || _raceRules == null)
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

    public void ResetGame()
    {
        _currentGame = new();
        _raceRules = null;
        RaiseGameStateChanged();
    }

    private void EndGame(Player winner)
    {
        _currentGame.Winner = winner;
        _currentGame.IsGameActive = false;
        RaiseGameStateChanged();
    }

    public GameState GetCurrentGameState() => _currentGame;

    public int GetRaceToValue(Player player)
    {
        return _raceRules?.GetRaceToValue(player) ?? 5;
    }

    private void RaiseGameStateChanged()
    {
        GameStateChanged?.Invoke(this, new GameStateChangedEventArgs { GameState = _currentGame });
    }
}

public class GameStateChangedEventArgs : EventArgs
{
    public GameState GameState { get; set; } = new();
}
