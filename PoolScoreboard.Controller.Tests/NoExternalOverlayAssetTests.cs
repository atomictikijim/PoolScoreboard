using System.IO;
using System.Text.RegularExpressions;

namespace PoolScoreboard.Controller.Tests;

/// <summary>
/// The overlay has to render on a machine with no network at all — CLAUDE.md's "No CDN/external references" — and it
/// is shown in OBS on the same PC as the scoreboard. A web font, script or stylesheet fetched from somewhere else is
/// the difference between the scoreboard and a half-styled page at exactly the moment it matters, and nothing in CI
/// looked at the six asset files: only OBS would have shown it.
/// <para>
/// Both ways an asset can leave this machine are covered — an absolute URL, and a protocol-relative reference
/// (<c>//host/…</c>), which has no scheme but still resolves to another host. The rule is deliberately the whole
/// scheme rather than a list of CDNs: no asset needs one today. If a data-URI SVG ever needs its
/// <c>xmlns="http://www.w3.org/2000/svg"</c>, that namespace is an identifier no browser fetches — add precisely that
/// exception then, with the reason next to it.
/// </para>
/// </summary>
public sealed class NoExternalOverlayAssetTests
{
    private static readonly Regex AbsoluteUrl = new("https?://[^\\s\"'()<>]+", RegexOptions.Compiled);

    /// <summary>A scheme-less reference to another host: an attribute, <c>url(…)</c>, or a fetch/EventSource call.</summary>
    private static readonly Regex ProtocolRelative = new(
        "(?:(?:src|href)\\s*=\\s*[\"']?|url\\(\\s*[\"']?|(?:fetch|EventSource)\\s*\\(\\s*[\"']|@import\\s+[\"']?)//[A-Za-z0-9]",
        RegexOptions.Compiled);

    [Fact]
    public void NoOverlayAssetNamesAnAbsoluteUrl()
    {
        var offenders = Assets()
            .SelectMany(file => AbsoluteUrl
                .Matches(RepositorySources.Text(file))
                .Select(match => $"{RepositorySources.Relative(file)} names {match.Value}"))
            .ToArray();

        Assert.True(offenders.Length == 0, $"the overlay must work with no network, but: {string.Join("; ", offenders)}");
    }

    [Fact]
    public void NoOverlayAssetReferencesAnotherHostWithoutAScheme()
    {
        var offenders = Assets()
            .SelectMany(file => ProtocolRelative
                .Matches(RepositorySources.Text(file))
                .Select(match => $"{RepositorySources.Relative(file)} has {match.Value}"))
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"a scheme-less URL still leaves this machine, and the overlay must not: {string.Join("; ", offenders)}");
    }

    /// <summary>Everything served as an overlay page, whatever its extension — not just the types known today.</summary>
    private static string[] Assets()
    {
        var assets = RepositorySources.Files(Path.Combine("PoolScoreboard.Overlay", "Assets"), "*").ToArray();

        // A guard that finds no files passes everything; say so instead of going quiet.
        Assert.NotEmpty(assets);
        return assets;
    }
}
