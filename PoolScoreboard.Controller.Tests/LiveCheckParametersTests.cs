using System.Text.RegularExpressions;
using PoolScoreboard.Core.Persistence;

namespace PoolScoreboard.Controller.Tests;

/// <summary>
/// The live check is the one place the app's ports are written down twice: it launches the Controller on a port of
/// its own choosing and moves the overlay to a second one. CI only proves that script parses, so a default that
/// drifted from <see cref="OperatorProfile.DefaultOverlayPort"/> — or a "moved" port the app would refuse, or a
/// scenario name the instructions no longer mention — would be found at the next manual run, if at all. That is the
/// same rot the parse check exists to stop, one level down.
/// <para>
/// Every number here is read out of the script rather than repeated in this file, so the guard follows the script
/// instead of freezing it: the assertion is that the script and the app agree, not that either says 51234. Each
/// extraction asserts it found something, because a rewrite that renamed the script's variables would otherwise
/// leave this passing over an empty set.
/// </para>
/// <para>
/// The port-change scenario also states its own offset in the help text ("Defaults to 7 above -Port"); that sentence
/// is what an operator sizes the move from, so it is held to the arithmetic the script actually runs.
/// </para>
/// </summary>
public sealed class LiveCheckParametersTests
{
    private const string Script = "tools/verify-live.ps1";
    private const string Instructions = "CLAUDE.md";

    /// <summary>The port the script launches the Controller on when nobody passes <c>-Port</c>.</summary>
    private static readonly Regex PortDefault = new(@"\[int\]\$Port\s*=\s*(?<port>\d+)", RegexOptions.Compiled);

    /// <summary>The port-change step's arithmetic: <c>$Port + 7</c>.</summary>
    private static readonly Regex SecondPortOffset = new(
        @"\$SecondPort\s*\}\s*else\s*\{\s*\$Port\s*\+\s*(?<offset>\d+)",
        RegexOptions.Compiled);

    /// <summary>The same offset as the script's help states it: "Defaults to 7 above -Port".</summary>
    private static readonly Regex DocumentedOffset = new(@"(?<offset>\d+)\s+above\s+-Port", RegexOptions.Compiled);

    /// <summary>The scenarios <c>-Scenario</c> accepts.</summary>
    private static readonly Regex AcceptedScenarios = new(@"\[ValidateSet\((?<names>[^)]*)\)\]", RegexOptions.Compiled);

    /// <summary>The scenarios the script actually runs, in the <c>all</c> list.</summary>
    private static readonly Regex RunScenarios = new(@"\$scenarios\s*=\s*if[^\r\n]*?@\((?<names>[^)]*)\)", RegexOptions.Compiled);

    /// <summary>A single-quoted PowerShell string: a scenario name or a name in the accepted list.</summary>
    private static readonly Regex Quoted = new(@"'(?<name>[^']+)'", RegexOptions.Compiled);

    [Fact]
    public void TheLiveCheckLaunchesTheAppOnThePortTheAppDefaultsTo()
    {
        var script = RepositorySources.TextAt(Script);

        var launched = Single(PortDefault, "port", script, "-Port default");
        var expected = OperatorProfile.DefaultOverlayPort;

        Assert.True(
            launched == expected,
            $"the live check launches the Controller on {launched}, but the app opens on {expected} " +
            $"({nameof(OperatorProfile)}.{nameof(OperatorProfile.DefaultOverlayPort)}), so the scenario would be " +
            "testing a port the app never binds");
    }

    [Fact]
    public void ThePortChangeScenarioMovesToAPortTheAppWouldAccept()
    {
        var script = RepositorySources.TextAt(Script);

        var offset = Single(SecondPortOffset, "offset", script, "port-change offset");
        var documented = Single(DocumentedOffset, "offset", script, "documented offset");

        Assert.True(
            offset == documented,
            $"the port-change step moves {offset} above -Port, but its help says {documented}");

        Assert.True(offset > 0, "the port-change step has to move to a different port, but its offset is 0");

        var moved = OperatorProfile.DefaultOverlayPort + offset;
        Assert.True(
            OperatorProfile.IsUsablePort(moved),
            $"the default port plus the port-change offset is {moved}, which the overlay cannot be bound to " +
            $"({OperatorProfile.MinOverlayPort}-{OperatorProfile.MaxOverlayPort}); the scenario would fail to move");
    }

    [Fact]
    public void EveryScenarioIsOneTheLiveCheckAcceptsAndTheInstructionsName()
    {
        var script = RepositorySources.TextAt(Script);

        var accepted = Names(AcceptedScenarios, script)
            .Where(name => name != "all")
            .ToArray();

        Assert.True(accepted.Length > 0, "the live check no longer accepts any scenario, so this guard stopped covering it");

        var run = Names(RunScenarios, script);
        Assert.True(run.Length > 0, "the live check no longer lists the scenarios it runs, so this guard stopped covering it");

        var neverRun = accepted.Except(run, StringComparer.Ordinal).ToArray();
        var unknown = run.Except(accepted, StringComparer.Ordinal).ToArray();

        Assert.True(
            neverRun.Length == 0 && unknown.Length == 0,
            "the scenarios the live check accepts and the ones it runs have drifted apart: " +
            $"never run: {string.Join(", ", neverRun)}; not accepted: {string.Join(", ", unknown)}");

        var instructions = RepositorySources.TextAt(Instructions);
        var unmentioned = run
            .Where(name => !instructions.Contains($"`{name}`", StringComparison.Ordinal))
            .ToArray();

        Assert.True(
            unmentioned.Length == 0,
            $"the live check runs these scenarios, but {Instructions} never names them: {string.Join(", ", unmentioned)}");
    }

    /// <summary>One capture that has to exist: an extraction that matched nothing would pass every later check.</summary>
    private static int Single(Regex pattern, string group, string text, string what)
    {
        var matches = pattern.Matches(text);
        Assert.True(matches.Count == 1, $"the live check no longer states its {what} once, but {matches.Count} time(s)");

        var value = matches[0].Groups[group].Value;
        Assert.True(int.TryParse(value, out var parsed), $"the live check's {what} is not a number: '{value}'");
        return parsed;
    }

    private static string[] Names(Regex pattern, string text)
    {
        var matches = pattern.Matches(text);
        Assert.True(matches.Count == 1, $"the live check no longer states its scenario list once, but {matches.Count} time(s)");

        return Quoted.Matches(matches[0].Groups["names"].Value)
            .Select(match => match.Groups["name"].Value)
            .ToArray();
    }
}
