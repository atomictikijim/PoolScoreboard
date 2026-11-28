using PoolScoreboard.Core;
using PoolScoreboard.Core.Enums;
using PoolScoreboard.Core.Models;
using PoolScoreboard.Core.Persistence;

namespace PoolScoreboard.Core.Tests;

public class OperatorProfileTests
{
    private const string HomeIcon = "data:image/png;base64,AAAA";

    [Fact]
    public void OverlayPort_DefaultsToThePortTheAppHasAlwaysUsed() =>
        Assert.Equal(51234, new OperatorProfile().OverlayPort);

    [Theory]
    [InlineData(0, false)]
    [InlineData(80, false)]
    [InlineData(1023, false)]
    [InlineData(1024, true)]
    [InlineData(51234, true)]
    [InlineData(65535, true)]
    [InlineData(65536, false)]
    public void IsUsablePort_AcceptsOnlyPortsThatCanBeBoundWithoutElevation(int port, bool expected) =>
        Assert.Equal(expected, OperatorProfile.IsUsablePort(port));

    [Theory]
    [InlineData(51240, 51240)]
    [InlineData(1024, 1024)]
    [InlineData(80, 51234)]
    [InlineData(0, 51234)]
    public void NormalizePort_FallsBackToTheDefaultWhenTheSavedPortCannotBeBound(int saved, int expected) =>
        Assert.Equal(expected, OperatorProfile.NormalizePort(saved));

    private static GameManager ManagerWithMatch()
    {
        var manager = new GameManager();
        manager.SetPlayerDetails(isHome: true, "Alice", "Blue Cuers", 7, HomeIcon);
        manager.SetPlayerDetails(isHome: false, "Bob", "Red Rackers", 5, endCapIcon: null);
        manager.SetColorTheme(new ColorTheme
        {
            HomeBackground = "#101010",
            AwayBackground = "#202020",
            HomeAccent = "#00ff00",
            AwayAccent = "#ff00ff",
            HomeText = "#eeeeee",
            AwayText = "#cccccc"
        });
        manager.SetScoreboardStyle(new ScoreboardStyle
        {
            CornerRoundness = 20,
            OverallScale = 140,
            GlossyFinish = false,
            EndCapStyle = EndCapStyle.Hidden,
            ShooterIndicatorStyle = ShooterIndicatorStyle.Both
        });
        manager.SetScoreBarVisible(false);
        manager.SetBallTrackerVisible(true);
        manager.SetWinnerBannerVisible(false);
        manager.InitializeGame(GameType.NineBall, RaceToMode.Split);
        return manager;
    }

    [Fact]
    public void Capture_KeepsTheLookAndBothPlayersButNotTheMatch()
    {
        var manager = ManagerWithMatch();
        manager.AddPoint(isPlayer1: true);
        manager.PocketBall(3);
        manager.AssignBallGroup(true, BallGroup.Solids);

        var profile = OperatorProfile.Capture(manager.GetStateSnapshot());

        Assert.Equal("#101010", profile.ColorTheme.HomeBackground);
        Assert.Equal("#cccccc", profile.ColorTheme.AwayText);
        Assert.Equal(20, profile.ScoreboardStyle.CornerRoundness);
        Assert.Equal(ShooterIndicatorStyle.Both, profile.ScoreboardStyle.ShooterIndicatorStyle);
        Assert.False(profile.Visibility.ScoreBarVisible);
        Assert.False(profile.Visibility.WinnerBannerVisible);

        Assert.Equal("Alice", profile.HomePlayer.Name);
        Assert.Equal("Blue Cuers", profile.HomePlayer.TeamName);
        Assert.Equal(7, profile.HomePlayer.RaceToTarget);
        Assert.Equal("Bob", profile.AwayPlayer.Name);
        Assert.Equal(5, profile.AwayPlayer.RaceToTarget);
    }

    [Fact]
    public void ApplyTo_RestoresALookAndPlayersIntoAFreshManager()
    {
        var saved = OperatorProfile.Capture(ManagerWithMatch().GetStateSnapshot());

        var restored = new GameManager();
        saved.ApplyTo(restored, HomeIcon, awayEndCapIcon: null);

        var state = restored.GetStateSnapshot();
        Assert.Equal("#101010", state.ColorTheme.HomeBackground);
        Assert.Equal("#202020", state.ColorTheme.AwayBackground);
        Assert.Equal("#ff00ff", state.ColorTheme.AwayAccent);
        Assert.Equal("#cccccc", state.ColorTheme.AwayText);
        Assert.Equal(20, state.ScoreboardStyle.CornerRoundness);
        Assert.Equal(140, state.ScoreboardStyle.OverallScale);
        Assert.False(state.ScoreboardStyle.GlossyFinish);
        Assert.Equal(EndCapStyle.Hidden, state.ScoreboardStyle.EndCapStyle);
        Assert.False(state.Visibility.ScoreBarVisible);
        Assert.True(state.Visibility.BallTrackerVisible);
        Assert.False(state.Visibility.WinnerBannerVisible);

        Assert.Equal("Alice", state.Player1.Name);
        Assert.Equal("Blue Cuers", state.Player1.TeamName);
        Assert.Equal(7, state.Player1.RaceToTarget);
        Assert.Equal(HomeIcon, state.Player1.EndCapIcon);
        Assert.Equal("Bob", state.Player2.Name);
        Assert.Null(state.Player2.EndCapIcon);

        // A restored profile is setup only: no match is in progress and nothing is scored.
        Assert.False(state.IsGameActive);
        Assert.Empty(state.PocketedBalls);
        Assert.Equal(0, state.Player1Score);
        Assert.Null(state.Winner);
    }

    [Fact]
    public void TheShotClockDisplayIsRememberedWithTheLook()
    {
        var manager = ManagerWithMatch();
        manager.SetShotClockDisplay(ShotClockDisplay.Line);

        var saved = OperatorProfile.Capture(manager.GetStateSnapshot());
        Assert.Equal(ShotClockDisplay.Line, saved.ShotClockDisplay);

        var restored = new GameManager();
        saved.ApplyTo(restored);

        // Saved with the look rather than the match, so the next launch comes back drawing the clock the way the
        // operator left it.
        Assert.Equal(ShotClockDisplay.Line, restored.GetStateSnapshot().ShotClockDisplay);
    }

    [Fact]
    public void ApplyTo_WithNothingSaved_LeavesTheDefaultLook()
    {
        var fresh = new GameManager();
        var defaults = fresh.GetStateSnapshot();

        new OperatorProfile().ApplyTo(fresh);

        var applied = fresh.GetStateSnapshot();
        Assert.Equal(defaults.ColorTheme.HomeBackground, applied.ColorTheme.HomeBackground);
        Assert.Equal(defaults.ScoreboardStyle.CornerRoundness, applied.ScoreboardStyle.CornerRoundness);
        Assert.Equal(defaults.ScoreboardStyle.EndCapStyle, applied.ScoreboardStyle.EndCapStyle);
        Assert.True(applied.Visibility.ScoreBarVisible);
        Assert.Equal(string.Empty, applied.Player1.Name);
        Assert.Equal(defaults.Player1.RaceToTarget, applied.Player1.RaceToTarget);
    }
}
