using System.IO;
using System.Text.RegularExpressions;

namespace PoolScoreboard.Controller.Tests;

/// <summary>
/// The live check drives the real window through UI Automation, so it depends on text this app chooses: the
/// AutomationId of the port field, the names of the buttons it presses, and the banner wording it waits for. The CI
/// parse check only proves the script is valid PowerShell — rename any of that and the script still parses, then
/// fails at the next manual run, which is exactly the rot the parse check was added to stop.
/// <para>
/// The literals are read out of the script rather than listed here, so the guard follows it: change the script's
/// wording and this asks about the new wording. Every extraction asserts it found something, because a rewrite that
/// renamed the script's helpers would otherwise leave this passing over an empty set.
/// </para>
/// <para>
/// Matching is deliberately strict about <em>where</em> the text may be found. Control names are compared for equality
/// against the values of the attributes that actually become UI Automation names — <c>Content</c>, <c>Text</c>,
/// <c>Title</c>, <c>Header</c>, <c>AutomationProperties.*</c>, <c>x:Name</c> — because a first draft that searched the
/// sources for the word "Retry" passed a mutation that renamed the button: the word was still in a <c>ToolTip</c> and
/// in <c>Command="{Binding RetryOverlayCommand}"</c>, neither of which is a control name. Waited-for phrases are
/// searched in <c>Text</c> values and C# string literals only, so a tooltip or a comment cannot stand in for the
/// banner either.
/// </para>
/// </summary>
public sealed class LiveCheckUiTextTests
{
    private const string Script = "tools/verify-live.ps1";

    /// <summary>Label passed to the script's "press the element named …" helper.</summary>
    private static readonly Regex PressedLabel = new(@"Invoke-WindowButton[^\r\n']*'(?<text>[^']+)'", RegexOptions.Compiled);

    /// <summary>Window text the script waits for: a <c>-match</c> pattern whose alternatives are each real text.</summary>
    private static readonly Regex WaitedText = new(@"Wait-WindowText[^\r\n']*'(?<text>[^']+)'", RegexOptions.Compiled);

    /// <summary>Names looked up by UI Automation — a control's visible text, or a window's title.</summary>
    private static readonly Regex NamedElement = new(@"NameProperty,\s*'(?<text>[^']+)'", RegexOptions.Compiled);

    /// <summary>Ids looked up by UI Automation, which WPF takes from <c>x:Name</c>.</summary>
    private static readonly Regex IdentifiedElement = new(@"AutomationIdProperty,\s*'(?<text>[^']+)'", RegexOptions.Compiled);

    /// <summary>The attributes that become part of what UI Automation reports for a control.</summary>
    private static readonly Regex MarkupAttribute = new(
        "(?<name>AutomationProperties\\.AutomationId|AutomationProperties\\.Name|x:Name|Content|Text|Title|Header)" +
        "\\s*=\\s*\"(?<value>[^\"]*)\"",
        RegexOptions.Compiled);

    [Fact]
    public void EveryControlTheLiveCheckNamesIsStillNamedThatInTheMarkup()
    {
        var script = RepositorySources.TextAt(Script);

        var expected = new (string Kind, IEnumerable<string> Values)[]
        {
            ("button label", Captures(PressedLabel, script)),
            ("element name", Captures(NamedElement, script)),
            ("automation id", Captures(IdentifiedElement, script)),
        };

        var named = MarkupAttributes().Select(attribute => attribute.Value).ToArray();
        var missing = new List<string>();

        foreach (var (kind, values) in expected)
        {
            var found = values.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            Assert.True(found.Length > 0, $"the live check no longer names a {kind}, so this guard stopped covering it");

            missing.AddRange(found
                .Where(text => !named.Contains(text, StringComparer.OrdinalIgnoreCase))
                .Select(text => $"{kind} '{text}'"));
        }

        Assert.True(
            missing.Count == 0,
            $"the live check looks these up by name, but no Controller element carries them any more: " +
            string.Join("; ", missing));
    }

    [Fact]
    public void EveryTextTheLiveCheckWaitsForIsStillShownByTheController()
    {
        var script = RepositorySources.TextAt(Script);

        var phrases = Captures(WaitedText, script)
            .SelectMany(Alternatives)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.True(phrases.Length > 0, "the live check no longer waits for any window text, so this guard stopped covering it");

        var shown = string.Join(
            "\n",
            MarkupAttributes().Where(attribute => attribute.Name == "Text").Select(attribute => attribute.Value)
                .Concat(ControllerStringLiterals()));

        var missing = phrases
            .Where(phrase => shown.IndexOf(phrase, StringComparison.OrdinalIgnoreCase) < 0)
            .ToArray();

        Assert.True(
            missing.Length == 0,
            $"the live check waits for these, but the Controller no longer shows them: {string.Join("; ", missing)}");
    }

    private static IEnumerable<string> Captures(Regex pattern, string text) =>
        pattern.Matches(text).Select(match => match.Groups["text"].Value.Trim());

    /// <summary>Each alternative of a <c>-match</c> pattern has to exist on its own.</summary>
    private static IEnumerable<string> Alternatives(string pattern) =>
        pattern.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static IEnumerable<(string Name, string Value)> MarkupAttributes() =>
        RepositorySources
            .Files("PoolScoreboard.Controller", "*.xaml")
            .SelectMany(file => MarkupAttribute.Matches(RepositorySources.Text(file))
                .Select(match => (Name: match.Groups["name"].Value, Value: match.Groups["value"].Value.Trim())));

    private static IEnumerable<string> ControllerStringLiterals() =>
        RepositorySources
            .Files("PoolScoreboard.Controller", "*.cs")
            .SelectMany(file => RepositorySources.StringLiterals(RepositorySources.Text(file)));
}
