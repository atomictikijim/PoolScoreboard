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

    /// <summary>
    /// A manager with a match in progress, set up the way the setup screen does it: write both
    /// players through <see cref="GameManager.SetPlayerDetails" />, then start from them. There is no
    /// "bring your own players" entry point any more, so the tests exercise the path the operator's
    /// START MATCH uses.
    /// </summary>
    private static GameManager NewMatch(
        GameType gameType = GameType.NineBall,
        RaceToMode raceToMode = RaceToMode.Single,
        int homeRaceTo = 5,
        int awayRaceTo = 5)
    {
        var manager = new GameManager();
        manager.SetPlayerDetails(isHome: true, "Alice", string.Empty, homeRaceTo, endCapIcon: null);
        manager.SetPlayerDetails(isHome: false, "Bob", string.Empty, awayRaceTo, endCapIcon: null);
        manager.InitializeGame(gameType, raceToMode);
        return manager;
    }

    [Fact]
    public void InitializeGame_SetsUpActiveGameWithPlayer1AsCurrentShooter()
    {
        var manager = NewMatch();

        var state = manager.GetCurrentGameState();
        Assert.True(state.IsGameActive);
        Assert.Equal("Alice", state.Player1.Name);
        Assert.Equal("Bob", state.Player2.Name);
        Assert.Same(state.Player1, state.CurrentShooter);
        Assert.Equal(0, state.Player1Score);
        Assert.Equal(0, state.Player2Score);
        Assert.Null(state.Winner);
        Assert.Equal(GameType.NineBall, state.GameType);
        Assert.Equal(RaceToMode.Single, state.RaceToMode);
    }

    [Fact]
    public void AddPoint_IncrementsCorrectPlayersScore()
    {
        var manager = NewMatch();

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
        var manager = NewMatch();

        var raised = 0;
        manager.GameStateChanged += (_, _) => raised++;

        manager.AddPoint(isPlayer1: true);

        Assert.Equal(1, raised);
    }

    [Fact]
    public void AddPoint_Player1WinsAtOwnRaceToTarget_SingleMode()
    {
        var manager = NewMatch(homeRaceTo: 3, awayRaceTo: 3);

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
        var manager = NewMatch(raceToMode: RaceToMode.Split, homeRaceTo: 2, awayRaceTo: 5);

        manager.AddPoint(true);
        manager.AddPoint(true);

        var state = manager.GetCurrentGameState();
        Assert.False(state.IsGameActive);
        Assert.Same(state.Player1, state.Winner);
        Assert.Equal(2, state.Player1Score);
    }

    [Fact]
    public void AddPoint_DoesNothingWhenGameIsNotActive()
    {
        var manager = NewMatch(raceToMode: RaceToMode.Split, homeRaceTo: 1);

        manager.AddPoint(true); // Alice wins immediately at race-to 1

        Assert.False(manager.GetCurrentGameState().IsGameActive);

        manager.AddPoint(false);

        Assert.Equal(0, manager.GetCurrentGameState().Player2Score);
    }

    [Fact]
    public void UndoPoint_ReversesTheMostRecentlyScoredPoint_NotTheLeader()
    {
        var manager = NewMatch();

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
        var manager = NewMatch();

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
        var manager = NewMatch();

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
        var manager = NewMatch(raceToMode: RaceToMode.Split, homeRaceTo: 1);

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
        var manager = NewMatch();
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
        var manager = NewMatch();

        manager.UndoPoint();

        var state = manager.GetCurrentGameState();
        Assert.Equal(0, state.Player1Score);
        Assert.Equal(0, state.Player2Score);
    }

    [Fact]
    public void ResetGame_ClearsScoresWinnerAndCurrentShooter()
    {
        var manager = NewMatch();
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
    public void ResetGame_KeepsTheSetupPlayersAndClearsTheirBallGroups()
    {
        var manager = new GameManager();
        manager.SetPlayerDetails(isHome: true, "Alice", "Blue Cuers", raceToTarget: 7, "data:image/png;base64,AAAA");
        manager.SetPlayerDetails(isHome: false, "Bob", "Red Rackers", raceToTarget: 7, endCapIcon: null);
        manager.InitializeGame(GameType.EightBall, RaceToMode.Split);
        manager.AssignBallGroup(isHome: true, BallGroup.Solids);
        manager.AddPoint(true);

        // A reset from the HTTP control API has no setup screen to re-type the names afterwards.
        manager.ResetGame();

        var state = manager.GetStateSnapshot();
        Assert.False(state.IsGameActive);
        Assert.Equal(0, state.Player1Score);
        Assert.Equal("Alice", state.Player1.Name);
        Assert.Equal("Blue Cuers", state.Player1.TeamName);
        Assert.Equal(7, state.Player1.RaceToTarget);
        Assert.Equal("data:image/png;base64,AAAA", state.Player1.EndCapIcon);
        Assert.Equal("Bob", state.Player2.Name);
        Assert.Equal(BallGroup.Unassigned, state.Player1.BallGroup);
        Assert.Equal(BallGroup.Unassigned, state.Player2.BallGroup);
    }

    [Fact]
    public void ResetGame_KeepsTheOperatorsColorThemeStyleAndVisibility()
    {
        var manager = NewMatch(GameType.EightBall);
        manager.SetColorTheme(new ColorTheme
        {
            HomeBackground = "#111111",
            AwayBackground = "#222222",
            HomeAccent = "#00ff00",
            AwayAccent = "#ff00ff",
            Text = "#eeeeee"
        });
        manager.SetScoreboardStyle(new ScoreboardStyle
        {
            CornerRoundness = 40,
            OverallScale = 150,
            GlossyFinish = false,
            EndCapStyle = EndCapStyle.Hidden,
            ShooterIndicatorStyle = ShooterIndicatorStyle.Both
        });
        manager.SetScoreBarVisible(false);
        manager.SetBallTrackerVisible(false);
        manager.SetWinnerBannerVisible(false);
        manager.AddPoint(true);

        // A reset ends the match; it doesn't restyle the show.
        manager.ResetGame();

        var state = manager.GetStateSnapshot();
        Assert.False(state.IsGameActive);
        Assert.Equal(0, state.Player1Score);

        Assert.Equal("#111111", state.ColorTheme.HomeBackground);
        Assert.Equal("#222222", state.ColorTheme.AwayBackground);
        Assert.Equal("#00ff00", state.ColorTheme.HomeAccent);
        Assert.Equal("#ff00ff", state.ColorTheme.AwayAccent);
        Assert.Equal("#eeeeee", state.ColorTheme.Text);

        Assert.Equal(40, state.ScoreboardStyle.CornerRoundness);
        Assert.Equal(150, state.ScoreboardStyle.OverallScale);
        Assert.False(state.ScoreboardStyle.GlossyFinish);
        Assert.Equal(EndCapStyle.Hidden, state.ScoreboardStyle.EndCapStyle);
        Assert.Equal(ShooterIndicatorStyle.Both, state.ScoreboardStyle.ShooterIndicatorStyle);

        Assert.False(state.Visibility.ScoreBarVisible);
        Assert.False(state.Visibility.BallTrackerVisible);
        Assert.False(state.Visibility.WinnerBannerVisible);

        // The survivors are copies: editing styling after the reset doesn't rewrite the snapshot.
        manager.SetScoreBarVisible(true);
        Assert.False(state.Visibility.ScoreBarVisible);
        Assert.True(manager.GetStateSnapshot().Visibility.ScoreBarVisible);
    }

    [Fact]
    public void ResetGame_KeepsTheChosenGameTypeAndRaceToMode()
    {
        var manager = NewMatch(raceToMode: RaceToMode.Split, homeRaceTo: 5, awayRaceTo: 7);
        manager.AddPoint(isPlayer1: true);

        // The overlay previews these two after a reset, so they have to survive it — otherwise the
        // preview falls back to the enum defaults (8-ball, single) while the setup screen still
        // shows what was selected, and an HTTP reset has no screen to correct it.
        manager.ResetGame();

        var state = manager.GetStateSnapshot();
        Assert.False(state.IsGameActive);
        Assert.Equal(0, state.Player1Score);
        Assert.Equal(GameType.NineBall, state.GameType);
        Assert.Equal(RaceToMode.Split, state.RaceToMode);
    }

    [Fact]
    public void InitializeGame_KeepsTheOperatorsColorThemeStyleAndVisibility()
    {
        var manager = new GameManager();
        manager.SetColorTheme(new ColorTheme { HomeBackground = "#111111", Text = "#eeeeee" });
        manager.SetScoreboardStyle(new ScoreboardStyle { CornerRoundness = 40, OverallScale = 150 });
        manager.SetScoreBarVisible(false);
        manager.SetBallTrackerVisible(false);

        // Starting a match resets the scoreboard's contents, not the operator's look — the same
        // rule ResetGame follows, so hiding the score bar before START MATCH sticks.
        manager.InitializeGame(GameType.NineBall, RaceToMode.Single);

        var state = manager.GetStateSnapshot();
        Assert.True(state.IsGameActive);
        Assert.Equal("#111111", state.ColorTheme.HomeBackground);
        Assert.Equal("#eeeeee", state.ColorTheme.Text);
        Assert.Equal(40, state.ScoreboardStyle.CornerRoundness);
        Assert.Equal(150, state.ScoreboardStyle.OverallScale);
        Assert.False(state.Visibility.ScoreBarVisible);
        Assert.False(state.Visibility.BallTrackerVisible);
        Assert.True(state.Visibility.WinnerBannerVisible);
    }

    [Theory]
    [InlineData(BallGroup.Solids, BallGroup.Stripes)]
    [InlineData(BallGroup.Stripes, BallGroup.Solids)]
    public void AssignBallGroup_EightBall_FlipsOtherPlayerToComplement(BallGroup assigned, BallGroup expectedOther)
    {
        var manager = NewMatch(GameType.EightBall);

        manager.AssignBallGroup(isHome: true, assigned);

        var state = manager.GetCurrentGameState();
        Assert.Equal(assigned, state.Player1.BallGroup);
        Assert.Equal(expectedOther, state.Player2.BallGroup);
    }

    [Fact]
    public void AssignBallGroup_IgnoredForNonEightBallGames()
    {
        var manager = NewMatch(GameType.NineBall);

        manager.AssignBallGroup(isHome: true, BallGroup.Solids);

        var state = manager.GetCurrentGameState();
        Assert.Equal(BallGroup.Unassigned, state.Player1.BallGroup);
        Assert.Equal(BallGroup.Unassigned, state.Player2.BallGroup);
    }

    [Fact]
    public void AssignBallGroup_ReassigningOtherPlayerFlipsBothBack()
    {
        var manager = NewMatch(GameType.EightBall);

        manager.AssignBallGroup(isHome: true, BallGroup.Solids);
        manager.AssignBallGroup(isHome: false, BallGroup.Solids);

        var state = manager.GetCurrentGameState();
        Assert.Equal(BallGroup.Stripes, state.Player1.BallGroup);
        Assert.Equal(BallGroup.Solids, state.Player2.BallGroup);
    }

    [Fact]
    public void PocketBallAndUnpocketBall_ToggleMembershipInPocketedSet()
    {
        var manager = NewMatch();

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
        var manager = NewMatch();

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
        var manager = NewMatch(homeRaceTo: 10_000, awayRaceTo: 10_000);

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
        var manager = NewMatch(homeRaceTo: 10_000, awayRaceTo: 10_000);

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
        var manager = NewMatch();
        var theme = new ColorTheme { HomeBackground = "#000000", AwayBackground = "#222222", HomeAccent = "#ff0000", AwayAccent = "#0000ff", Text = "#ffffff" };

        manager.SetColorTheme(theme);

        var stored = manager.GetCurrentGameState().ColorTheme;
        Assert.Equal("#000000", stored.HomeBackground);
        Assert.Equal("#222222", stored.AwayBackground);
        Assert.Equal("#ff0000", stored.HomeAccent);
        Assert.Equal("#0000ff", stored.AwayAccent);
        Assert.Equal("#ffffff", stored.Text);

        // Stored as a copy, like the scoreboard style: a caller holding its own instance can't keep
        // editing state off the lock, with no change event, after this returns.
        Assert.NotSame(theme, stored);
        theme.HomeBackground = "#ff00ff";
        Assert.Equal("#000000", manager.GetStateSnapshot().ColorTheme.HomeBackground);
    }

    [Fact]
    public void ResetBalls_ClearsPocketedBalls()
    {
        var manager = NewMatch();
        manager.PocketBall(1);
        manager.PocketBall(2);

        manager.ResetBalls();

        Assert.Empty(manager.GetCurrentGameState().PocketedBalls);
    }

    [Fact]
    public void InitializeGame_DefaultsToFullyRoundedVisibleStyle()
    {
        var manager = NewMatch();

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
        var manager = NewMatch();
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
        var manager = NewMatch();

        manager.SetScoreboardStyle(new ScoreboardStyle { CornerRoundness = input });

        Assert.Equal(expected, manager.GetCurrentGameState().ScoreboardStyle.CornerRoundness);
    }

    [Theory]
    [InlineData(10, 50)]
    [InlineData(500, 200)]
    public void SetScoreboardStyle_ClampsOverallScaleToValidRange(int input, int expected)
    {
        var manager = NewMatch();

        manager.SetScoreboardStyle(new ScoreboardStyle { OverallScale = input });

        Assert.Equal(expected, manager.GetCurrentGameState().ScoreboardStyle.OverallScale);
    }

    [Fact]
    public void SetScoreBarVisible_UpdatesVisibilityAndRaisesGameStateChanged()
    {
        var manager = NewMatch();
        var raised = 0;
        manager.GameStateChanged += (_, _) => raised++;

        manager.SetScoreBarVisible(false);

        Assert.False(manager.GetCurrentGameState().Visibility.ScoreBarVisible);
        Assert.Equal(1, raised);
    }

    [Fact]
    public void SetBallTrackerVisible_UpdatesVisibility()
    {
        var manager = NewMatch();

        manager.SetBallTrackerVisible(false);

        Assert.False(manager.GetCurrentGameState().Visibility.BallTrackerVisible);
    }

    [Fact]
    public void SetMatchPreview_UpdatesGameTypeAndRaceToMode_BeforeMatchStarts()
    {
        var manager = new GameManager();

        manager.SetMatchPreview(GameType.EightBall, RaceToMode.Split);

        var state = manager.GetCurrentGameState();
        Assert.Equal(GameType.EightBall, state.GameType);
        Assert.Equal(RaceToMode.Split, state.RaceToMode);
        Assert.False(state.IsGameActive);
    }

    [Fact]
    public void SetMatchPreview_DoesNothingWhileGameIsActive()
    {
        var manager = NewMatch();

        manager.SetMatchPreview(GameType.TenBall, RaceToMode.Split);

        var state = manager.GetCurrentGameState();
        Assert.Equal("Alice", state.Player1.Name);
        Assert.Equal("Bob", state.Player2.Name);
        Assert.Equal(GameType.NineBall, state.GameType);
        Assert.Equal(RaceToMode.Single, state.RaceToMode);
    }

    [Fact]
    public void SetPlayerDetails_LandsInTheSnapshotTheOverlaySerializes()
    {
        const string icon = "data:image/png;base64,AAAA";
        var manager = new GameManager();

        manager.SetPlayerDetails(isHome: true, "Alice", "Blue Cuers", raceToTarget: 7, icon);
        manager.SetPlayerDetails(isHome: false, "Bob", "Red Rackers", raceToTarget: 7, endCapIcon: null);

        // GetStateSnapshot() is what the overlay's state endpoint serializes: these are the fields
        // it renders, plus the icon the end-cap URL points at.
        var snapshot = manager.GetStateSnapshot();
        Assert.Equal("Alice", snapshot.Player1.Name);
        Assert.Equal("Blue Cuers", snapshot.Player1.TeamName);
        Assert.Equal(7, snapshot.Player1.RaceToTarget);
        Assert.Equal(icon, snapshot.Player1.EndCapIcon);
        Assert.Equal(1, snapshot.Player1.EndCapIconVersion);

        Assert.Equal("Bob", snapshot.Player2.Name);
        Assert.Equal("Red Rackers", snapshot.Player2.TeamName);
        Assert.Equal(7, snapshot.Player2.RaceToTarget);
        Assert.Null(snapshot.Player2.EndCapIcon);
        Assert.Equal(0, snapshot.Player2.EndCapIconVersion);

        // The snapshot is a point-in-time copy, not a window onto the live player.
        manager.SetPlayerDetails(isHome: true, "Carol", "Blue Cuers", raceToTarget: 7, icon);
        Assert.Equal("Alice", snapshot.Player1.Name);
    }

    [Fact]
    public void SetPlayerDetails_ValuesSurviveStartingTheMatchFromTheStatePlayers()
    {
        var manager = new GameManager();
        manager.SetPlayerDetails(isHome: true, "Alice", "Blue Cuers", raceToTarget: 9, "data:image/png;base64,AAAA");
        manager.SetPlayerDetails(isHome: false, "Bob", "Red Rackers", raceToTarget: 9, endCapIcon: null);

        // This is the setup screen's START MATCH path: no players are passed in, the state players
        // the screen has been writing are the ones that play.
        manager.InitializeGame(GameType.NineBall, RaceToMode.Single);

        var state = manager.GetStateSnapshot();
        Assert.True(state.IsGameActive);
        Assert.Equal("Alice", state.Player1.Name);
        Assert.Equal("Blue Cuers", state.Player1.TeamName);
        Assert.Equal(9, state.Player1.RaceToTarget);
        Assert.Equal("data:image/png;base64,AAAA", state.Player1.EndCapIcon);
        Assert.Equal("Bob", state.Player2.Name);
        Assert.Equal(9, state.Player2.RaceToTarget);
        Assert.Same(state.Player1, state.CurrentShooter);
        Assert.Equal(0, state.Player1Score);
    }

    [Fact]
    public void SetPlayerDetails_OnlyBumpsIconVersionWhenTheArtworkChanges()
    {
        var manager = new GameManager();
        manager.SetPlayerDetails(isHome: true, "A", string.Empty, raceToTarget: 5, "data:image/png;base64,AAAA");

        // Editing the name re-sends the same icon: no new version, so the overlay keeps its cached copy.
        manager.SetPlayerDetails(isHome: true, "Alice", string.Empty, raceToTarget: 5, "data:image/png;base64,AAAA");
        Assert.Equal(1, manager.GetStateSnapshot().Player1.EndCapIconVersion);

        manager.SetPlayerDetails(isHome: true, "Alice", string.Empty, raceToTarget: 5, "data:image/png;base64,BBBB");
        var changed = manager.GetStateSnapshot().Player1;
        Assert.Equal("data:image/png;base64,BBBB", changed.EndCapIcon);
        Assert.Equal(2, changed.EndCapIconVersion);
    }

    [Fact]
    public async Task SetPlayerDetails_KeepsNameAndIconConsistentInEverySnapshotUnderConcurrency()
    {
        const string iconA = "data:image/png;base64,AAAA";
        const string iconB = "data:image/png;base64,BBBB";
        var manager = new GameManager();
        manager.SetPlayerDetails(isHome: true, "Alice", string.Empty, raceToTarget: 5, iconA);

        var writers = new[]
        {
            Task.Run(() =>
            {
                for (var i = 0; i < 300; i++)
                    manager.SetPlayerDetails(isHome: true, "Alice", string.Empty, raceToTarget: 5, iconA);
            }),
            Task.Run(() =>
            {
                for (var i = 0; i < 300; i++)
                    manager.SetPlayerDetails(isHome: true, "Bob", string.Empty, raceToTarget: 5, iconB);
            })
        };

        var readers = Enumerable.Range(0, 2).Select(_ => Task.Run(() =>
        {
            for (var i = 0; i < 400; i++)
            {
                var player = manager.GetStateSnapshot().Player1;
                Assert.Equal(player.Name == "Alice" ? iconA : iconB, player.EndCapIcon);
            }
        }));

        await Task.WhenAll(writers.Concat(readers));
    }

    [Fact]
    public void SetWinnerBannerVisible_UpdatesVisibility()
    {
        var manager = NewMatch();

        manager.SetWinnerBannerVisible(false);

        Assert.False(manager.GetCurrentGameState().Visibility.WinnerBannerVisible);
    }

    [Fact]
    public void SetCueBallSpin_SetsValue()
    {
        var manager = NewMatch();

        manager.SetCueBallSpin(0.25, 0.75);

        var spin = manager.GetCurrentGameState().CueBallSpin;
        Assert.NotNull(spin);
        Assert.Equal(0.25, spin!.X);
        Assert.Equal(0.75, spin.Y);
    }

    [Fact]
    public void SetCueBallSpin_ClampsOutOfRangeValues()
    {
        var manager = NewMatch();

        manager.SetCueBallSpin(-0.5, 1.5);

        var spin = manager.GetCurrentGameState().CueBallSpin;
        Assert.NotNull(spin);
        Assert.Equal(0.0, spin!.X);
        Assert.Equal(1.0, spin.Y);
    }

    [Fact]
    public void ClearCueBallSpin_ClearsValue()
    {
        var manager = NewMatch();
        manager.SetCueBallSpin(0.5, 0.5);

        manager.ClearCueBallSpin();

        Assert.Null(manager.GetCurrentGameState().CueBallSpin);
    }

    [Fact]
    public void AddPoint_ClearsCueBallSpin()
    {
        var manager = NewMatch();
        manager.SetCueBallSpin(0.5, 0.5);

        manager.AddPoint(isPlayer1: true);

        Assert.Null(manager.GetCurrentGameState().CueBallSpin);
    }
}
