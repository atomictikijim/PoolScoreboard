using System.IO;
using System.Text.RegularExpressions;

namespace PoolScoreboard.Controller.Tests;

/// <summary>
/// The one place the source guards find the repository and decide what counts as source: a walk up from the test
/// output folder to <c>PoolScoreboard.sln</c>, then every file under a project minus its build output. Shared so the
/// address, route and bind guards all scan the same set — a file one of them silently stopped seeing would make that
/// guard pass for the wrong reason.
/// </summary>
internal static class RepositorySources
{
    /// <summary>Ordinary C# string literals, including interpolated ones (the <c>$</c> sits outside the quotes).</summary>
    private static readonly Regex Literal = new("\"(?:[^\"\\\\]|\\\\.)*\"", RegexOptions.Compiled);

    private static readonly Lazy<string> RootDirectory = new(FindRoot);

    /// <summary>The folder holding <c>PoolScoreboard.sln</c>, found above the test output folder.</summary>
    internal static string Root => RootDirectory.Value;

    /// <summary>Source files under <paramref name="project"/> matching any of <paramref name="patterns"/>.</summary>
    internal static IEnumerable<string> Files(string project, params string[] patterns) =>
        patterns
            .SelectMany(pattern => Directory.EnumerateFiles(
                Path.Combine(Root, project),
                pattern,
                SearchOption.AllDirectories))
            .Where(path => !IsBuildOutput(path))
            .Distinct()
            .OrderBy(path => path, StringComparer.Ordinal);

    internal static string Text(string path) => File.ReadAllText(path);

    /// <summary>Reads a file named relative to the repository root, for sources outside any project folder.</summary>
    internal static string TextAt(string relativePath) => File.ReadAllText(Path.Combine(Root, relativePath));

    internal static string Relative(string path) => Path.GetRelativePath(Root, path);

    /// <summary>
    /// The string literals in a source text: what the guards treat as behaviour, as opposed to comments, which are
    /// documentation and may name things the code must not.
    /// </summary>
    internal static IEnumerable<string> StringLiterals(string text) =>
        Literal.Matches(text).Select(match => match.Value);

    private static bool IsBuildOutput(string path)
    {
        var segments = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return segments.Contains("bin") || segments.Contains("obj");
    }

    private static string FindRoot()
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
