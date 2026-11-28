using System.Collections.Concurrent;
using PoolScoreboard.Core.Enums;
using PoolScoreboard.Core.Models;

namespace PoolScoreboard.Core.Tests;

public class GameManagerTests
{
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
    public void StartShotClock_SetsTheDeadlineToNowPlusTheShotLength()
    {
        var now = new DateTimeOffset(2026, 11, 27, 20, 0, 0, TimeSpan.FromHours(-5));
        var manager = new GameManager(() => now);
        manager.SetPlayerDetails(isHome: true, "Alice", string.Empty, 2, endCapIcon: null);
        manager.SetPlayerDetails(isHome: false, "Bob", string.Empty, 2, endCapIcon: null);
        manager.InitializeGame(GameType.NineBall, RaceToMode.Single);

        manager.StartShotClock();

        // A deadline, not a countdown: the overlay ticks it out itself, so the only thing to get right here is
        // which instant the shot runs out.
        Assert.Equal(now.AddSeconds(GameState.DefaultShotClockSeconds), manager.GetStateSnapshot().ShotClockEndsAt);
    }

    [Fact]
    public void StartShotClock_RestartsFromTheMomentItIsPressed()
    {
        var now = new DateTimeOffset(2026, 11, 27, 20, 0, 0, TimeSpan.FromHours(-5));
        var manager = new GameManager(() => now);
        manager.SetPlayerDetails(isHome: true, "Alice", string.Empty, 2, endCapIcon: null);
        manager.SetPlayerDetails(isHome: false, "Bob", string.Empty, 2, endCapIcon: null);
        manager.InitializeGame(GameType.NineBall, RaceToMode.Single);

        manager.StartShotClock();
        now = now.AddSeconds(12);
        manager.StartShotClock();

        // The shot gets its full time again rather than the twelve seconds already spent counting against it.
        Assert.Equal(now.AddSeconds(GameState.DefaultShotClockSeconds), manager.GetStateSnapshot().ShotClockEndsAt);
    }

    [Fact]
    public void StartShotClock_IsIgnoredAtSetup()
    {
        var manager = new GameManager(() => DateTimeOffset.Now);

        manager.StartShotClock();

        // Setup is not a shot; a clock started there would be one nobody is playing to.
        Assert.Null(manager.GetStateSnapshot().ShotClockEndsAt);
    }

    [Fact]
    public void APoint_RestartsARunningClock_ButNeverStartsOne()
    {
        var now = new DateTimeOffset(2026, 11, 27, 20, 0, 0, TimeSpan.FromHours(-5));
        var manager = new GameManager(() => now);
        manager.SetPlayerDetails(isHome: true, "Alice", string.Empty, 5, endCapIcon: null);
        manager.SetPlayerDetails(isHome: false, "Bob", string.Empty, 5, endCapIcon: null);
        manager.InitializeGame(GameType.NineBall, RaceToMode.Single);

        manager.StartShotClock();
        now = now.AddSeconds(5);
        manager.AddPoint(isPlayer1: true);

        // The point ends one shot and the next one is now on the clock.
        Assert.Equal(now.AddSeconds(GameState.DefaultShotClockSeconds), manager.GetStateSnapshot().ShotClockEndsAt);

        // A match nobody started a clock on stays without one: the first point must not sprout a clock halfway
        // through a stream the operator had chosen to run without it.
        var quiet = new GameManager(() => now);
        quiet.SetPlayerDetails(isHome: true, "Alice", string.Empty, 5, endCapIcon: null);
        quiet.SetPlayerDetails(isHome: false, "Bob", string.Empty, 5, endCapIcon: null);
        quiet.InitializeGame(GameType.NineBall, RaceToMode.Single);

        quiet.AddPoint(isPlayer1: true);

        Assert.Null(quiet.GetStateSnapshot().ShotClockEndsAt);
    }

    [Fact]
    public void ANewRackLeavesTheClockIdleAtItsFullLength_AndAResetHidesIt()
    {
        var manager = NewMatch();
        manager.StartShotClock();

        manager.ResetBalls();

        // The previous shot's countdown has nothing left to time, but the new rack is not started either: the clock
        // waits at its full length until the operator starts it after the break.
        var rack = manager.GetStateSnapshot();
        Assert.Null(rack.ShotClockEndsAt);
        Assert.True(rack.ShotClockArmed);

        // And nothing starts it on its own — not a ball going down and not the other player taking the table.
        manager.PocketBall(4);
        manager.ToggleCurrentShooter();
        Assert.Null(manager.GetStateSnapshot().ShotClockEndsAt);
        Assert.True(manager.GetStateSnapshot().ShotClockArmed);

        manager.StartShotClock();
        Assert.NotNull(manager.GetStateSnapshot().ShotClockEndsAt);
        Assert.False(manager.GetStateSnapshot().ShotClockArmed);

        // Armed again, so the reset below is judged against a clock that was really waiting.
        manager.ResetBalls();
        manager.ResetGame();

        // A reset ends the match, so it must not leave a clock waiting on the setup screen either.
        var reset = manager.GetStateSnapshot();
        Assert.Null(reset.ShotClockEndsAt);
        Assert.False(reset.ShotClockArmed);
    }

    [Fact]
    public void AnIdleClock_IsHiddenByTheClearRouteToo()
    {
        var manager = NewMatch();
        manager.ResetBalls();

        manager.ClearShotClock();

        // Hiding has to cover the waiting clock as well as the running one, or a Stream Deck hide button would do
        // nothing after a new rack.
        Assert.False(manager.GetStateSnapshot().ShotClockArmed);
    }

    [Fact]
    public void AWonMatch_DoesNotLeaveAnIdleClockBehind()
    {
        var manager = NewMatch(homeRaceTo: 1, awayRaceTo: 1);
        manager.ResetBalls();
        manager.AddPoint(isPlayer1: true);

        var won = manager.GetStateSnapshot();
        Assert.NotNull(won.Winner);
        Assert.Null(won.ShotClockEndsAt);
        Assert.False(won.ShotClockArmed);
    }

    [Fact]
    public void AShotClockLengthSetAtSetup_IsWhatTheMatchCountsDownFrom()
    {
        var now = new DateTimeOffset(2026, 11, 28, 20, 0, 0, TimeSpan.FromHours(-5));
        var manager = new GameManager(() => now);
        manager.SetPlayerDetails(isHome: true, "Alice", string.Empty, 2, endCapIcon: null);
        manager.SetPlayerDetails(isHome: false, "Bob", string.Empty, 2, endCapIcon: null);

        manager.SetShotClockLengths(shotClockSeconds: 25, extensionSeconds: 10);
        manager.InitializeGame(GameType.NineBall, RaceToMode.Single);
        manager.StartShotClock();

        // A setup choice, so it has to survive the START MATCH that begins the match it was chosen for — a fresh
        // state would quietly count down from the default forty instead.
        Assert.Equal(now.AddSeconds(25), manager.GetStateSnapshot().ShotClockEndsAt);
    }

    [Fact]
    public void TheSetupLengths_AreClampedToWhatTheClockCanUse()
    {
        var manager = new GameManager();

        manager.SetShotClockLengths(shotClockSeconds: 0, extensionSeconds: 10_000);

        // Clamped rather than refused: a slipped digit leaves a clock that can be read, not a match without one.
        var state = manager.GetStateSnapshot();
        Assert.Equal(GameState.MinShotClockSeconds, state.ShotClockSeconds);
        Assert.Equal(GameState.MaxShotClockSeconds, state.ShotClockExtensionSeconds);
    }

    [Fact]
    public void ALengthChangedMidMatch_IsIgnored()
    {
        var manager = NewMatch();

        manager.SetShotClockLengths(shotClockSeconds: 10, extensionSeconds: 5);

        // The clock on screen is counting down for a player; moving the length under them would be a different
        // shot than the one they started.
        var state = manager.GetStateSnapshot();
        Assert.Equal(GameState.DefaultShotClockSeconds, state.ShotClockSeconds);
        Assert.Equal(GameState.DefaultShotClockExtensionSeconds, state.ShotClockExtensionSeconds);
    }

    [Fact]
    public void AnExtension_AddsItsTimeToTheRunningClock_OncePerPlayerPerRack()
    {
        var now = new DateTimeOffset(2026, 11, 28, 20, 0, 0, TimeSpan.FromHours(-5));
        var manager = new GameManager(() => now);
        manager.SetPlayerDetails(isHome: true, "Alice", string.Empty, 5, endCapIcon: null);
        manager.SetPlayerDetails(isHome: false, "Bob", string.Empty, 5, endCapIcon: null);
        manager.SetShotClockLengths(shotClockSeconds: 30, extensionSeconds: 15);
        manager.InitializeGame(GameType.NineBall, RaceToMode.Single);

        manager.StartShotClock();
        now = now.AddSeconds(10);
        manager.ExtendShotClock();

        // More time on the shot in progress, not a fresh shot: the ten seconds already spent stay spent.
        Assert.Equal(now.AddSeconds(20 + 15), manager.GetStateSnapshot().ShotClockEndsAt);

        manager.ExtendShotClock();

        // The same player's second call changes nothing: the rule is one each per rack.
        Assert.Equal(now.AddSeconds(35), manager.GetStateSnapshot().ShotClockEndsAt);

        manager.ToggleCurrentShooter();
        manager.ExtendShotClock();

        // The other player still had theirs — the shooter change restarts the clock at thirty, and this adds
        // fifteen to it.
        Assert.Equal(now.AddSeconds(45), manager.GetStateSnapshot().ShotClockEndsAt);

        manager.ResetBalls();
        manager.StartShotClock();
        manager.ExtendShotClock();

        // A new rack hands both of them one back.
        Assert.Equal(now.AddSeconds(45), manager.GetStateSnapshot().ShotClockEndsAt);
    }

    [Fact]
    public void AnExtension_WithoutAClockRunning_IsNotSpent()
    {
        var now = new DateTimeOffset(2026, 11, 28, 20, 0, 0, TimeSpan.FromHours(-5));
        var manager = new GameManager(() => now);
        manager.SetPlayerDetails(isHome: true, "Alice", string.Empty, 5, endCapIcon: null);
        manager.SetPlayerDetails(isHome: false, "Bob", string.Empty, 5, endCapIcon: null);
        manager.InitializeGame(GameType.NineBall, RaceToMode.Single);

        manager.ExtendShotClock();

        // Nothing to add time to, so nothing happens and nothing is given up.
        var idle = manager.GetStateSnapshot();
        Assert.Null(idle.ShotClockEndsAt);
        Assert.False(idle.HomeShotClockExtensionUsed);

        manager.StartShotClock();
        manager.ExtendShotClock();

        Assert.Equal(
            now.AddSeconds(GameState.DefaultShotClockSeconds + GameState.DefaultShotClockExtensionSeconds),
            manager.GetStateSnapshot().ShotClockEndsAt);
    }

    [Fact]
    public void APointThatWinsTheMatch_HidesTheClockInsteadOfRestartingIt()
    {
        var now = new DateTimeOffset(2026, 11, 28, 20, 0, 0, TimeSpan.FromHours(-5));
        var manager = new GameManager(() => now);
        manager.SetPlayerDetails(isHome: true, "Alice", string.Empty, 1, endCapIcon: null);
        manager.SetPlayerDetails(isHome: false, "Bob", string.Empty, 1, endCapIcon: null);
        manager.InitializeGame(GameType.NineBall, RaceToMode.Single);
        manager.StartShotClock();

        manager.AddPoint(isPlayer1: true);

        // The winning point ends the match, not just the shot: the clock goes with the winner banner rather than
        // counting down behind it.
        var won = manager.GetStateSnapshot();
        Assert.NotNull(won.Winner);
        Assert.Null(won.ShotClockEndsAt);

        // And it cannot be put back up behind the banner, by either route.
        manager.StartShotClock();
        manager.ExtendShotClock();
        Assert.Null(manager.GetStateSnapshot().ShotClockEndsAt);
    }

    [Fact]
    public void PocketingABall_RestartsARunningClock_AndBringingItBackDoesNot()
    {
        var now = new DateTimeOffset(2026, 11, 28, 20, 0, 0, TimeSpan.FromHours(-5));
        var manager = new GameManager(() => now);
        manager.SetPlayerDetails(isHome: true, "Alice", string.Empty, 5, endCapIcon: null);
        manager.SetPlayerDetails(isHome: false, "Bob", string.Empty, 5, endCapIcon: null);
        manager.InitializeGame(GameType.NineBall, RaceToMode.Single);
        manager.StartShotClock();

        now = now.AddSeconds(5);
        manager.PocketBall(3);

        // A ball leaving the table ends the shot it was potted in, the same way a point does.
        Assert.Equal(now.AddSeconds(GameState.DefaultShotClockSeconds), manager.GetStateSnapshot().ShotClockEndsAt);

        now = now.AddSeconds(5);
        manager.UnpocketBall(3);

        // Putting a ball back is the operator correcting a mistake, not a shot: five seconds of this one are gone.
        Assert.Equal(
            now.AddSeconds(GameState.DefaultShotClockSeconds - 5), manager.GetStateSnapshot().ShotClockEndsAt);

        now = now.AddSeconds(5);
        manager.ToggleBall(3);

        // The Stream Deck's per-ball button is that same shot when it pockets a ball...
        Assert.Equal(now.AddSeconds(GameState.DefaultShotClockSeconds), manager.GetStateSnapshot().ShotClockEndsAt);

        now = now.AddSeconds(5);
        manager.ToggleBall(3);

        // ...and a correction when it brings one back.
        Assert.Equal(
            now.AddSeconds(GameState.DefaultShotClockSeconds - 5), manager.GetStateSnapshot().ShotClockEndsAt);

        manager.PocketBall(4);
        now = now.AddSeconds(5);
        var beforeRepeat = manager.GetStateSnapshot().ShotClockEndsAt;
        manager.PocketBall(4);

        // Marking an already-pocketed ball again is not a shot either.
        Assert.Equal(beforeRepeat, manager.GetStateSnapshot().ShotClockEndsAt);
    }

    [Theory]
    [InlineData(GameType.EightBall, 8)]
    [InlineData(GameType.NineBall, 9)]
    [InlineData(GameType.TenBall, 10)]
    public void PocketingTheMoneyBall_StopsTheClock_WhileAnyOtherBallRestartsIt(GameType gameType, int moneyBall)
    {
        var now = new DateTimeOffset(2026, 11, 28, 20, 0, 0, TimeSpan.FromHours(-5));
        var manager = new GameManager(() => now);
        manager.SetPlayerDetails(isHome: true, "Alice", string.Empty, 5, endCapIcon: null);
        manager.SetPlayerDetails(isHome: false, "Bob", string.Empty, 5, endCapIcon: null);
        manager.InitializeGame(gameType, RaceToMode.Single);

        Assert.Equal(moneyBall, manager.GetStateSnapshot().MoneyBallNumber);

        manager.StartShotClock();
        manager.PocketBall(moneyBall);

        // The rack is over: there is no next shot for a countdown to time, so it stops rather than restarting over
        // the end of the game.
        Assert.Null(manager.GetStateSnapshot().ShotClockEndsAt);

        // A ball that is not the money ball still ends only the shot, so the clock restarts for the next one.
        manager.StartShotClock();
        now = now.AddSeconds(5);
        manager.PocketBall(1);

        Assert.Equal(now.AddSeconds(GameState.DefaultShotClockSeconds), manager.GetStateSnapshot().ShotClockEndsAt);

        // A clock left waiting by a new rack goes with the rack too: there is no shot left for it to be waiting on.
        manager.ResetBalls();
        manager.PocketBall(moneyBall);

        Assert.False(manager.GetStateSnapshot().ShotClockArmed);
    }

    [Fact]
    public void ThePerBallToggle_StopsTheClockWhenItPocketsTheMoneyBall()
    {
        var manager = NewMatch(gameType: GameType.NineBall);
        manager.StartShotClock();

        // The Stream Deck's per-ball button has to follow the same rule as the Controller's ball buttons.
        manager.ToggleBall(9);

        Assert.Null(manager.GetStateSnapshot().ShotClockEndsAt);

        // Even on a match whose game type is not the one the ball row is showing, the money ball is the game's own.
        var eightBall = NewMatch(gameType: GameType.EightBall);
        eightBall.StartShotClock();
        eightBall.ToggleBall(9);

        Assert.NotNull(eightBall.GetStateSnapshot().ShotClockEndsAt);
    }

    [Fact]
    public void TogglingTheShooter_RestartsARunningClock()
    {
        var now = new DateTimeOffset(2026, 11, 28, 20, 0, 0, TimeSpan.FromHours(-5));
        var manager = new GameManager(() => now);
        manager.SetPlayerDetails(isHome: true, "Alice", string.Empty, 5, endCapIcon: null);
        manager.SetPlayerDetails(isHome: false, "Bob", string.Empty, 5, endCapIcon: null);
        manager.InitializeGame(GameType.NineBall, RaceToMode.Single);
        manager.StartShotClock();

        now = now.AddSeconds(12);
        manager.ToggleCurrentShooter();

        // The other player is at the table, so the clock restarts for the shot they are about to play.
        Assert.Equal(now.AddSeconds(GameState.DefaultShotClockSeconds), manager.GetStateSnapshot().ShotClockEndsAt);
    }

    [Fact]
    public void TheShotClockDisplay_CanBeSwitchedMidMatch_AndSurvivesTheNextState()
    {
        var manager = NewMatch();

        manager.SetShotClockDisplay(ShotClockDisplay.Line);

        // A display choice rather than a rule, so unlike the lengths it applies to the clock already on screen.
        Assert.Equal(ShotClockDisplay.Line, manager.GetStateSnapshot().ShotClockDisplay);

        manager.ResetGame();

        // And it describes the show, not the match: a reset and the next match both keep it, the way the colour
        // theme and the show/hide settings are kept.
        Assert.Equal(ShotClockDisplay.Line, manager.GetStateSnapshot().ShotClockDisplay);

        manager.InitializeGame(GameType.NineBall, RaceToMode.Single);
        Assert.Equal(ShotClockDisplay.Line, manager.GetStateSnapshot().ShotClockDisplay);
    }

    [Fact]
    public void InitializeGame_SetsUpActiveGameWithPlayer1AsCurrentShooter()
    {
        var manager = NewMatch();

        var state = manager.GetStateSnapshot();
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

        var state = manager.GetStateSnapshot();
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

        var state = manager.GetStateSnapshot();
        Assert.True(state.IsGameActive);
        Assert.Same(state.Player1, state.Winner);
    }

    [Fact]
    public void AddPoint_SplitMode_PlayersWinAtDifferentTargets()
    {
        var manager = NewMatch(raceToMode: RaceToMode.Split, homeRaceTo: 2, awayRaceTo: 5);

        manager.AddPoint(true);
        manager.AddPoint(true);

        var state = manager.GetStateSnapshot();
        Assert.True(state.IsGameActive);
        Assert.Same(state.Player1, state.Winner);
        Assert.Equal(2, state.Player1Score);
    }

    [Fact]
    public void AddPoint_StillWorksAfterAPlayerReachesRaceToTarget()
    {
        var manager = NewMatch(raceToMode: RaceToMode.Split, homeRaceTo: 1);

        manager.AddPoint(true); // Alice wins immediately at race-to 1

        var won = manager.GetStateSnapshot();
        Assert.True(won.IsGameActive);
        Assert.Same(won.Player1, won.Winner);

        // The match must stay fully editable after a win, in case of a scoring mistake.
        manager.AddPoint(false);

        Assert.Equal(1, manager.GetStateSnapshot().Player2Score);
    }

    [Fact]
    public void ToggleCurrentShooter_StillWorksAfterAPlayerReachesRaceToTarget()
    {
        var manager = NewMatch(raceToMode: RaceToMode.Split, homeRaceTo: 1);

        manager.AddPoint(true); // Alice wins immediately at race-to 1

        manager.ToggleCurrentShooter();

        var state = manager.GetStateSnapshot();
        Assert.Same(state.Player2, state.CurrentShooter);
    }

    [Fact]
    public void SubtractPoint_DecrementsCorrectPlayersScore()
    {
        var manager = NewMatch();

        manager.AddPoint(isPlayer1: true);
        manager.AddPoint(isPlayer1: true);
        manager.AddPoint(isPlayer1: false);

        manager.SubtractPoint(isPlayer1: true);

        var state = manager.GetStateSnapshot();
        Assert.Equal(1, state.Player1Score);
        Assert.Equal(1, state.Player2Score);
    }

    [Fact]
    public void SubtractPoint_DoesNotGoBelowZero()
    {
        var manager = NewMatch();

        manager.SubtractPoint(isPlayer1: true);
        manager.SubtractPoint(isPlayer1: false);

        var state = manager.GetStateSnapshot();
        Assert.Equal(0, state.Player1Score);
        Assert.Equal(0, state.Player2Score);
    }

    [Fact]
    public void SubtractPoint_ClearsWinnerWhenScoreDropsBelowRaceToTarget()
    {
        var manager = NewMatch(raceToMode: RaceToMode.Split, homeRaceTo: 1);

        manager.AddPoint(isPlayer1: true); // Alice wins immediately at race-to 1
        Assert.NotNull(manager.GetStateSnapshot().Winner);

        manager.SubtractPoint(isPlayer1: true);

        var state = manager.GetStateSnapshot();
        Assert.Null(state.Winner);
        Assert.True(state.IsGameActive);
        Assert.Equal(0, state.Player1Score);
    }

    [Fact]
    public void SubtractPoint_DoesNotMakeALaterUndoGoNegative()
    {
        var manager = NewMatch();

        manager.AddPoint(isPlayer1: true);
        manager.SubtractPoint(isPlayer1: true);
        Assert.Equal(0, manager.GetStateSnapshot().Player1Score);

        // The point is still on the undo history; undoing it afterwards must not push the score
        // below zero just because the minus got there first.
        manager.UndoPoint();
        Assert.Equal(0, manager.GetStateSnapshot().Player1Score);
    }

    [Fact]
    public void UndoPoint_ReversesTheMostRecentlyScoredPoint_NotTheLeader()
    {
        var manager = NewMatch();

        manager.AddPoint(true);
        manager.AddPoint(true);
        manager.AddPoint(false); // away scores last, but home is still ahead

        manager.UndoPoint();

        var state = manager.GetStateSnapshot();
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
        Assert.Equal(1, manager.GetStateSnapshot().Player1Score);
        Assert.Equal(1, manager.GetStateSnapshot().Player2Score);

        manager.UndoPoint(); // undoes away's point
        Assert.Equal(1, manager.GetStateSnapshot().Player1Score);
        Assert.Equal(0, manager.GetStateSnapshot().Player2Score);
    }

    [Fact]
    public void UndoPoint_AtATie_ReversesTheMostRecentPoint()
    {
        var manager = NewMatch();

        manager.AddPoint(true);
        manager.AddPoint(false);

        manager.UndoPoint();

        var state = manager.GetStateSnapshot();
        Assert.Equal(1, state.Player1Score);
        Assert.Equal(0, state.Player2Score);
    }

    [Fact]
    public void UndoPoint_AfterTheWinningPoint_ClearsTheWinner()
    {
        var manager = NewMatch(raceToMode: RaceToMode.Split, homeRaceTo: 1);

        manager.AddPoint(true); // Alice reaches race-to 1
        Assert.NotNull(manager.GetStateSnapshot().Winner);

        manager.UndoPoint();

        var state = manager.GetStateSnapshot();
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

        var state = manager.GetStateSnapshot();
        Assert.False(state.IsGameActive);
        Assert.Equal(0, state.Player1Score);
        Assert.Null(state.Winner);
    }

    [Fact]
    public void UndoPoint_DoesNothingWhenNoPointsScored()
    {
        var manager = NewMatch();

        manager.UndoPoint();

        var state = manager.GetStateSnapshot();
        Assert.Equal(0, state.Player1Score);
        Assert.Equal(0, state.Player2Score);
    }

    [Fact]
    public void ResetGame_ClearsScoresWinnerAndCurrentShooter()
    {
        var manager = NewMatch();
        manager.AddPoint(true);

        manager.ResetGame();

        var state = manager.GetStateSnapshot();
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
            HomeText = "#eeeeee",
            AwayText = "#cccccc"
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
        Assert.Equal("#eeeeee", state.ColorTheme.HomeText);
        Assert.Equal("#cccccc", state.ColorTheme.AwayText);

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
        manager.SetColorTheme(new ColorTheme { HomeBackground = "#111111", HomeText = "#eeeeee", AwayText = "#cccccc" });
        manager.SetScoreboardStyle(new ScoreboardStyle { CornerRoundness = 40, OverallScale = 150 });
        manager.SetScoreBarVisible(false);
        manager.SetBallTrackerVisible(false);

        // Starting a match resets the scoreboard's contents, not the operator's look — the same
        // rule ResetGame follows, so hiding the score bar before START MATCH sticks.
        manager.InitializeGame(GameType.NineBall, RaceToMode.Single);

        var state = manager.GetStateSnapshot();
        Assert.True(state.IsGameActive);
        Assert.Equal("#111111", state.ColorTheme.HomeBackground);
        Assert.Equal("#eeeeee", state.ColorTheme.HomeText);
        Assert.Equal("#cccccc", state.ColorTheme.AwayText);
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

        var state = manager.GetStateSnapshot();
        Assert.Equal(assigned, state.Player1.BallGroup);
        Assert.Equal(expectedOther, state.Player2.BallGroup);
    }

    [Fact]
    public void AssignBallGroup_IgnoredForNonEightBallGames()
    {
        var manager = NewMatch(GameType.NineBall);

        manager.AssignBallGroup(isHome: true, BallGroup.Solids);

        var state = manager.GetStateSnapshot();
        Assert.Equal(BallGroup.Unassigned, state.Player1.BallGroup);
        Assert.Equal(BallGroup.Unassigned, state.Player2.BallGroup);
    }

    [Fact]
    public void AssignBallGroup_ReassigningOtherPlayerFlipsBothBack()
    {
        var manager = NewMatch(GameType.EightBall);

        manager.AssignBallGroup(isHome: true, BallGroup.Solids);
        manager.AssignBallGroup(isHome: false, BallGroup.Solids);

        var state = manager.GetStateSnapshot();
        Assert.Equal(BallGroup.Stripes, state.Player1.BallGroup);
        Assert.Equal(BallGroup.Solids, state.Player2.BallGroup);
    }

    [Fact]
    public void PocketBallAndUnpocketBall_ToggleMembershipInPocketedSet()
    {
        var manager = NewMatch();

        manager.PocketBall(7);
        manager.PocketBall(9);

        var state = manager.GetStateSnapshot();
        Assert.Contains(7, state.PocketedBalls);
        Assert.Contains(9, state.PocketedBalls);

        manager.UnpocketBall(7);

        state = manager.GetStateSnapshot();
        Assert.DoesNotContain(7, state.PocketedBalls);
        Assert.Contains(9, state.PocketedBalls);
    }

    [Fact]
    public void EndCapIconVersion_BumpsWheneverIconIsAssigned()
    {
        var player = new Player { Name = "Alice", RaceToTarget = 5 };

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

        // Mutate afterwards: everything the subscriber was handed must stay as it was. (There is no
        // live-state accessor to compare against any more — these value checks are what prove the
        // payload is detached, where Assert.NotSame used to.)
        manager.AddPoint(isPlayer1: false);
        manager.PocketBall(3);

        Assert.NotNull(raised);
        Assert.Equal(1, raised!.Player1Score);
        Assert.Equal(0, raised.Player2Score);
        Assert.Empty(raised.PocketedBalls);
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

        var state = manager.GetStateSnapshot();
        Assert.Equal(100, state.Player1Score);
        Assert.Equal(100, state.Player2Score);
    }

    [Fact]
    public void SetPlayerDetails_TrimsNamesAndStoresWhitespaceOnlyAsEmpty()
    {
        var manager = NewMatch();

        manager.SetPlayerDetails(isHome: true, "  Alice  ", "  Blue Cuers  ", 5, endCapIcon: null);

        var padded = manager.GetStateSnapshot();
        Assert.Equal("Alice", padded.Player1.Name);
        Assert.Equal("Blue Cuers", padded.Player1.TeamName);

        // The setup screen can be left holding spaces in a field, and every consumer (Controller
        // winner line, overlay pills and banner) relies on this being "" rather than whitespace.
        manager.SetPlayerDetails(isHome: true, "   ", " \t ", 5, endCapIcon: null);

        var blank = manager.GetStateSnapshot();
        Assert.Equal(string.Empty, blank.Player1.Name);
        Assert.Equal(string.Empty, blank.Player1.TeamName);
    }

    [Fact]
    public void WhitespaceOnlyWinnerNameIsStoredEmptySoTheBannerCanFallBack()
    {
        var manager = NewMatch(raceToMode: RaceToMode.Split, homeRaceTo: 1);
        manager.SetPlayerDetails(isHome: true, "   ", string.Empty, 1, endCapIcon: null);

        manager.AddPoint(isPlayer1: true);

        // The overlay maps WinnerName from this and shows the banner when it is non-null, so an
        // unnamed winner has to arrive as "" — whitespace here would render " WINS!".
        var state = manager.GetStateSnapshot();
        Assert.NotNull(state.Winner);
        Assert.Same(state.Player1, state.Winner);
        Assert.Equal(string.Empty, state.Winner!.Name);
    }

    [Fact]
    public void SetColorTheme_UpdatesGameStateColorTheme()
    {
        var manager = NewMatch();
        var theme = new ColorTheme { HomeBackground = "#000000", AwayBackground = "#222222", HomeAccent = "#ff0000", AwayAccent = "#0000ff", HomeText = "#ffffff", AwayText = "#dddddd" };

        manager.SetColorTheme(theme);

        var stored = manager.GetStateSnapshot().ColorTheme;
        Assert.Equal("#000000", stored.HomeBackground);
        Assert.Equal("#222222", stored.AwayBackground);
        Assert.Equal("#ff0000", stored.HomeAccent);
        Assert.Equal("#0000ff", stored.AwayAccent);
        Assert.Equal("#ffffff", stored.HomeText);
        Assert.Equal("#dddddd", stored.AwayText);

        // Stored as a copy, like the scoreboard style: a caller holding its own instance can't keep
        // editing state off the lock, with no change event, after this returns. That is what the
        // mutation below proves — a NotSame against a snapshot would pass either way, since every
        // snapshot hands out its own copies.
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

        Assert.Empty(manager.GetStateSnapshot().PocketedBalls);
    }

    [Fact]
    public void InitializeGame_DefaultsToFullyRoundedVisibleStyle()
    {
        var manager = NewMatch();

        var state = manager.GetStateSnapshot();
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

        var result = manager.GetStateSnapshot().ScoreboardStyle;
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

        Assert.Equal(expected, manager.GetStateSnapshot().ScoreboardStyle.CornerRoundness);
    }

    [Theory]
    [InlineData(10, 50)]
    [InlineData(500, 200)]
    public void SetScoreboardStyle_ClampsOverallScaleToValidRange(int input, int expected)
    {
        var manager = NewMatch();

        manager.SetScoreboardStyle(new ScoreboardStyle { OverallScale = input });

        Assert.Equal(expected, manager.GetStateSnapshot().ScoreboardStyle.OverallScale);
    }

    [Fact]
    public void SetScoreBarVisible_UpdatesVisibilityAndRaisesGameStateChanged()
    {
        var manager = NewMatch();
        var raised = 0;
        manager.GameStateChanged += (_, _) => raised++;

        manager.SetScoreBarVisible(false);

        Assert.False(manager.GetStateSnapshot().Visibility.ScoreBarVisible);
        Assert.Equal(1, raised);
    }

    [Fact]
    public void SetBallTrackerVisible_UpdatesVisibility()
    {
        var manager = NewMatch();

        manager.SetBallTrackerVisible(false);

        Assert.False(manager.GetStateSnapshot().Visibility.BallTrackerVisible);
    }

    [Fact]
    public void SetMatchPreview_UpdatesGameTypeAndRaceToMode_BeforeMatchStarts()
    {
        var manager = new GameManager();

        manager.SetMatchPreview(GameType.EightBall, RaceToMode.Split);

        var state = manager.GetStateSnapshot();
        Assert.Equal(GameType.EightBall, state.GameType);
        Assert.Equal(RaceToMode.Split, state.RaceToMode);
        Assert.False(state.IsGameActive);
    }

    [Fact]
    public void SetMatchPreview_DoesNothingWhileGameIsActive()
    {
        var manager = NewMatch();

        manager.SetMatchPreview(GameType.TenBall, RaceToMode.Split);

        var state = manager.GetStateSnapshot();
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

        Assert.False(manager.GetStateSnapshot().Visibility.WinnerBannerVisible);
    }

    [Fact]
    public void SetCueBallSpin_SetsValue()
    {
        var manager = NewMatch();

        manager.SetCueBallSpin(0.25, 0.75);

        var spin = manager.GetStateSnapshot().CueBallSpin;
        Assert.NotNull(spin);
        Assert.Equal(0.25, spin!.X);
        Assert.Equal(0.75, spin.Y);
    }

    [Fact]
    public void SetCueBallSpin_ClampsOutOfRangeValues()
    {
        var manager = NewMatch();

        manager.SetCueBallSpin(-0.5, 1.5);

        var spin = manager.GetStateSnapshot().CueBallSpin;
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

        Assert.Null(manager.GetStateSnapshot().CueBallSpin);
    }

    [Fact]
    public void AddPoint_ClearsCueBallSpin()
    {
        var manager = NewMatch();
        manager.SetCueBallSpin(0.5, 0.5);

        manager.AddPoint(isPlayer1: true);

        Assert.Null(manager.GetStateSnapshot().CueBallSpin);
    }
}
