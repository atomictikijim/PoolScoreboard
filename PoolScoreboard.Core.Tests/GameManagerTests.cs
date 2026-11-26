using System.Collections.Concurrent;
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
    public void UndoPoint_ReversesTheMostRecentlyScoredPoint_NotTheLeader()
    {
        var manager = new GameManager();
        manager.InitializeGame(CreatePlayer("Alice", 5), CreatePlayer("Bob", 5), GameType.NineBall, RaceToMode.Single);

        manager.AddPoint(true);
        manager.AddPoint(true);
        manager.AddPoint(false); // away scores last, but home is still ahead

        manager.UndoPoint();

        var state = manager.GetCurrentGameState();
        Assert.Equal(2, state.Player1Score);
        Assert.Equal(0, state.Player2Score);
    }

    [Fact]
    public void UndoPoint_WalksBackThroughPointsInReverseOrder()
    {
        var manager = new GameManager();
        manager.InitializeGame(CreatePlayer("Alice", 5), CreatePlayer("Bob", 5), GameType.NineBall, RaceToMode.Single);

        manager.AddPoint(true);
        manager.AddPoint(false);
        manager.AddPoint(true);

        manager.UndoPoint(); // undoes home's second point
        Assert.Equal(1, manager.GetCurrentGameState().Player1Score);
        Assert.Equal(1, manager.GetCurrentGameState().Player2Score);

        manager.UndoPoint(); // undoes away's point
        Assert.Equal(1, manager.GetCurrentGameState().Player1Score);
        Assert.Equal(0, manager.GetCurrentGameState().Player2Score);
    }

    [Fact]
    public void UndoPoint_AtATie_ReversesTheMostRecentPoint()
    {
        var manager = new GameManager();
        manager.InitializeGame(CreatePlayer("Alice", 5), CreatePlayer("Bob", 5), GameType.NineBall, RaceToMode.Single);

        manager.AddPoint(true);
        manager.AddPoint(false);

        manager.UndoPoint();

        var state = manager.GetCurrentGameState();
        Assert.Equal(1, state.Player1Score);
        Assert.Equal(0, state.Player2Score);
    }

    [Fact]
    public void UndoPoint_AfterTheWinningPoint_ReactivatesTheMatch()
    {
        var manager = new GameManager();
        manager.InitializeGame(CreatePlayer("Alice", 1), CreatePlayer("Bob", 5), GameType.NineBall, RaceToMode.Split);

        manager.AddPoint(true); // Alice reaches race-to 1, match ends
        Assert.False(manager.GetCurrentGameState().IsGameActive);

        manager.UndoPoint();

        var state = manager.GetCurrentGameState();
        Assert.True(state.IsGameActive);
        Assert.Null(state.Winner);
        Assert.Equal(0, state.Player1Score);
    }

    [Fact]
    public void UndoPoint_DoesNothingAfterResetGame()
    {
        var manager = new GameManager();
        manager.InitializeGame(CreatePlayer("Alice", 5), CreatePlayer("Bob", 5), GameType.NineBall, RaceToMode.Single);
        manager.AddPoint(true);
        manager.AddPoint(true);

        manager.ResetGame();
        manager.UndoPoint();

        var state = manager.GetCurrentGameState();
        Assert.False(state.IsGameActive);
        Assert.Equal(0, state.Player1Score);
        Assert.Null(state.Winner);
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

        manager.AssignBallGroup(isHome: true, assigned);

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

        manager.AssignBallGroup(isHome: true, BallGroup.Solids);

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

        manager.AssignBallGroup(isHome: true, BallGroup.Solids);
        manager.AssignBallGroup(isHome: false, BallGroup.Solids);

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
    public void EndCapIconVersion_BumpsWheneverIconIsAssigned()
    {
        var player = CreatePlayer("Alice", 5);

        Assert.Equal(0, player.EndCapIconVersion);

        player.EndCapIcon = "data:image/png;base64,AAAA";
        Assert.Equal(1, player.EndCapIconVersion);

        player.EndCapIcon = null;
        Assert.Equal(2, player.EndCapIconVersion);
    }

    [Fact]
    public void GameStateChanged_CarriesASnapshotThatLaterMutationsCannotChange()
    {
        var manager = new GameManager();
        manager.InitializeGame(CreatePlayer("Alice", 5), CreatePlayer("Bob", 5), GameType.NineBall, RaceToMode.Single);

        GameState? raised = null;
        void Capture(object? sender, GameStateChangedEventArgs e) => raised = e.GameState;

        manager.GameStateChanged += Capture;
        manager.AddPoint(isPlayer1: true);
        manager.GameStateChanged -= Capture;

        // Mutate afterwards: the snapshot handed to the subscriber must not follow along.
        manager.PocketBall(3);

        Assert.NotNull(raised);
        Assert.Equal(1, raised!.Player1Score);
        Assert.Empty(raised.PocketedBalls);
        Assert.NotSame(manager.GetCurrentGameState(), raised);
    }

    [Fact]
    public void ConcurrentPoints_RaiseSnapshotsInMutationOrder()
    {
        var manager = new GameManager();
        manager.InitializeGame(CreatePlayer("Alice", 10_000), CreatePlayer("Bob", 10_000), GameType.NineBall, RaceToMode.Single);

        // Events are raised under the state lock, so this list is appended to by one thread at a
        // time and the scores must arrive as 1,2,3... — an out-of-order delivery would show up as a
        // score that goes backwards.
        var observedScores = new List<int>();
        manager.GameStateChanged += (_, e) => observedScores.Add(e.GameState.Player1Score);

        Parallel.For(0, 200, _ => manager.AddPoint(isPlayer1: true));

        Assert.Equal(Enumerable.Range(1, 200), observedScores);
    }

    [Fact]
    public void ConcurrentMutations_AreSerializedAndEveryEventCarriesAConsistentSnapshot()
    {
        var manager = new GameManager();
        manager.InitializeGame(CreatePlayer("Alice", 10_000), CreatePlayer("Bob", 10_000), GameType.NineBall, RaceToMode.Single);

        // Enumerating the pocketed set is what the overlay's SSE serializer does on the raising
        // thread. Before the lock/snapshot change this threw "Collection was modified" under load,
        // which surfaced as an HTTP 500 from whichever control endpoint was mutating state.
        var snapshotSizes = new ConcurrentQueue<int>();
        manager.GameStateChanged += (_, e) => snapshotSizes.Enqueue(new List<int>(e.GameState.PocketedBalls).Count);

        var failures = new ConcurrentQueue<Exception>();
        Parallel.For(0, 400, i =>
        {
            try
            {
                switch (i % 4)
                {
                    case 0: manager.AddPoint(isPlayer1: true); break;
                    case 1: manager.AddPoint(isPlayer1: false); break;
                    case 2: manager.ToggleBall((i % 15) + 1); break;
                    default: manager.ToggleCurrentShooter(); break;
                }
            }
            catch (Exception ex)
            {
                failures.Enqueue(ex);
            }
        });

        Assert.Empty(failures);
        Assert.All(snapshotSizes, size => Assert.InRange(size, 0, 15));

        var state = manager.GetCurrentGameState();
        Assert.Equal(100, state.Player1Score);
        Assert.Equal(100, state.Player2Score);
    }

    [Fact]
    public void SetColorTheme_UpdatesGameStateColorTheme()
    {
        var manager = new GameManager();
        manager.InitializeGame(CreatePlayer("Alice", 5), CreatePlayer("Bob", 5), GameType.NineBall, RaceToMode.Single);
        var theme = new ColorTheme { HomeBackground = "#000000", AwayBackground = "#222222", HomeAccent = "#ff0000", AwayAccent = "#0000ff", Text = "#ffffff" };

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

    [Fact]
    public void InitializeGame_DefaultsToFullyRoundedVisibleStyle()
    {
        var manager = new GameManager();
        manager.InitializeGame(CreatePlayer("Alice", 5), CreatePlayer("Bob", 5), GameType.NineBall, RaceToMode.Single);

        var state = manager.GetCurrentGameState();
        Assert.Equal(100, state.ScoreboardStyle.CornerRoundness);
        Assert.Equal(100, state.ScoreboardStyle.OverallScale);
        Assert.True(state.ScoreboardStyle.GlossyFinish);
        Assert.Equal(EndCapStyle.Dot, state.ScoreboardStyle.EndCapStyle);
        Assert.Equal(ShooterIndicatorStyle.Glow, state.ScoreboardStyle.ShooterIndicatorStyle);
        Assert.True(state.Visibility.ScoreBarVisible);
        Assert.True(state.Visibility.BallTrackerVisible);
        Assert.True(state.Visibility.WinnerBannerVisible);
    }

    [Fact]
    public void SetScoreboardStyle_UpdatesGameStateScoreboardStyle()
    {
        var manager = new GameManager();
        manager.InitializeGame(CreatePlayer("Alice", 5), CreatePlayer("Bob", 5), GameType.NineBall, RaceToMode.Single);
        var style = new ScoreboardStyle
        {
            CornerRoundness = 40,
            OverallScale = 150,
            GlossyFinish = false,
            EndCapStyle = EndCapStyle.Hidden,
            ShooterIndicatorStyle = ShooterIndicatorStyle.Both
        };

        manager.SetScoreboardStyle(style);

        var result = manager.GetCurrentGameState().ScoreboardStyle;
        Assert.Equal(40, result.CornerRoundness);
        Assert.Equal(150, result.OverallScale);
        Assert.False(result.GlossyFinish);
        Assert.Equal(EndCapStyle.Hidden, result.EndCapStyle);
        Assert.Equal(ShooterIndicatorStyle.Both, result.ShooterIndicatorStyle);
    }

    [Theory]
    [InlineData(-20, 0)]
    [InlineData(140, 100)]
    public void SetScoreboardStyle_ClampsCornerRoundnessToValidRange(int input, int expected)
    {
        var manager = new GameManager();
        manager.InitializeGame(CreatePlayer("Alice", 5), CreatePlayer("Bob", 5), GameType.NineBall, RaceToMode.Single);

        manager.SetScoreboardStyle(new ScoreboardStyle { CornerRoundness = input });

        Assert.Equal(expected, manager.GetCurrentGameState().ScoreboardStyle.CornerRoundness);
    }

    [Theory]
    [InlineData(10, 50)]
    [InlineData(500, 200)]
    public void SetScoreboardStyle_ClampsOverallScaleToValidRange(int input, int expected)
    {
        var manager = new GameManager();
        manager.InitializeGame(CreatePlayer("Alice", 5), CreatePlayer("Bob", 5), GameType.NineBall, RaceToMode.Single);

        manager.SetScoreboardStyle(new ScoreboardStyle { OverallScale = input });

        Assert.Equal(expected, manager.GetCurrentGameState().ScoreboardStyle.OverallScale);
    }

    [Fact]
    public void SetScoreBarVisible_UpdatesVisibilityAndRaisesGameStateChanged()
    {
        var manager = new GameManager();
        manager.InitializeGame(CreatePlayer("Alice", 5), CreatePlayer("Bob", 5), GameType.NineBall, RaceToMode.Single);
        var raised = 0;
        manager.GameStateChanged += (_, _) => raised++;

        manager.SetScoreBarVisible(false);

        Assert.False(manager.GetCurrentGameState().Visibility.ScoreBarVisible);
        Assert.Equal(1, raised);
    }

    [Fact]
    public void SetBallTrackerVisible_UpdatesVisibility()
    {
        var manager = new GameManager();
        manager.InitializeGame(CreatePlayer("Alice", 5), CreatePlayer("Bob", 5), GameType.NineBall, RaceToMode.Single);

        manager.SetBallTrackerVisible(false);

        Assert.False(manager.GetCurrentGameState().Visibility.BallTrackerVisible);
    }

    [Fact]
    public void SetMatchPreview_UpdatesPlayersGameTypeAndRaceToMode_BeforeMatchStarts()
    {
        var manager = new GameManager();
        var player1 = CreatePlayer("Alice", 7);
        var player2 = CreatePlayer("Bob", 7);

        manager.SetMatchPreview(player1, player2, GameType.EightBall, RaceToMode.Split);

        var state = manager.GetCurrentGameState();
        Assert.Same(player1, state.Player1);
        Assert.Same(player2, state.Player2);
        Assert.Equal(GameType.EightBall, state.GameType);
        Assert.Equal(RaceToMode.Split, state.RaceToMode);
        Assert.False(state.IsGameActive);
    }

    [Fact]
    public void SetMatchPreview_DoesNothingWhileGameIsActive()
    {
        var manager = new GameManager();
        var player1 = CreatePlayer("Alice", 5);
        var player2 = CreatePlayer("Bob", 5);
        manager.InitializeGame(player1, player2, GameType.NineBall, RaceToMode.Single);

        manager.SetMatchPreview(CreatePlayer("Someone Else", 9), CreatePlayer("Another", 9), GameType.TenBall, RaceToMode.Split);

        var state = manager.GetCurrentGameState();
        Assert.Same(player1, state.Player1);
        Assert.Same(player2, state.Player2);
        Assert.Equal(GameType.NineBall, state.GameType);
        Assert.Equal(RaceToMode.Single, state.RaceToMode);
    }

    [Fact]
    public void SetWinnerBannerVisible_UpdatesVisibility()
    {
        var manager = new GameManager();
        manager.InitializeGame(CreatePlayer("Alice", 5), CreatePlayer("Bob", 5), GameType.NineBall, RaceToMode.Single);

        manager.SetWinnerBannerVisible(false);

        Assert.False(manager.GetCurrentGameState().Visibility.WinnerBannerVisible);
    }

    [Fact]
    public void SetCueBallSpin_SetsValue()
    {
        var manager = new GameManager();
        manager.InitializeGame(CreatePlayer("Alice", 5), CreatePlayer("Bob", 5), GameType.NineBall, RaceToMode.Single);

        manager.SetCueBallSpin(0.25, 0.75);

        var spin = manager.GetCurrentGameState().CueBallSpin;
        Assert.NotNull(spin);
        Assert.Equal(0.25, spin!.X);
        Assert.Equal(0.75, spin.Y);
    }

    [Fact]
    public void SetCueBallSpin_ClampsOutOfRangeValues()
    {
        var manager = new GameManager();
        manager.InitializeGame(CreatePlayer("Alice", 5), CreatePlayer("Bob", 5), GameType.NineBall, RaceToMode.Single);

        manager.SetCueBallSpin(-0.5, 1.5);

        var spin = manager.GetCurrentGameState().CueBallSpin;
        Assert.NotNull(spin);
        Assert.Equal(0.0, spin!.X);
        Assert.Equal(1.0, spin.Y);
    }

    [Fact]
    public void ClearCueBallSpin_ClearsValue()
    {
        var manager = new GameManager();
        manager.InitializeGame(CreatePlayer("Alice", 5), CreatePlayer("Bob", 5), GameType.NineBall, RaceToMode.Single);
        manager.SetCueBallSpin(0.5, 0.5);

        manager.ClearCueBallSpin();

        Assert.Null(manager.GetCurrentGameState().CueBallSpin);
    }

    [Fact]
    public void AddPoint_ClearsCueBallSpin()
    {
        var manager = new GameManager();
        manager.InitializeGame(CreatePlayer("Alice", 5), CreatePlayer("Bob", 5), GameType.NineBall, RaceToMode.Single);
        manager.SetCueBallSpin(0.5, 0.5);

        manager.AddPoint(isPlayer1: true);

        Assert.Null(manager.GetCurrentGameState().CueBallSpin);
    }
}
