using PoolScoreboard.Controller.Persistence;
using PoolScoreboard.Core;
using PoolScoreboard.Core.Persistence;

namespace PoolScoreboard.Controller.Tests;

/// <summary>
/// Which port the app starts on, and which one it writes down, used to live only inside <c>App</c> — so the live
/// check was the only thing that could catch a saved port being ignored, or the default being saved over the
/// operator's choice. <see cref="OverlayPortSettings"/> is those two lines on their own, driven here with a session
/// that records what it was asked for and binds nothing.
/// </summary>
public sealed class OverlayPortSettingsTests
{
    [Fact]
    public void ApplySavedPort_HandsTheSavedPortToTheSession()
    {
        var session = new RecordingSession();
        var settings = new OverlayPortSettings(session);

        var applied = settings.ApplySavedPort(new OperatorProfile { OverlayPort = 51241 });

        Assert.Equal(51241, applied);
        Assert.Equal(51241, session.Port);
        Assert.Equal(51241, Assert.Single(session.SetPortCalls));
    }

    [Theory]
    [InlineData(0, OperatorProfile.DefaultOverlayPort)]
    [InlineData(80, OperatorProfile.DefaultOverlayPort)]
    [InlineData(1023, OperatorProfile.DefaultOverlayPort)]
    [InlineData(65536, OperatorProfile.DefaultOverlayPort)]
    [InlineData(1024, 1024)]
    [InlineData(51241, 51241)]
    [InlineData(65535, 65535)]
    public void ApplySavedPort_TurnsAPortThatCannotBeBoundIntoTheDefault(int saved, int expected)
    {
        var session = new RecordingSession();
        var settings = new OverlayPortSettings(session);

        var applied = settings.ApplySavedPort(new OperatorProfile { OverlayPort = saved });

        Assert.Equal(expected, applied);
        Assert.Equal(expected, session.Port);
        Assert.Equal(expected, Assert.Single(session.SetPortCalls));
    }

    [Fact]
    public void RecordPortInUse_WritesThePortTheSessionIsOn_NotTheOneTheProfileHeld()
    {
        var session = new RecordingSession { Port = 51241 };
        var settings = new OverlayPortSettings(session);
        var profile = new OperatorProfile { OverlayPort = OperatorProfile.DefaultOverlayPort };

        settings.RecordPortInUse(profile);

        Assert.Equal(51241, profile.OverlayPort);
    }

    [Fact]
    public void TheSavedPortSurvivesTheRoundTripThroughApplyAndSave()
    {
        var session = new RecordingSession();
        var settings = new OverlayPortSettings(session);

        settings.ApplySavedPort(new OperatorProfile { OverlayPort = 51241 });

        // What the next launch saves: captured from game state, so it still carries the default until it is stamped.
        var next = new OperatorProfile();
        settings.RecordPortInUse(next);

        Assert.Equal(51241, next.OverlayPort);
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
            FailureReason = null;
        }

        public bool Start(GameManager gameManager)
        {
            IsRunning = true;
            return IsRunning;
        }
    }
}
