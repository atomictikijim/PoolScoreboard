using PoolScoreboard.Core.Persistence;

namespace PoolScoreboard.Controller.Persistence;

/// <summary>
/// The two places where the saved overlay port and the running host have to agree, kept out of <see cref="App"/>
/// the way <see cref="MatchAutosave"/> was so both directions can be asserted without a window, a socket or a
/// settings file: a profile being applied hands its port to the session <em>before</em> anything binds, and a
/// profile being saved records the port the session is actually on rather than a value that was typed and never
/// taken.
/// </summary>
internal sealed class OverlayPortSettings
{
    private readonly IOverlaySession _session;

    internal OverlayPortSettings(IOverlaySession session) => _session = session;

    /// <summary>
    /// Points the session at the port the profile saved. A value that cannot be bound — a hand-edited file, one
    /// written before the range was enforced — becomes <see cref="OperatorProfile.DefaultOverlayPort"/> instead of
    /// an overlay that cannot start, because surviving a port it cannot have is the reason the port is a setting at
    /// all. Call it before the host starts: changing the port stops a running host, since a listening socket cannot
    /// be moved.
    /// </summary>
    /// <returns>The port the session is on afterwards.</returns>
    public int ApplySavedPort(OperatorProfile profile)
    {
        var port = OperatorProfile.NormalizePort(profile.OverlayPort);
        _session.SetPort(port);
        return port;
    }

    /// <summary>
    /// Stamps the port in use onto a profile about to be written, so the next launch comes back on the port the
    /// overlay was really served on — not the default, and not a value that never reached the host.
    /// </summary>
    public void RecordPortInUse(OperatorProfile profile) => profile.OverlayPort = _session.Port;
}
