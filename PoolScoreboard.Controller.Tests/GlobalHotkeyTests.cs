using System.IO;
using System.Text.RegularExpressions;
using System.Windows.Input;
using PoolScoreboard.Controller;
using PoolScoreboard.Controller.Hotkeys;
using PoolScoreboard.Controller.Persistence;
using PoolScoreboard.Controller.ViewModels;
using PoolScoreboard.Core;
using PoolScoreboard.Core.Enums;
using PoolScoreboard.Core.Models;
using PoolScoreboard.Core.Persistence;

namespace PoolScoreboard.Controller.Tests;

/// <summary>
/// The global shortcuts: what each chord runs, that they are only live while a match is running, and that none of
/// that needs a real registration — the fake stands in for <c>RegisterHotKey</c>, so running the tests does not take
/// Ctrl+Alt+Q from the machine they run on.
/// </summary>
public sealed class GlobalHotkeyTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "poolscoreboard-hotkey-tests", Guid.NewGuid().ToString("n"));

    private GameViewModel BuildViewModel(GameManager manager) =>
        new(manager, persist: null, overlaySession: null, new PlayerRosterStore(_root), dispatcher: null, owner: null,
            historyStore: new MatchHistoryStore(_root));

    private static void StartMatch(GameManager manager)
    {
        manager.SetPlayerDetails(isHome: true, "Home", string.Empty, 5, endCapIcon: null);
        manager.SetPlayerDetails(isHome: false, "Away", string.Empty, 5, endCapIcon: null);
        manager.InitializeGame(GameType.NineBall, RaceToMode.Single);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void EveryActionHasOneChord_AndNoTwoShareAnIdOrAKey()
    {
        var hotkeys = GlobalHotkeys.Default;

        foreach (var action in Enum.GetValues<GlobalHotkeyAction>())
            Assert.Single(hotkeys, hotkey => hotkey.Action == action);

        Assert.Equal(hotkeys.Count, hotkeys.Select(hotkey => hotkey.Id).Distinct().Count());
        Assert.Equal(hotkeys.Count, hotkeys.Select(hotkey => hotkey.Key).Distinct().Count());
    }

    [Fact]
    public void EveryChordKeepsCtrlAndAlt_SoTheRestOfTheDesktopStillTypes()
    {
        // Registering a bare letter or the spacebar system-wide would take it from every other program for the length
        // of a match, OBS's own text boxes included — which is why the window's own keys are not what gets registered.
        Assert.All(GlobalHotkeys.Default, hotkey =>
            Assert.Equal(ModifierKeys.Control | ModifierKeys.Alt, hotkey.Modifiers));
    }

    [Theory]
    [InlineData(GlobalHotkeyAction.HomePoint)]
    [InlineData(GlobalHotkeyAction.AwayPoint)]
    [InlineData(GlobalHotkeyAction.Undo)]
    [InlineData(GlobalHotkeyAction.ToggleShooter)]
    [InlineData(GlobalHotkeyAction.NewRack)]
    [InlineData(GlobalHotkeyAction.StartShotClock)]
    [InlineData(GlobalHotkeyAction.ExtendShotClock)]
    public void EachChordRunsTheSameCommandAsItsKey(GlobalHotkeyAction action)
    {
        var manager = new GameManager();
        var viewModel = BuildViewModel(manager);
        StartMatch(manager);

        // The two actions that need something to act on first: something to undo, and a clock to add time to.
        if (action == GlobalHotkeyAction.Undo)
            manager.AddPoint(isPlayer1: true);

        if (action == GlobalHotkeyAction.ExtendShotClock)
            manager.StartShotClock();

        var before = manager.GetStateSnapshot();

        var command = GlobalHotkeys.CommandFor(viewModel, action);
        Assert.True(command.CanExecute(null));
        command.Execute(null);

        var after = manager.GetStateSnapshot();
        switch (action)
        {
            case GlobalHotkeyAction.HomePoint:
                Assert.Equal(1, after.Player1Score);
                break;
            case GlobalHotkeyAction.AwayPoint:
                Assert.Equal(1, after.Player2Score);
                break;
            case GlobalHotkeyAction.Undo:
                Assert.Equal(0, after.Player1Score);
                break;
            case GlobalHotkeyAction.ToggleShooter:
                Assert.NotEqual(before.CurrentShooter?.Id, after.CurrentShooter?.Id);
                break;
            case GlobalHotkeyAction.NewRack:
                Assert.True(after.ShotClockArmed);
                Assert.Null(after.ShotClockEndsAt);
                break;
            case GlobalHotkeyAction.StartShotClock:
                Assert.Null(before.ShotClockEndsAt);
                Assert.NotNull(after.ShotClockEndsAt);
                break;
            case GlobalHotkeyAction.ExtendShotClock:
                Assert.Equal(
                    GameState.DefaultShotClockExtensionSeconds,
                    (after.ShotClockEndsAt!.Value - before.ShotClockEndsAt!.Value).TotalSeconds);
                break;
            default:
                Assert.Fail($"No assertion was written for {action}.");
                break;
        }
    }

    [Fact]
    public void TheGlobalChordsCoverTheWindowsOwnShortcuts()
    {
        var window = RepositorySources.Files("PoolScoreboard.Controller", "MainWindow.xaml.cs").Single();
        var local = Regex.Matches(RepositorySources.Text(window), @"case Key\.(\w+):")
            .Select(match => match.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);

        // A shortcut added to the window without its global twin would work with the Controller focused and do nothing
        // during the stream, which is the whole reason the chords exist.
        Assert.Equal(local, GlobalHotkeys.Default.Select(hotkey => hotkey.Key.ToString()).ToHashSet(StringComparer.Ordinal));
    }

    [Fact]
    public void NothingIsRegisteredUntilAMatchIsRunning()
    {
        var registration = new FakeRegistration();
        using var session = new GlobalHotkeySession(registration);

        session.Attach(new IntPtr(4242));

        // Setup: a chord here would score a match nobody is playing.
        Assert.Empty(session.Registered);
        Assert.Empty(registration.Registered);

        session.SetActive(true);

        // A match: every action the window's keys reach, reachable from OBS too.
        Assert.Equal(GlobalHotkeys.Default.Count, session.Registered.Count);
        Assert.Equal(GlobalHotkeys.Default.Count, registration.Registered.Count);
    }

    [Fact]
    public void LeavingTheMatchTakesEveryChordBack()
    {
        var registration = new FakeRegistration();
        using var session = new GlobalHotkeySession(registration);
        session.Attach(new IntPtr(4242));
        session.SetActive(true);

        session.SetActive(false);

        // A reset returns to setup, where the chords have nothing to act on and must not be held against every other
        // program on the machine.
        Assert.Empty(session.Registered);
        Assert.Equal(GlobalHotkeys.Default.Count, registration.Unregistered.Count);
    }

    [Fact]
    public void AttachingAnAlreadyRunningMatchRegistersAtOnce()
    {
        var registration = new FakeRegistration();
        using var session = new GlobalHotkeySession(registration);

        // The window is created after the view model has already restored a match, so the order has to work both ways.
        session.SetActive(true);
        session.Attach(new IntPtr(4242));

        Assert.Equal(GlobalHotkeys.Default.Count, session.Registered.Count);
    }

    [Fact]
    public void AChordAnotherProgramOwnsLeavesTheRestWorking_AndThatKeyReachesNothing()
    {
        var registration = new FakeRegistration();
        var taken = GlobalHotkeys.Default[0];
        registration.Refuse.Add(taken.Id);

        using var session = new GlobalHotkeySession(registration);
        session.Attach(new IntPtr(4242));
        session.SetActive(true);

        Assert.DoesNotContain(taken, session.Registered);
        Assert.Null(session.HotkeyFor(taken.Id));
        Assert.Equal(GlobalHotkeys.Default.Count - 1, session.Registered.Count);
    }

    [Fact]
    public void DisposingTakesEveryChordBack()
    {
        var registration = new FakeRegistration();
        var session = new GlobalHotkeySession(registration);
        session.Attach(new IntPtr(4242));
        session.SetActive(true);

        session.Dispose();

        // Closing the window mid-match must not leave the chords registered to a window that no longer exists.
        Assert.Empty(session.Registered);
        Assert.Equal(GlobalHotkeys.Default.Count, registration.Unregistered.Count);
    }

    private sealed class FakeRegistration : IHotkeyRegistration
    {
        public List<GlobalHotkey> Registered { get; } = new();

        public List<GlobalHotkey> Unregistered { get; } = new();

        /// <summary>Ids Windows refuses, standing in for a chord another program already owns.</summary>
        public HashSet<int> Refuse { get; } = new();

        public bool TryRegister(IntPtr window, GlobalHotkey hotkey)
        {
            if (Refuse.Contains(hotkey.Id))
                return false;

            Registered.Add(hotkey);
            return true;
        }

        public void Unregister(IntPtr window, GlobalHotkey hotkey) => Unregistered.Add(hotkey);
    }
}
