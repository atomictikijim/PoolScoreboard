using System.IO;
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

    /// <summary>Ordinary C# string literals, including interpolated ones (the <c>$</c> sits outside the quotes).</summary>
    private static readonly Regex StringLiteral = new("\"(?:[^\"\\\\]|\\\\.)*\"", RegexOptions.Compiled);

    [Fact]
    public void NoControllerMarkupCarriesTheOverlayAddressOrTheDefaultPort()
    {
        var offenders = ControllerSources("*.xaml")
            .SelectMany(file => Tokens(File.ReadAllText(file), "127.0.0.1", DefaultPort)
                .Select(token => $"{Relative(file)} has {token}"))
            .ToArray();

        Assert.True(offenders.Length == 0, $"the Controller's markup hard-codes: {string.Join("; ", offenders)}");
    }

    [Fact]
    public void NoControllerStringHardCodesTheOverlayAddressWithAPortOrTheDefaultPort()
    {
        var offenders = ControllerSources("*.cs")
            .SelectMany(file => StringLiteral.Matches(File.ReadAllText(file))
                .Select(match => match.Value)
                .Where(literal => literal.Contains(DefaultPort, StringComparison.Ordinal)
                                  || HardCodedAddressAndPort.IsMatch(literal))
                .Select(literal => $"{Relative(file)} has {literal}"))
            .ToArray();

        Assert.True(offenders.Length == 0, $"the Controller's code hard-codes: {string.Join("; ", offenders)}");
    }

    private static IEnumerable<string> Tokens(string text, params string[] tokens) =>
        tokens.Where(token => text.Contains(token, StringComparison.Ordinal));

    private static IEnumerable<string> ControllerSources(string pattern) =>
        Directory
            .EnumerateFiles(
                Path.Combine(RepositoryRoot(), "PoolScoreboard.Controller"),
                pattern,
                SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(path));

    private static bool IsBuildOutput(string path)
    {
        var segments = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return segments.Contains("bin") || segments.Contains("obj");
    }

    private static string Relative(string path) => Path.GetRelativePath(RepositoryRoot(), path);

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "PoolScoreboard.sln")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new InvalidOperationException("PoolScoreboard.sln was not found above the test output folder.");
    }
}
