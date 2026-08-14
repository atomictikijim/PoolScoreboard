using PoolScoreboard.Core.Enums;
using PoolScoreboard.Core.Models;

namespace PoolScoreboard.Core.Tests;

public class GameManagerTests
{
    private static Player CreatePlayer(string name, int raceToTarget) => new()
    {
        Name = name,
        RaceToTarget = raceToTarget
    };

    [Fact]
    public void InitializeGame_SetsUpActiveGameWithPlayer1AsCurrentShooter()
    {
        var manager = new GameManager();
        var player1 = CreatePlayer("Alice", 5);
        var player2 = CreatePlayer("Bob", 5);

        manager.InitializeGame(player1, player2, GameType.NineBall, RaceToMode.Single);

        var state = manager.GetCurrentGameState();
        Assert.True(state.IsGameActive);
        Assert.Same(player1, state.Player1);
        Assert.Same(player2, state.Player2);
        Assert.Same(player1, state.CurrentShooter);
        Assert.Equal(0, state.Player1Score);
        Assert.Equal(0, state.Player2Score);
        Assert.Null(state.Winner);
        Assert.Equal(GameType.NineBall, state.GameType);
        Assert.Equal(RaceToMode.Single, state.RaceToMode);
    }

    [Fact]
    public void AddPoint_IncrementsCorrectPlayersScore()
    {
        var manager = new GameManager();
        manager.InitializeGame(CreatePlayer("Alice", 5), CreatePlayer("Bob", 5), GameType.NineBall, RaceToMode.Single);

        manager.AddPoint(isPlayer1: true);
        manager.AddPoint(isPlayer1: false);
        manager.AddPoint(isPlayer1: true);

        var state = manager.GetCurrentGameState();
        Assert.Equal(2, state.Player1Score);
        Assert.Equal(1, state.Player2Score);
    }

    [Fact]
    public void AddPoint_RaisesGameStateChanged()
    {
        var manager = new GameManager();
        manager.InitializeGame(CreatePlayer("Alice", 5), CreatePlayer("Bob", 5), GameType.NineBall, RaceToMode.Single);

        var raised = 0;
        manager.GameStateChanged += (_, _) => raised++;

        manager.AddPoint(isPlayer1: true);

        Assert.Equal(1, raised);
    }

    [Fact]
    public void AddPoint_Player1WinsAtOwnRaceToTarget_SingleMode()
    {
        var manager = new GameManager();
        manager.InitializeGame(CreatePlayer("Alice", 3), CreatePlayer("Bob", 3), GameType.NineBall, RaceToMode.Single);

        manager.AddPoint(true);
        manager.AddPoint(true);
        manager.AddPoint(true);

        var state = manager.GetCurrentGameState();
        Assert.False(state.IsGameActive);
        Assert.Same(state.Player1, state.Winner);
    }

    [Fact]
    public void AddPoint_SplitMode_PlayersWinAtDifferentTargets()
    {
        var manager = new GameManager();
        var player1 = CreatePlayer("Alice", 2);
        var player2 = CreatePlayer("Bob", 5);
        manager.InitializeGame(player1, player2, GameType.NineBall, RaceToMode.Split);

        manager.AddPoint(true);
        manager.AddPoint(true);

        var state = manager.GetCurrentGameState();
        Assert.False(state.IsGameActive);
        Assert.Same(player1, state.Winner);
        Assert.Equal(2, state.Player1Score);
    }

    [Fact]
    public void AddPoint_DoesNothingWhenGameIsNotActive()
    {
        var manager = new GameManager();
        manager.InitializeGame(CreatePlayer("Alice", 1), CreatePlayer("Bob", 5), GameType.NineBall, RaceToMode.Split);

        manager.AddPoint(true); // Alice wins immediately at race-to 1

        Assert.False(manager.GetCurrentGameState().IsGameActive);

        manager.AddPoint(false);

        Assert.Equal(0, manager.GetCurrentGameState().Player2Score);
    }

    [Fact]
    public void UndoPoint_DecrementsLeadingPlayersScore()
    {
        var manager = new GameManager();
        manager.InitializeGame(CreatePlayer("Alice", 5), CreatePlayer("Bob", 5), GameType.NineBall, RaceToMode.Single);

        manager.AddPoint(true);
        manager.AddPoint(true);
        manager.AddPoint(false);

        manager.UndoPoint();

        var state = manager.GetCurrentGameState();
        Assert.Equal(1, state.Player1Score);
        Assert.Equal(1, state.Player2Score);
    }

    [Fact]
    public void UndoPoint_DoesNothingWhenNoPointsScored()
    {
        var manager = new GameManager();
        manager.InitializeGame(CreatePlayer("Alice", 5), CreatePlayer("Bob", 5), GameType.NineBall, RaceToMode.Single);

        manager.UndoPoint();

        var state = manager.GetCurrentGameState();
        Assert.Equal(0, state.Player1Score);
        Assert.Equal(0, state.Player2Score);
    }

    [Fact]
    public void ResetGame_ClearsStateBackToDefaults()
    {
        var manager = new GameManager();
        manager.InitializeGame(CreatePlayer("Alice", 5), CreatePlayer("Bob", 5), GameType.NineBall, RaceToMode.Single);
        manager.AddPoint(true);

        manager.ResetGame();

        var state = manager.GetCurrentGameState();
        Assert.False(state.IsGameActive);
        Assert.Equal(0, state.Player1Score);
        Assert.Equal(0, state.Player2Score);
        Assert.Null(state.Winner);
        Assert.Null(state.CurrentShooter);
    }

    [Fact]
    public void SetCurrentShooter_UpdatesCurrentShooterWhileGameActive()
    {
        var manager = new GameManager();
        var player1 = CreatePlayer("Alice", 5);
        var player2 = CreatePlayer("Bob", 5);
        manager.InitializeGame(player1, player2, GameType.NineBall, RaceToMode.Single);

        manager.SetCurrentShooter(player2);

        Assert.Same(player2, manager.GetCurrentGameState().CurrentShooter);
    }

    [Fact]
    public void SetCurrentShooter_DoesNothingAfterGameEnds()
    {
        var manager = new GameManager();
        var player1 = CreatePlayer("Alice", 1);
        var player2 = CreatePlayer("Bob", 5);
        manager.InitializeGame(player1, player2, GameType.NineBall, RaceToMode.Split);

        manager.AddPoint(true); // Alice wins, game ends

        manager.SetCurrentShooter(player2);

        Assert.Same(player1, manager.GetCurrentGameState().CurrentShooter);
    }

    [Theory]
    [InlineData(BallGroup.Solids, BallGroup.Stripes)]
    [InlineData(BallGroup.Stripes, BallGroup.Solids)]
    public void AssignBallGroup_EightBall_FlipsOtherPlayerToComplement(BallGroup assigned, BallGroup expectedOther)
    {
        var manager = new GameManager();
        var player1 = CreatePlayer("Alice", 5);
        var player2 = CreatePlayer("Bob", 5);
        manager.InitializeGame(player1, player2, GameType.EightBall, RaceToMode.Single);

        manager.AssignBallGroup(player1, assigned);

        Assert.Equal(assigned, player1.BallGroup);
        Assert.Equal(expectedOther, player2.BallGroup);
    }

    [Fact]
    public void AssignBallGroup_IgnoredForNonEightBallGames()
    {
        var manager = new GameManager();
        var player1 = CreatePlayer("Alice", 5);
        var player2 = CreatePlayer("Bob", 5);
        manager.InitializeGame(player1, player2, GameType.NineBall, RaceToMode.Single);

        manager.AssignBallGroup(player1, BallGroup.Solids);

        Assert.Equal(BallGroup.Unassigned, player1.BallGroup);
        Assert.Equal(BallGroup.Unassigned, player2.BallGroup);
    }

    [Fact]
    public void AssignBallGroup_ReassigningOtherPlayerFlipsBothBack()
    {
        var manager = new GameManager();
        var player1 = CreatePlayer("Alice", 5);
        var player2 = CreatePlayer("Bob", 5);
        manager.InitializeGame(player1, player2, GameType.EightBall, RaceToMode.Single);

        manager.AssignBallGroup(player1, BallGroup.Solids);
        manager.AssignBallGroup(player2, BallGroup.Solids);

        Assert.Equal(BallGroup.Stripes, player1.BallGroup);
        Assert.Equal(BallGroup.Solids, player2.BallGroup);
    }

    [Fact]
    public void PocketBallAndUnpocketBall_ToggleMembershipInPocketedSet()
    {
        var manager = new GameManager();
        manager.InitializeGame(CreatePlayer("Alice", 5), CreatePlayer("Bob", 5), GameType.NineBall, RaceToMode.Single);

        manager.PocketBall(7);
        manager.PocketBall(9);

        var state = manager.GetCurrentGameState();
        Assert.Contains(7, state.PocketedBalls);
        Assert.Contains(9, state.PocketedBalls);

        manager.UnpocketBall(7);

        state = manager.GetCurrentGameState();
        Assert.DoesNotContain(7, state.PocketedBalls);
        Assert.Contains(9, state.PocketedBalls);
    }

    [Fact]
    public void SetColorTheme_UpdatesGameStateColorTheme()
    {
        var manager = new GameManager();
        manager.InitializeGame(CreatePlayer("Alice", 5), CreatePlayer("Bob", 5), GameType.NineBall, RaceToMode.Single);
        var theme = new ColorTheme { Background = "#000000", Accent = "#ff0000", Text = "#ffffff" };

        manager.SetColorTheme(theme);

        Assert.Same(theme, manager.GetCurrentGameState().ColorTheme);
    }

    [Fact]
    public void ResetBalls_ClearsPocketedBalls()
    {
        var manager = new GameManager();
        manager.InitializeGame(CreatePlayer("Alice", 5), CreatePlayer("Bob", 5), GameType.NineBall, RaceToMode.Single);
        manager.PocketBall(1);
        manager.PocketBall(2);

        manager.ResetBalls();

        Assert.Empty(manager.GetCurrentGameState().PocketedBalls);
    }
}
