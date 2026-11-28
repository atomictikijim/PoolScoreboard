using PoolScoreboard.Controller.Persistence;
using PoolScoreboard.Core.Persistence;

namespace PoolScoreboard.Controller.Startup;

/// <summary>
/// The order this app starts in, in one place and testable without a window. Each piece it calls has its own tests;
/// what none of them could see was the order, and that order has already produced a bug — the profile's change
/// events deleting the match a launch was about to resume. The live check was the only thing that could catch it,
/// and CI cannot run that, so the sequence is pinned here instead.
/// <para>
/// The four steps, and why each must follow the one before:
/// </para>
/// <list type="bullet">
/// <item><description>
/// the saved port reaches the session first, because setting it stops a running host — a listening socket cannot be
/// moved — so it has to happen before anything binds;
/// </description></item>
/// <item><description>
/// the profile goes into game state next, because the window reads that state when it is constructed and
/// <c>StartupUri</c> builds the window only after <c>OnStartup</c> returns, which is what makes the setup screen and
/// the overlay open with the saved colours, style, visibility and last players instead of the defaults;
/// </description></item>
/// <item><description>
/// the match in progress is resumed after the profile, because the profile supplies the players and Race-To targets
/// the scores are judged against, and because a resume that had already started watching for changes would let the
/// profile's own events delete the file being resumed;
/// </description></item>
/// <item><description>
/// the overlay starts last, since it is the only step that binds a socket and serves the page the rest describes.
/// </description></item>
/// </list>
/// </summary>
internal sealed class StartupWiring
{
    private readonly Func<OperatorSettingsStore.LoadedProfile?> _loadProfile;
    private readonly Action<OperatorProfile> _applyPort;
    private readonly Action<OperatorSettingsStore.LoadedProfile> _applyProfile;
    private readonly Action _resumeMatch;
    private readonly Action _startOverlay;

    /// <param name="loadProfile">Reads what was saved, or null on a first launch.</param>
    /// <param name="applyPort">Hands the saved port to the overlay session.</param>
    /// <param name="applyProfile">Writes the saved profile into game state.</param>
    /// <param name="resumeMatch">Restores the match in progress and starts watching it.</param>
    /// <param name="startOverlay">Starts the in-process overlay host.</param>
    internal StartupWiring(
        Func<OperatorSettingsStore.LoadedProfile?> loadProfile,
        Action<OperatorProfile> applyPort,
        Action<OperatorSettingsStore.LoadedProfile> applyProfile,
        Action resumeMatch,
        Action startOverlay)
    {
        _loadProfile = loadProfile;
        _applyPort = applyPort;
        _applyProfile = applyProfile;
        _resumeMatch = resumeMatch;
        _startOverlay = startOverlay;
    }

    /// <summary>
    /// Applies what was saved, resumes the match, then starts the overlay. A first launch skips the two profile steps
    /// and nothing else: there is no port or look to apply, but the match file may still hold something (a build that
    /// saved it without a profile) and the overlay has to serve either way.
    /// </summary>
    public void Run()
    {
        if (_loadProfile() is { } loaded)
        {
            _applyPort(loaded.Profile);
            _applyProfile(loaded);
        }

        _resumeMatch();
        _startOverlay();
    }
}
