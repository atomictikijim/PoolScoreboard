namespace PoolScoreboard.Controller.Tests;

/// <summary>
/// The address the Overlay hands Kestrel is the only thing keeping this app off the network: it serves a browser
/// source and a control API that changes the score, and <c>OverlayHost</c> asks for 127.0.0.1. Nothing checked that,
/// and no live check could — every one of them connects from this machine, so a wildcard bind would pass all of them
/// while putting the control API on the LAN. Two facts, both from the sources: no wildcard is asked for, and the
/// loopback address is.
/// </summary>
public sealed class OverlayLoopbackBindTests
{
    /// <summary>Ways of asking Kestrel for every interface. <c>AnyIP</c> covers <c>ListenAnyIP</c>.</summary>
    private static readonly string[] Wildcards = { "0.0.0.0", "AnyIP", "http://+", "http://*", "https://*", "[::]" };

    [Fact]
    public void TheOverlayHostNeverAsksKestrelForAWildcardAddress()
    {
        var offenders = OverlaySources()
            .SelectMany(file => Wildcards
                .Where(token => RepositorySources.Text(file).Contains(token, StringComparison.Ordinal))
                .Select(token => $"{RepositorySources.Relative(file)} has {token}"))
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"the overlay must stay on this machine, but: {string.Join("; ", offenders)}");
    }

    [Fact]
    public void TheOverlayHostAsksKestrelForTheLoopbackAddress()
    {
        // Literals, not raw text: the class comment says "127.0.0.1 only", and a comment must not be able to stand in
        // for the address the host actually asks for. A future ListenLocalhost(port) call names no literal, so it
        // would land here — point the guard at it then rather than deleting the fact.
        var named = OverlaySources()
            .Where(file => RepositorySources.StringLiterals(RepositorySources.Text(file))
                .Any(literal => literal.Contains("127.0.0.1", StringComparison.Ordinal)
                                || literal.Contains("localhost", StringComparison.Ordinal)))
            .Select(RepositorySources.Relative)
            .ToArray();

        Assert.True(
            named.Length > 0,
            "no Overlay source hands Kestrel a loopback address, so nothing keeps the host on this machine");
    }

    private static IEnumerable<string> OverlaySources() =>
        RepositorySources.Files("PoolScoreboard.Overlay", "*.cs");
}
