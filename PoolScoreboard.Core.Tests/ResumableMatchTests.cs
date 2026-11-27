using PoolScoreboard.Core.Enums;
using PoolScoreboard.Core.Persistence;

namespace PoolScoreboard.Core.Tests;

/// <summary>
/// A restart mid-match has to be invisible on the overlay: scores, groups, pocketed balls and the shooter come
/// back, the winner is recomputed rather than trusted, and junk in the file can't poison the state.
/// </summary>
public sealed class ResumableMatchTests
{
    [Fact]
    public void Capture_LeavesNothingToResumeBeforeTheMatchStarts()
    {
        Assert.Null(ResumableMatch.Capture(new GameManager().GetStateSnapshot()));
    }

    [Fact]
    public void CaptureThenRestore_BringsTheMatchBack()
    {
        var manager = new GameManager();
        manager.SetPlayerDetails(isHome: true, "Home", "Team H", 5, null);
        manager.SetPlayerDetails(isHome: false, "Away", "Team A", 5, null);
        manager.InitializeGame(GameType.EightBall, RaceToMode.Split);
        manager.AddPoint(isPlayer1: true);
        manager.AddPoint(isPlayer1: true);
        manager.AddPoint(isPlayer1: false);
        manager.AssignBallGroup(isHome: true, BallGroup.Solids);
        manager.PocketBall(3);
        manager.PocketBall(11);
        manager.ToggleCurrentShooter();

        var saved = ResumableMatch.Capture(manager.GetStateSnapshot());
        Assert.NotNull(saved);

        // The shape of a crash and a fresh launch: the match is gone, the players stay (the profile restores
        // them), and the file brings the play back.
        manager.ResetGame();
        manager.RestoreMatch(saved);

        var restored = manager.GetStateSnapshot();
        Assert.True(restored.IsGameActive);
        Assert.Equal(GameType.EightBall, restored.GameType);
        Assert.Equal(RaceToMode.Split, restored.RaceToMode);
        Assert.Equal(2, restored.Player1Score);
        Assert.Equal(1, restored.Player2Score);
        Assert.Equal(BallGroup.Solids, restored.Player1.BallGroup);
        Assert.Equal(BallGroup.Stripes, restored.Player2.BallGroup);
        Assert.Equal(new[] { 3, 11 }, restored.PocketedBalls.OrderBy(number => number));
        Assert.Same(restored.Player2, restored.CurrentShooter);
        Assert.Equal("Home", restored.Player1.Name);
        Assert.Null(restored.Winner);
    }

    [Fact]
    public void Restore_KeepsTheMatchIdSoTheSameMatchIsNotFiledTwice()
    {
        var manager = new GameManager();
        manager.SetPlayerDetails(isHome: true, "Home", "", 3, null);
        manager.SetPlayerDetails(isHome: false, "Away", "", 3, null);
        manager.InitializeGame(GameType.EightBall, RaceToMode.Single);
        manager.AddPoint(isPlayer1: true);

        var id = manager.GetStateSnapshot().MatchId;
        Assert.False(string.IsNullOrEmpty(id));

        var saved = ResumableMatch.Capture(manager.GetStateSnapshot());
        Assert.NotNull(saved);
        Assert.Equal(id, saved.MatchId);

        manager.ResetGame();
        manager.RestoreMatch(saved);

        // The history keys a record on this id, so a restored match is recognisably the one already filed.
        Assert.Equal(id, manager.GetStateSnapshot().MatchId);
    }

    [Fact]
    public void EachNewMatchGetsItsOwnId()
    {
        var manager = new GameManager();
        manager.SetPlayerDetails(isHome: true, "Home", "", 2, null);
        manager.SetPlayerDetails(isHome: false, "Away", "", 2, null);

        manager.InitializeGame(GameType.EightBall, RaceToMode.Single);
        var first = manager.GetStateSnapshot().MatchId;

        manager.ResetGame();
        Assert.True(string.IsNullOrEmpty(manager.GetStateSnapshot().MatchId));

        manager.InitializeGame(GameType.EightBall, RaceToMode.Single);
        Assert.NotEqual(first, manager.GetStateSnapshot().MatchId);
    }

    [Fact]
    public void Restore_RecomputesTheWinner_AndIgnoresImpossibleBalls()
    {
        var manager = new GameManager();
        manager.SetPlayerDetails(isHome: true, "Home", "", 2, null);
        manager.SetPlayerDetails(isHome: false, "Away", "", 2, null);
        manager.InitializeGame(GameType.NineBall, RaceToMode.Single);
        manager.AddPoint(isPlayer1: true);
        manager.AddPoint(isPlayer1: true);

        var saved = ResumableMatch.Capture(manager.GetStateSnapshot())!;
        manager.ResetGame();

        // A hand-edited file (or one from a future layout): ball numbers off the table are dropped, a negative
        // score can't come back, and the winner follows the scores and the restored Race-To target.
        saved.PocketedBalls = new List<int> { 0, 3, 16 };
        saved.AwayScore = -4;
        manager.RestoreMatch(saved);

        var restored = manager.GetStateSnapshot();
        Assert.Equal(new[] { 3 }, restored.PocketedBalls);
        Assert.Equal(0, restored.Player2Score);
        Assert.Same(restored.Player1, restored.Winner);
    }

    [Fact]
    public void Restore_ClearsTheUndoHistoryItNoLongerHasPointsFor()
    {
        var manager = new GameManager();
        manager.InitializeGame(GameType.NineBall, RaceToMode.Single);
        manager.AddPoint(isPlayer1: true);
        manager.AddPoint(isPlayer1: false);

        var saved = ResumableMatch.Capture(manager.GetStateSnapshot())!;
        manager.ResetGame();
        manager.RestoreMatch(saved);

        // The two points are back on screen but their history entries are not, so undo must not take a point
        // off a match the operator just resumed — the first undo after a restart does nothing.
        manager.UndoPoint();
        Assert.Equal(1, manager.GetStateSnapshot().Player1Score);
        Assert.Equal(1, manager.GetStateSnapshot().Player2Score);
    }
}
