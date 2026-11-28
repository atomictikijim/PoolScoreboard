using System.IO;
using PoolScoreboard.Controller.Persistence;
using PoolScoreboard.Controller.Startup;
using PoolScoreboard.Core;
using PoolScoreboard.Core.Enums;
using PoolScoreboard.Core.Models;
using PoolScoreboard.Core.Persistence;

namespace PoolScoreboard.Controller.Tests;

/// <summary>
/// Every piece of startup has its own tests, but the order they run in — the saved port before anything binds, the
/// profile before a window reads state, the match after the profile, the overlay last — was only ever proven by
/// running the app by hand. That order has already produced a bug (the profile's events deleting the match a launch
/// was about to resume), so <see cref="StartupWiring"/> exists to be asserted here: the first two facts follow the
/// calls themselves, the next three run the real pieces with only the file and the socket faked.
/// </summary>
public sealed class StartupWiringTests
{
    [Fact]
    public void Run_AppliesTheSavedPortThenTheProfileThenTheMatchThenTheOverlay()
    {
        var order = new List<string>();

        new StartupWiring(
            () => new OperatorSettingsStore.LoadedProfile(new OperatorProfile(), null, null),
            _ => order.Add("port"),
            _ => order.Add("profile"),
            () => order.Add("match"),
            () => order.Add("overlay")).Run();

        Assert.Equal(new[] { "port", "profile", "match", "overlay" }, order);
    }

    [Fact]
    public void Run_WithNothingSaved_StillResumesTheMatchAndServesTheOverlay()
    {
        // A first launch: no profile to apply, but the overlay still has to come up and a match file left behind by
        // another build still has to be looked at rather than silently ignored.
        var order = new List<string>();

        new StartupWiring(
            () => null,
            _ => order.Add("port"),
            _ => order.Add("profile"),
            () => order.Add("match"),
            () => order.Add("overlay")).Run();

        Assert.Equal(new[] { "match", "overlay" }, order);
    }

    [Fact]
    public void Run_HasHandedTheSavedPortToTheSession_ByTheTimeTheOverlayStarts()
    {
        var session = new RecordingSession();
        var portSettings = new OverlayPortSettings(session);
        var portAtStart = 0;

        new StartupWiring(
            () => new OperatorSettingsStore.LoadedProfile(new OperatorProfile { OverlayPort = 51241 }, null, null),
            profile => portSettings.ApplySavedPort(profile),
            _ => { },
            () => { },
            () => portAtStart = session.Port).Run();

        // The host reads the session's port when it binds, so anything but this leaves the overlay on 51234 while
        // every URL in the window (and the OBS scene) says 51241.
        Assert.Equal(51241, portAtStart);
        Assert.Equal(51241, Assert.Single(session.SetPortCalls));
    }

    [Fact]
    public void Run_HasTheProfilesPlayersAndTargetsInState_WhenTheMatchIsResumed()
    {
        // The historical bug in one assertion: resuming before the profile is applied judges a resumed score against
        // whatever target the state happened to hold, and lets the profile's change events reach a resume that has
        // already started watching.
        var manager = new GameManager();
        GameState? atResume = null;

        new StartupWiring(
            () => new OperatorSettingsStore.LoadedProfile(
                new OperatorProfile
                {
                    HomePlayer = new PlayerProfile { Name = "Alice", RaceToTarget = 3 },
                    AwayPlayer = new PlayerProfile { Name = "Bob", RaceToTarget = 3 }
                },
                null,
                null),
            _ => { },
            loaded => loaded.Profile.ApplyTo(manager),
            () => atResume = manager.GetStateSnapshot(),
            () => { }).Run();

        Assert.NotNull(atResume);
        var resumed = atResume!.Player1;
        Assert.Equal("Alice", resumed.Name);
        Assert.Equal(3, resumed.RaceToTarget);
        Assert.False(atResume.IsGameActive);
    }

    [Fact]
    public void Run_ResumesTheSavedMatchBeforeTheOverlayStarts()
    {
        // The real autosave against a store that hands back one match: what the overlay is asked to serve has to be
        // the resumed match, not the empty board a window would show if the restore ran after the host.
        var manager = new GameManager();
        var store = new SavedMatchStore(new ResumableMatch
        {
            MatchId = "resumed",
            GameType = GameType.NineBall,
            RaceToMode = RaceToMode.Single,
            HomeScore = 2,
            AwayScore = 1
        });
        using var autosave = new MatchAutosave(manager, store);
        GameState? atStart = null;

        new StartupWiring(
            () => null,
            _ => { },
            _ => { },
            autosave.Restore,
            () => atStart = manager.GetStateSnapshot()).Run();

        Assert.NotNull(atStart);
        var served = atStart!;
        Assert.True(served.IsGameActive);
        Assert.Equal(2, served.Player1Score);
        Assert.Equal(1, served.Player2Score);
    }

    [Fact]
    public void TheWindowIsBuiltOnlyAfterStartup_SoItOpensOnTheRestoredProfile()
    {
        // "Profile before window" cannot be asserted by running the wiring — the window is WPF's. It holds because
        // App.xaml's StartupUri is honoured after OnStartup returns, and because App itself constructs no window, so
        // this pins both halves: a window built inside App could be built before the profile is applied, and would
        // then open on defaults while the overlay showed the saved look.
        var appSource = RepositorySources.TextAt(Path.Combine("PoolScoreboard.Controller", "App.xaml.cs"));
        var appMarkup = RepositorySources.TextAt(Path.Combine("PoolScoreboard.Controller", "App.xaml"));

        Assert.Contains("new StartupWiring(", appSource);
        Assert.DoesNotContain("new MainWindow(", appSource);
        Assert.Contains("StartupUri=\"MainWindow.xaml\"", appMarkup);
    }

    /// <summary>A session that only remembers what it was told, so no port is bound and nothing can fail to start.</summary>
    private sealed class RecordingSession : IOverlaySession
    {
        public bool IsRunning { get; private set; }

        public int Port { get; set; } = OperatorProfile.DefaultOverlayPort;

        public string? FailureReason { get; private set; }

        public List<int> SetPortCalls { get; } = new();

        public event EventHandler? Stopped
        {
            add { }
            remove { }
        }

        public void SetPort(int port)
        {
            SetPortCalls.Add(port);
            Port = port;
        }

        public bool Start(GameManager gameManager)
        {
            IsRunning = true;
            return IsRunning;
        }
    }

    /// <summary>Hands back one saved match and ignores writes, so the resume path runs without a file.</summary>
    private sealed class SavedMatchStore : IMatchSnapshotStore
    {
        private readonly ResumableMatch? _match;

        public SavedMatchStore(ResumableMatch? match) => _match = match;

        public ResumableMatch? Load() => _match;

        public bool TrySave(ResumableMatch match) => true;

        public void Delete()
        {
        }
    }
}
