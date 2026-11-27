using System.IO;
using PoolScoreboard.Controller;
using PoolScoreboard.Controller.Persistence;
using PoolScoreboard.Controller.ViewModels;
using PoolScoreboard.Core;
using PoolScoreboard.Core.Enums;
using PoolScoreboard.Core.Persistence;

namespace PoolScoreboard.Controller.Tests;

/// <summary>
/// What the window shows at startup, without a window: a fresh launch keeps the screen's own 9-ball default, and a
/// resumed 8-ball match brings its groups, its ball layout and the live view. Built against a roster store rooted
/// in a throwaway folder and with no dispatcher, so nothing here needs a running app or the operator's
/// <c>%AppData%</c>.
/// </summary>
public sealed class GameViewModelTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "poolscoreboard-viewmodel-tests", Guid.NewGuid().ToString("n"));

    private GameViewModel BuildViewModel(GameManager manager) =>
        new(manager, persist: null, overlaySession: null, new PlayerRosterStore(_root), dispatcher: null, owner: null);

    [Fact]
    public void FreshLaunch_StaysOnTheNineBallDefault()
    {
        var viewModel = BuildViewModel(new GameManager());

        // A state at setup carries the 8-ball enum default, which is not a choice anyone made: adopting it would
        // flip every fresh launch off the 9-ball this screen has always started on.
        Assert.Equal(GameType.NineBall, viewModel.SelectedGameType);
        Assert.Equal(RaceToMode.Single, viewModel.SelectedRaceToMode);
        Assert.False(viewModel.GameInitialized);
        Assert.Empty(viewModel.HomeGroupBalls);
        Assert.Empty(viewModel.EightBallItems);
        Assert.Equal(9, viewModel.NumericRowBalls.Count);
    }

    [Fact]
    public void ResumedEightBallMatch_ShowsItsGroupsAndTheLiveView()
    {
        // Play a match, then hand a second manager the saved snapshot the way App does at startup: the players
        // first (what the profile carries), then the match.
        var played = new GameManager();
        played.SetPlayerDetails(isHome: true, "Home", "", 5, null);
        played.SetPlayerDetails(isHome: false, "Away", "", 5, null);
        played.InitializeGame(GameType.EightBall, RaceToMode.Split);
        played.AddPoint(isPlayer1: true);
        played.AddPoint(isPlayer1: true);
        played.AddPoint(isPlayer1: true);
        played.AddPoint(isPlayer1: false);
        played.AssignBallGroup(isHome: true, BallGroup.Solids);
        played.PocketBall(3);

        var saved = ResumableMatch.Capture(played.GetStateSnapshot());
        Assert.NotNull(saved);

        var relaunched = new GameManager();
        relaunched.SetPlayerDetails(isHome: true, "Home", "", 5, null);
        relaunched.SetPlayerDetails(isHome: false, "Away", "", 5, null);
        relaunched.RestoreMatch(saved);

        var viewModel = BuildViewModel(relaunched);

        // The live view, not the setup screen, carrying the scores that were on screen before the crash.
        Assert.True(viewModel.GameInitialized);
        Assert.Equal(3, viewModel.HomeScore);
        Assert.Equal(1, viewModel.AwayScore);

        // The choices the match was played with, rather than this screen's defaults.
        Assert.Equal(GameType.EightBall, viewModel.SelectedGameType);
        Assert.Equal(RaceToMode.Split, viewModel.SelectedRaceToMode);
        Assert.True(viewModel.IsEightBall);

        // And the layout that follows from them: each side's group plus the 8-ball, and no 9/10-ball numeric row.
        Assert.Equal(BallGroup.Solids, viewModel.HomeBallGroup);
        Assert.Equal(BallGroup.Stripes, viewModel.AwayBallGroup);
        Assert.Equal(7, viewModel.HomeGroupBalls.Count);
        Assert.Equal(7, viewModel.AwayGroupBalls.Count);

        var eightBall = Assert.Single(viewModel.EightBallItems);
        Assert.Equal(8, eightBall.Number);
        Assert.Empty(viewModel.NumericRowBalls);
    }

    [Fact]
    public void ChangingTheOverlayPort_RebindsAndSavesTheChoice()
    {
        var session = new FakeOverlaySession(51234);
        var saves = 0;
        var viewModel = new GameViewModel(
            new GameManager(), () => saves++, session, new PlayerRosterStore(_root), dispatcher: null, owner: null);

        viewModel.OverlayPort = 51240;

        Assert.Equal(51240, session.Port);
        Assert.Equal(1, session.Starts);
        Assert.Equal(1, saves);
        Assert.False(viewModel.OverlayIsOffline);
    }

    [Fact]
    public void AnUnusableOverlayPort_SnapsBackToTheOneTheHostIsOn()
    {
        var session = new FakeOverlaySession(51234);
        var saves = 0;
        var viewModel = new GameViewModel(
            new GameManager(), () => saves++, session, new PlayerRosterStore(_root), dispatcher: null, owner: null);

        // Below 1024 needs elevation to bind, so the field goes back to what is really in use rather than
        // leaving the window advertising a port that was never tried.
        viewModel.OverlayPort = 80;

        Assert.Equal(51234, viewModel.OverlayPort);
        Assert.Equal(0, session.Starts);
        Assert.Equal(0, saves);
    }

    [Fact]
    public void ChangingTheOverlayPort_NotifiesTheBaseUrlBuiltFromIt()
    {
        var viewModel = new GameViewModel(
            new GameManager(), persist: null, new FakeOverlaySession(51234), new PlayerRosterStore(_root),
            dispatcher: null, owner: null);

        var announced = new List<string?>();
        viewModel.PropertyChanged += (_, e) => announced.Add(e.PropertyName);

        viewModel.OverlayPort = 51240;

        // Nothing binds to it yet — ShowHelp() reads the address fresh at click time — which is why the bug was
        // invisible; the first binding that did would have kept naming the old port until something unrelated
        // refreshed it.
        Assert.Contains(nameof(GameViewModel.OverlayBaseUrl), announced);
        Assert.Equal("http://127.0.0.1:51240", viewModel.OverlayBaseUrl);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp folder is not worth failing a passing test over.
        }
    }

    /// <summary>Stands in for the host: records the port it was pointed at and whether it was started again.</summary>
    private sealed class FakeOverlaySession : IOverlaySession
    {
        public FakeOverlaySession(int port) => Port = port;

        public bool IsRunning { get; private set; } = true;

        public int Port { get; private set; }

        public string? FailureReason { get; private set; }

        public int Starts { get; private set; }

        /// <summary>Never raised here: these tests are about the port, not about a host dying.</summary>
        public event EventHandler? Stopped
        {
            add { }
            remove { }
        }

        public void SetPort(int port)
        {
            Port = port;
            IsRunning = false;
            FailureReason = null;
        }

        public bool Start(GameManager gameManager)
        {
            Starts++;
            IsRunning = true;
            FailureReason = null;
            return true;
        }
    }
}
