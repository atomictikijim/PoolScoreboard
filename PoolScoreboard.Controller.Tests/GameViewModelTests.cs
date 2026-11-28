using System.IO;
using PoolScoreboard.Controller;
using PoolScoreboard.Controller.Persistence;
using PoolScoreboard.Controller.ViewModels;
using PoolScoreboard.Core;
using PoolScoreboard.Core.Enums;
using PoolScoreboard.Core.Models;
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

    // The history store is rooted in the same throwaway folder as the roster: a save test must never be able to
    // read or write the operator's %AppData% history.
    private GameViewModel BuildViewModel(GameManager manager, MatchHistoryStore? historyStore = null) =>
        new(manager, persist: null, overlaySession: null, new PlayerRosterStore(_root), dispatcher: null, owner: null,
            historyStore ?? new MatchHistoryStore(_root), presetStore: new SetupPresetStore(_root));

    private static void StartMatch(GameManager manager, int homeTarget, int awayTarget)
    {
        manager.SetPlayerDetails(isHome: true, "Home", "Team H", homeTarget, null);
        manager.SetPlayerDetails(isHome: false, "Away", "Team A", awayTarget, null);
        manager.InitializeGame(GameType.EightBall, RaceToMode.Split);
    }

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
    [Fact]
    public void StartingTheShotClock_StartsItOnTheOverlay()
    {
        var now = new DateTimeOffset(2026, 11, 27, 20, 0, 0, TimeSpan.FromHours(-5));
        var manager = new GameManager(() => now);
        var viewModel = BuildViewModel(manager);
        StartMatch(manager, homeTarget: 3, awayTarget: 3);

        viewModel.StartShotClockCommand.Execute(null);

        // The key (and the command behind it) only has to reach GameManager; the deadline itself is Core's to
        // compute, which its own tests pin.
        Assert.Equal(now.AddSeconds(GameState.DefaultShotClockSeconds), manager.GetStateSnapshot().ShotClockEndsAt);
    }

    [Fact]
    public void TheSetupScreensLengths_AreWhatTheMatchUses_AndTheExtensionAddsTheirs()
    {
        var now = new DateTimeOffset(2026, 11, 28, 20, 0, 0, TimeSpan.FromHours(-5));
        var manager = new GameManager(() => now);
        var viewModel = BuildViewModel(manager);

        viewModel.ShotClockSeconds = 20;
        viewModel.ShotClockExtensionSeconds = 10;

        StartMatch(manager, homeTarget: 3, awayTarget: 3);
        manager.StartShotClock();

        Assert.Equal(now.AddSeconds(20), manager.GetStateSnapshot().ShotClockEndsAt);

        viewModel.ExtendShotClockCommand.Execute(null);

        // The screen's two numbers reach Core through the same setup write, so the button and the key only have to
        // call the command — the arithmetic is Core's and its own tests pin it.
        Assert.Equal(now.AddSeconds(30), manager.GetStateSnapshot().ShotClockEndsAt);
    }

    [Fact]
    public void TheReadoutSaysWhatTheClockIsDoing_WithoutLookingAtTheOverlay()
    {
        var start = new DateTimeOffset(2026, 11, 28, 20, 0, 0, TimeSpan.FromHours(-5));
        var now = start;
        var manager = new GameManager(() => now);
        var viewModel = new GameViewModel(
            manager, persist: null, overlaySession: null, new PlayerRosterStore(_root), dispatcher: null, owner: null,
            clock: () => now);
        StartMatch(manager, homeTarget: 3, awayTarget: 3);

        // Nothing started, nothing to read.
        Assert.False(viewModel.ShotClockReadoutVisible);

        manager.ResetBalls();
        Assert.True(viewModel.ShotClockReadoutVisible);
        Assert.Equal("0:40 · waiting", viewModel.ShotClockReadout);

        manager.StartShotClock();
        now = start.AddSeconds(8);

        // A deadline raises no event as it passes, so the refresh is what has to be able to move the number; the
        // window's timer is only the thing that calls it every quarter second.
        viewModel.RefreshShotClockReadout();

        Assert.Equal("0:32 · running", viewModel.ShotClockReadout);

        now = start.AddSeconds(42);
        viewModel.RefreshShotClockReadout();

        // Past the deadline the overlay still draws 0:00, so the readout says the same rather than going blank.
        Assert.Equal("0:00 · time up", viewModel.ShotClockReadout);

        manager.ClearShotClock();
        Assert.False(viewModel.ShotClockReadoutVisible);
    }

    [Fact]
    public void TheHeaderSaysWhoHasAnExtensionLeft_SoASecondCallIsNotASurprise()
    {
        var manager = new GameManager();
        var viewModel = BuildViewModel(manager);
        StartMatch(manager, homeTarget: 3, awayTarget: 3);

        Assert.Equal("Home: 1 left", viewModel.HomeExtensionStatus);
        Assert.Equal("Away: 1 left", viewModel.AwayExtensionStatus);
        Assert.False(viewModel.HomeExtensionUsed);
        Assert.False(viewModel.AwayExtensionUsed);

        manager.StartShotClock();
        viewModel.ExtendShotClockCommand.Execute(null);

        // The shooter's is spent and says so; the other side is untouched, which is the whole point of showing both.
        Assert.Equal("Home: used", viewModel.HomeExtensionStatus);
        Assert.True(viewModel.HomeExtensionUsed);
        Assert.Equal("Away: 1 left", viewModel.AwayExtensionStatus);
        Assert.False(viewModel.AwayExtensionUsed);

        // A new rack hands both of them one back; a header still saying "used" would stop the operator calling it.
        manager.ResetBalls();

        Assert.Equal("Home: 1 left", viewModel.HomeExtensionStatus);
        Assert.False(viewModel.HomeExtensionUsed);
    }

    [Fact]
    public void TheCallExtensionButton_IsGreyedOnceTheShooterHasSpentTheirs()
    {
        var manager = new GameManager();
        var viewModel = BuildViewModel(manager);
        StartMatch(manager, homeTarget: 3, awayTarget: 3);

        Assert.True(viewModel.CanCallExtension);
        Assert.True(viewModel.ExtendShotClockCommand.CanExecute(null));

        manager.StartShotClock();
        viewModel.ExtendShotClockCommand.Execute(null);

        // Home has spent theirs: the press Core would ignore cannot be made at all.
        Assert.False(viewModel.CanCallExtension);
        Assert.False(viewModel.ExtendShotClockCommand.CanExecute(null));

        // The other player still has one, and the button follows the player at the table rather than either side.
        viewModel.ToggleCurrentShooterCommand.Execute(null);
        Assert.True(viewModel.CanCallExtension);

        viewModel.ExtendShotClockCommand.Execute(null);
        Assert.False(viewModel.CanCallExtension);

        // A new rack hands them both back, so the button is usable again without the operator doing anything else.
        viewModel.ToggleCurrentShooterCommand.Execute(null);
        manager.ResetBalls();
        Assert.True(viewModel.CanCallExtension);
    }

    [Fact]
    public void TheStaleCueToggle_ReachesTheStateAndIsSaved()
    {
        var saves = 0;
        var manager = new GameManager();
        var viewModel = new GameViewModel(
            manager, () => saves++, overlaySession: null, new PlayerRosterStore(_root), dispatcher: null, owner: null);
        StartMatch(manager, homeTarget: 3, awayTarget: 3);

        Assert.Equal("Stale Cue: Off", viewModel.StaleCueButtonText);

        viewModel.ToggleStaleCueCommand.Execute(null);

        // The button says what the overlay is doing, and a look setting is saved the moment it is pressed.
        Assert.True(manager.GetStateSnapshot().StaleCueEnabled);
        Assert.True(viewModel.StaleCueEnabled);
        Assert.Equal("Stale Cue: On", viewModel.StaleCueButtonText);
        Assert.Equal(1, saves);

        viewModel.ToggleStaleCueCommand.Execute(null);

        Assert.False(manager.GetStateSnapshot().StaleCueEnabled);
        Assert.Equal("Stale Cue: Off", viewModel.StaleCueButtonText);
        Assert.Equal(2, saves);
    }

    [Fact]
    public void TheDisplayToggleSwapsTheClockBetweenTimeAndALine_AndSaves()
    {
        var saves = 0;
        var manager = new GameManager();
        var viewModel = new GameViewModel(
            manager, () => saves++, overlaySession: null, new PlayerRosterStore(_root), dispatcher: null, owner: null);
        StartMatch(manager, homeTarget: 3, awayTarget: 3);

        Assert.Equal("Shot Clock: Time", viewModel.ShotClockDisplayButtonText);

        viewModel.ToggleShotClockDisplayCommand.Execute(null);

        // The button says which drawing is on screen, and the change is a look setting, so it is saved the moment it
        // is made rather than waited for on exit.
        Assert.Equal(ShotClockDisplay.Line, manager.GetStateSnapshot().ShotClockDisplay);
        Assert.Equal(ShotClockDisplay.Line, viewModel.ShotClockDisplay);
        Assert.Equal("Shot Clock: Line", viewModel.ShotClockDisplayButtonText);
        Assert.Equal(1, saves);

        viewModel.ToggleShotClockDisplayCommand.Execute(null);

        Assert.Equal(ShotClockDisplay.Time, manager.GetStateSnapshot().ShotClockDisplay);
        Assert.Equal("Shot Clock: Time", viewModel.ShotClockDisplayButtonText);
    }

    [Fact]
    public void AWinnerAloneWritesNothing_AndSavingFilesTheMatchOnce()
    {
        var manager = new GameManager();
        var history = new MatchHistoryStore(_root);
        var viewModel = BuildViewModel(manager, history);
        StartMatch(manager, homeTarget: 2, awayTarget: 2);

        while (manager.GetStateSnapshot().Winner is null)
            manager.AddPoint(isPlayer1: true);

        // A win is not a record: the operator decides what belongs in the history.
        Assert.True(viewModel.MatchWon);
        Assert.True(viewModel.CanSaveMatch);
        Assert.Empty(history.Load().Matches);

        viewModel.SaveMatchCommand.Execute(null);

        Assert.True(viewModel.MatchSaved);
        Assert.False(viewModel.CanSaveMatch);
        Assert.Single(history.Load().Matches);

        // Pressing it again files nothing: the archive refuses the id it already holds.
        viewModel.SaveMatchCommand.Execute(null);
        Assert.Single(history.Load().Matches);
    }

    [Fact]
    public void SaveIsNotOfferedBeforeThereIsAWinner()
    {
        var manager = new GameManager();
        var history = new MatchHistoryStore(_root);
        var viewModel = BuildViewModel(manager, history);
        StartMatch(manager, homeTarget: 3, awayTarget: 3);
        manager.AddPoint(isPlayer1: true);

        Assert.False(viewModel.MatchWon);
        Assert.False(viewModel.CanSaveMatch);

        viewModel.SaveMatchCommand.Execute(null);

        Assert.False(viewModel.MatchSaved);
        Assert.Empty(history.Load().Matches);
    }

    [Fact]
    public void AResumedMatchAlreadyInTheHistory_ShowsAsSavedAndCannotBeFiledAgain()
    {
        var manager = new GameManager();
        StartMatch(manager, homeTarget: 2, awayTarget: 2);
        while (manager.GetStateSnapshot().Winner is null)
            manager.AddPoint(isPlayer1: false);

        var history = new MatchHistoryStore(_root);
        Assert.NotNull(history.TryArchive(manager.GetStateSnapshot(), DateTimeOffset.Now));

        // What a relaunch builds: nothing in this run remembers the save, so the window has to read the file to
        // offer the button honestly.
        var viewModel = BuildViewModel(manager, history);

        Assert.True(viewModel.MatchWon);
        Assert.True(viewModel.MatchSaved);
        Assert.False(viewModel.CanSaveMatch);

        viewModel.SaveMatchCommand.Execute(null);
        Assert.Single(history.Load().Matches);
    }

    [Fact]
    public void StartingTheNextMatch_ClearsTheSavedMark()
    {
        var manager = new GameManager();
        var history = new MatchHistoryStore(_root);
        var viewModel = BuildViewModel(manager, history);
        StartMatch(manager, homeTarget: 1, awayTarget: 1);
        manager.AddPoint(isPlayer1: true);
        viewModel.SaveMatchCommand.Execute(null);
        Assert.True(viewModel.MatchSaved);

        manager.ResetGame();
        StartMatch(manager, homeTarget: 1, awayTarget: 1);

        Assert.False(viewModel.MatchWon);
        Assert.False(viewModel.MatchSaved);
        Assert.False(viewModel.CanSaveMatch);
    }

    [Fact]
    public void DeletingTheSavedMatch_ClearsTheSavedMark()
    {
        var manager = new GameManager();
        var history = new MatchHistoryStore(_root);
        var viewModel = BuildViewModel(manager, history);
        StartMatch(manager, homeTarget: 1, awayTarget: 1);
        manager.AddPoint(isPlayer1: true);
        viewModel.SaveMatchCommand.Execute(null);
        Assert.True(viewModel.MatchSaved);

        var record = Assert.Single(history.Load().Matches);
        Assert.True(history.TryDelete(record.MatchId));

        // What the History window closing does: the banner is still up, but the save it advertised is gone, so the
        // live view has to offer the button again rather than claim a record that no longer exists.
        viewModel.RefreshSavedMatch(manager.GetStateSnapshot(), reread: true);

        Assert.True(viewModel.MatchWon);
        Assert.False(viewModel.MatchSaved);
        Assert.True(viewModel.CanSaveMatch);
    }

    [Fact]
    public void DeletingAnotherMatch_LeavesTheSavedMarkAlone()
    {
        var manager = new GameManager();
        var history = new MatchHistoryStore(_root);
        var viewModel = BuildViewModel(manager, history);
        StartMatch(manager, homeTarget: 1, awayTarget: 1);
        manager.AddPoint(isPlayer1: true);
        viewModel.SaveMatchCommand.Execute(null);

        // Somebody else's match, deleted from the same file: the match on screen is untouched.
        var archive = history.Load();
        archive.Matches.Add(new MatchRecord
        {
            MatchId = "another-match",
            CompletedAt = DateTimeOffset.Now.AddHours(-1),
            GameType = GameType.EightBall,
            RaceToMode = RaceToMode.Single,
            HomeName = "Older",
            HomeRaceTo = 1,
            HomeScore = 1,
            AwayName = "Opponent",
            AwayRaceTo = 1,
            HomeWon = true
        });
        Assert.True(history.TrySave(archive));
        Assert.True(history.TryDelete("another-match"));

        viewModel.RefreshSavedMatch(manager.GetStateSnapshot(), reread: true);

        Assert.True(viewModel.MatchSaved);
        Assert.False(viewModel.CanSaveMatch);
    }

    // --- Setup presets ---

    [Fact]
    public void SavePreset_FilesTheSetupOnScreenUnderTheTypedName()
    {
        var manager = new GameManager();
        var viewModel = BuildViewModel(manager);

        viewModel.SelectedGameType = GameType.EightBall;
        viewModel.SelectedRaceToMode = RaceToMode.Split;
        viewModel.ShotClockSeconds = 35;
        viewModel.ShotClockExtensionSeconds = 25;
        viewModel.PresetName = "  House A  ";

        Assert.True(viewModel.SavePresetCommand.CanExecute(null));
        viewModel.SavePresetCommand.Execute(null);

        // The file a second store reads is what the next launch will offer, and the name was trimmed because that is
        // how a preset is looked up again.
        var saved = new SetupPresetStore(_root).Load().Find("House A");
        Assert.NotNull(saved);
        Assert.Equal(GameType.EightBall, saved!.GameType);
        Assert.Equal(RaceToMode.Split, saved.RaceToMode);
        Assert.Equal(35, saved.ShotClockSeconds);
        Assert.Equal(25, saved.ShotClockExtensionSeconds);
        Assert.Contains("House A", viewModel.PresetNames);
        Assert.Equal("Saved 'House A'.", viewModel.PresetStatus);
    }

    [Fact]
    public void SavePreset_ReplacesTheSameName_InsteadOfListingItTwice()
    {
        var viewModel = BuildViewModel(new GameManager());

        viewModel.PresetName = "House A";
        viewModel.ShotClockSeconds = 40;
        viewModel.SavePresetCommand.Execute(null);

        viewModel.ShotClockSeconds = 55;
        viewModel.SavePresetCommand.Execute(null);

        Assert.Equal(new[] { "House A" }, viewModel.PresetNames);
        Assert.Equal(55, new SetupPresetStore(_root).Load().Find("House A")!.ShotClockSeconds);
    }

    [Fact]
    public void SavePreset_LeavesThePickerEmpty_SoThePresetCanBeAppliedBack()
    {
        var manager = new GameManager();
        var viewModel = BuildViewModel(manager);

        viewModel.ShotClockSeconds = 35;
        viewModel.PresetName = "House A";
        viewModel.SavePresetCommand.Execute(null);

        // Saving says what was stored, not that the screen is that preset now. Leaving the name picked would make
        // the next pick of it a no-op, and that pick is the only gesture that puts the saved values back.
        Assert.Null(viewModel.SelectedPreset);

        viewModel.ShotClockSeconds = 50;
        viewModel.SelectedPreset = "House A";

        Assert.Equal(35, viewModel.ShotClockSeconds);
        Assert.Equal(35, manager.GetStateSnapshot().ShotClockSeconds);
        Assert.Equal("Applied 'House A'.", viewModel.PresetStatus);
    }


    [Fact]
    public void SelectingAPreset_AppliesItsMatchChoicesAndItsLook()
    {
        // Saved by an earlier session, so the picker is filled from the file at construction.
        Assert.NotNull(new SetupPresetStore(_root).TryUpsert(new SetupPreset
        {
            Name = "House B",
            GameType = GameType.TenBall,
            RaceToMode = RaceToMode.Split,
            ShotClockSeconds = 45,
            ShotClockExtensionSeconds = 20,
            OverlayPort = 51241,
            ColorTheme = new ColorTheme { HomeAccent = "#123456" },
            Visibility = new ScoreboardVisibility { BallTrackerVisible = false },
            ShotClockDisplay = ShotClockDisplay.Line,
            StaleCueEnabled = true
        }));

        var manager = new GameManager();
        var viewModel = BuildViewModel(manager);

        viewModel.SelectedPreset = "House B";

        // The screen...
        Assert.Equal(GameType.TenBall, viewModel.SelectedGameType);
        Assert.Equal(RaceToMode.Split, viewModel.SelectedRaceToMode);
        Assert.Equal(45, viewModel.ShotClockSeconds);
        Assert.Equal(20, viewModel.ShotClockExtensionSeconds);
        Assert.Equal("#123456", viewModel.HomeAccentColor);
        Assert.False(viewModel.BallTrackerVisible);
        Assert.Equal(ShotClockDisplay.Line, viewModel.ShotClockDisplay);
        Assert.True(viewModel.StaleCueEnabled);
        Assert.Equal(51241, viewModel.OverlayPort);

        // ...and the state the overlay and the clock will read, pushed by the same change handlers an edit goes through.
        var state = manager.GetStateSnapshot();
        Assert.Equal(GameType.TenBall, state.GameType);
        Assert.Equal(RaceToMode.Split, state.RaceToMode);
        Assert.Equal(45, state.ShotClockSeconds);
        Assert.Equal(20, state.ShotClockExtensionSeconds);
        Assert.Equal("#123456", state.ColorTheme.HomeAccent);
        Assert.False(state.Visibility.BallTrackerVisible);
        Assert.Equal(ShotClockDisplay.Line, state.ShotClockDisplay);
        Assert.True(state.StaleCueEnabled);
        Assert.Equal("Applied 'House B'.", viewModel.PresetStatus);
    }

    [Fact]
    public void DeletingAPreset_TakesItOutOfThePickerAndTheFile()
    {
        var store = new SetupPresetStore(_root);
        store.TryUpsert(new SetupPreset { Name = "House A" });
        store.TryUpsert(new SetupPreset { Name = "House B" });

        var viewModel = BuildViewModel(new GameManager());
        viewModel.SelectedPreset = "House A";
        Assert.True(viewModel.DeletePresetCommand.CanExecute(null));

        viewModel.DeletePresetCommand.Execute(null);

        Assert.Equal(new[] { "House B" }, viewModel.PresetNames);
        Assert.Equal(new[] { "House B" }, new SetupPresetStore(_root).Load().Names());
        Assert.Equal("Deleted 'House A'.", viewModel.PresetStatus);
        Assert.False(viewModel.DeletePresetCommand.CanExecute(null));
    }

    [Fact]
    public void SavePreset_WithABlankName_CannotBePressedAndSavesNothing()
    {
        var viewModel = BuildViewModel(new GameManager());
        viewModel.PresetName = "   ";

        Assert.False(viewModel.SavePresetCommand.CanExecute(null));
        viewModel.SavePresetCommand.Execute(null);

        Assert.Empty(new SetupPresetStore(_root).Load().Presets);
        Assert.Empty(viewModel.PresetNames);
    }

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
