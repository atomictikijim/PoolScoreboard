using System.Text.RegularExpressions;

namespace PoolScoreboard.Controller.Tests;

/// <summary>
/// The port work exists because the address was written into thirteen Help strings. <c>HelpWindowTests</c> pins that
/// page; this pins the rule for the project that shows the URLs at all: the Controller may name the loopback address,
/// but what it displays has to come from the session, and the default port lives in
/// <c>OperatorProfile.DefaultOverlayPort</c> and nowhere else.
/// <para>
/// Comments are deliberately out of scope — the address and the old port are named in several of them, and they are
/// documentation rather than behaviour. Markup is checked as raw text; C# is checked inside string literals only, so
/// <c>$"http://127.0.0.1:{Port}/…"</c> (a URL whose port is the live one) passes while
/// <c>"http://127.0.0.1:51241/…"</c> fails.
/// </para>
/// </summary>
public sealed class NoHardCodedOverlayAddressTests
{
    private const string DefaultPort = "51234";

    /// <summary>An address followed by a digit is a URL with its port baked in; one followed by <c>{</c> is not.</summary>
    private static readonly Regex HardCodedAddressAndPort = new("127\\.0\\.0\\.1:[0-9]", RegexOptions.Compiled);

    [Fact]
    public void NoControllerMarkupCarriesTheOverlayAddressOrTheDefaultPort()
    {
        var offenders = RepositorySources
            .Files("PoolScoreboard.Controller", "*.xaml")
            .SelectMany(file => Tokens(RepositorySources.Text(file), "127.0.0.1", DefaultPort)
                .Select(token => $"{RepositorySources.Relative(file)} has {token}"))
            .ToArray();

        Assert.True(offenders.Length == 0, $"the Controller's markup hard-codes: {string.Join("; ", offenders)}");
    }

    [Fact]
    public void NoControllerStringHardCodesTheOverlayAddressWithAPortOrTheDefaultPort()
    {
        var offenders = RepositorySources
            .Files("PoolScoreboard.Controller", "*.cs")
            .SelectMany(file => RepositorySources.StringLiterals(RepositorySources.Text(file))
                .Where(literal => literal.Contains(DefaultPort, StringComparison.Ordinal)
                                  || HardCodedAddressAndPort.IsMatch(literal))
                .Select(literal => $"{RepositorySources.Relative(file)} has {literal}"))
            .ToArray();

        Assert.True(offenders.Length == 0, $"the Controller's code hard-codes: {string.Join("; ", offenders)}");
    }

    private static IEnumerable<string> Tokens(string text, params string[] tokens) =>
        tokens.Where(token => text.Contains(token, StringComparison.Ordinal));
}
