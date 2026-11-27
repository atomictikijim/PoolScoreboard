using System.Text.RegularExpressions;

namespace PoolScoreboard.Controller.Tests;

/// <summary>
/// The overlay's paths are written down in five places and nothing connected them: <c>Endpoints/*.cs</c> chooses them,
/// and <c>HelpWindow.xaml</c>, both page scripts and <c>OverlayHostSession</c>'s health probe each spell them again.
/// A renamed route therefore left CI green while the app's own probe reported "overlay stopped serving" every three
/// seconds, the Help page advertised 404s and the OBS pages retried forever. This reads the mapped routes and fails
/// on any path used elsewhere that is not one.
/// <para>
/// The comparison is segment-wise, so a route's <c>{...}</c> and a placeholder the Help page tells the operator to
/// replace (one capital, <c>N</c> for a ball number) match any single segment — while everything around them still has
/// to line up, which is where a typo shows.
/// </para>
/// </summary>
public sealed class OverlayRouteTests
{
    private static readonly Regex MappedRoute =
        new("\\.Map(?:Get|Post|Put|Delete|Patch)\\(\\s*\"(?<path>/[^\"]+)\"", RegexOptions.Compiled);

    private static readonly Regex PathInText =
        new("/(?:overlay|api)/[A-Za-z0-9._/{}-]*", RegexOptions.Compiled);

    /// <summary>Everything that names a path besides the mapping itself.</summary>
    private static readonly string[] PathsUsedIn =
    {
        "PoolScoreboard.Controller/HelpWindow.xaml",
        "PoolScoreboard.Controller/OverlayHostSession.cs",
        "PoolScoreboard.Overlay/Assets/Scoreboard/scoreboard.js",
        "PoolScoreboard.Overlay/Assets/CueBall/cueball.js",
    };

    [Fact]
    public void EveryPathUsedOutsideTheEndpointFilesIsAMappedRoute()
    {
        var routes = MappedRoutes();

        // A guard that finds no routes would pass everything; say so instead of going quiet.
        Assert.NotEmpty(routes);

        var used = PathsUsedIn.SelectMany(PathsIn).ToArray();
        Assert.NotEmpty(used);

        var offenders = used
            .Where(usedPath => !routes.Any(route => Covers(route, usedPath.Path)))
            .Select(usedPath => $"{usedPath.Source} uses {usedPath.Path}")
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"these paths are used but nothing maps them: {string.Join("; ", offenders)}");
    }

    [Fact]
    public void EveryPlaceTheGuardReadsStillNamesAPath()
    {
        // Coverage: if a path moves to a file this guard does not read, the check above would keep passing for the
        // wrong reason. Point PathsUsedIn at the new home rather than deleting this.
        var silent = PathsUsedIn.Where(source => !PathsIn(source).Any()).ToArray();

        Assert.True(
            silent.Length == 0,
            $"these sources no longer name any /overlay or /api path, so the route guard stopped covering them: " +
            string.Join(", ", silent));
    }

    private static IReadOnlyCollection<string> MappedRoutes() =>
        RepositorySources
            .Files("PoolScoreboard.Overlay", "*.cs")
            .SelectMany(file => MappedRoute.Matches(RepositorySources.Text(file)).Select(match => match.Groups["path"].Value))
            .ToArray();

    private static IEnumerable<(string Path, string Source)> PathsIn(string source) =>
        PathInText
            .Matches(RepositorySources.TextAt(source))
            .Select(match => (Path: match.Value, Source: source));

    private static bool Covers(string route, string used)
    {
        var routeSegments = Segments(route);
        var usedSegments = Segments(used);
        if (routeSegments.Length != usedSegments.Length)
            return false;

        return routeSegments
            .Zip(usedSegments, (mapped, written) =>
                mapped == written || IsPlaceholder(mapped) || IsPlaceholder(written))
            .All(matches => matches);
    }

    private static string[] Segments(string path) =>
        path.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);

    /// <summary><c>{number:int}</c> in a route template, or <c>N</c> where the Help page says to fill one in.</summary>
    private static bool IsPlaceholder(string segment) =>
        (segment.StartsWith('{') && segment.EndsWith('}'))
        || (segment.Length == 1 && char.IsUpper(segment[0]));
}
