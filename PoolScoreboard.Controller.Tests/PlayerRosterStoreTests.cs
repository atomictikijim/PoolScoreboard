using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using PoolScoreboard.Controller.Diagnostics;
using PoolScoreboard.Controller.Persistence;
using PoolScoreboard.Core.Persistence;

namespace PoolScoreboard.Controller.Tests;

/// <summary>
/// Like the settings store's tests: real files in a throwaway folder, so the roster's JSON and icon
/// handling are covered in CI rather than only by running the app.
/// </summary>
public sealed class PlayerRosterStoreTests : IDisposable
{
    private static readonly byte[] PngBytes = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x01 };
    private static readonly byte[] SvgBytes = Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\"></svg>");

    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "poolscoreboard-roster-tests", Guid.NewGuid().ToString("n"));

    private string RosterPath => Path.Combine(_root, "roster.json");

    private string IconsFolder => Path.Combine(_root, "icons");

    private string CorruptRosterPath => Path.Combine(_root, "roster.corrupt.json");

    private string CorruptIconsFolder => Path.Combine(_root, "roster.corrupt.icons");

    private PlayerRosterStore Store => new(_root);

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private static string Png() => EndCapIconData.ToDataUri("image/png", PngBytes);

    private static string Svg() => EndCapIconData.ToDataUri("image/svg+xml", SvgBytes);

    private static PlayerRoster Roster(params (string Id, string Name, string Team, int RaceTo)[] players)
    {
        var roster = new PlayerRoster();
        foreach (var (id, name, team, raceTo) in players)
            roster.Players.Add(new RosterEntry { Id = id, Name = name, TeamName = team, RaceToTarget = raceTo });
        return roster;
    }

    /// <summary>What a fresh start would see: load the roster first, then ask for one player's artwork.</summary>
    private string? IconFor(string entryId)
    {
        var store = Store;
        store.Load();
        return store.LoadIcon(entryId);
    }

    private string[] IconFiles() =>
        Directory.Exists(IconsFolder)
            ? Directory.GetFiles(IconsFolder).Select(Path.GetFileName).OfType<string>().OrderBy(n => n).ToArray()
            : Array.Empty<string>();

    [Fact]
    public void SaveThenLoad_RoundTripsPlayersInOrder()
    {
        Store.Save(Roster(("b", "Bob", "Red Rackers", 5), ("a", "Alice", "Blue Cuers", 7)));

        var loaded = Store.Load();

        Assert.Equal(new[] { "Bob", "Alice" }, loaded.Players.Select(p => p.Name));
        Assert.Equal(new[] { "Red Rackers", "Blue Cuers" }, loaded.Players.Select(p => p.TeamName));
        Assert.Equal(new[] { 5, 7 }, loaded.Players.Select(p => p.RaceToTarget));
        Assert.Equal(new[] { "b", "a" }, loaded.Players.Select(p => p.Id));
    }

    [Fact]
    public void SaveThenLoad_RoundTripsEachPlayersIcon()
    {
        var roster = Roster(("a", "Alice", "", 7), ("b", "Bob", "", 5));
        var store = Store;
        store.SetIcon("a", Png());
        store.SetIcon("b", Svg());
        store.Save(roster);

        var reloaded = Store;
        var loaded = reloaded.Load();

        Assert.Equal(new[] { "a", "b" }, loaded.Players.Select(p => p.Id));
        Assert.Equal(Png(), reloaded.LoadIcon("a"));
        Assert.Equal(Svg(), reloaded.LoadIcon("b"));
        Assert.Equal(new[] { "roster-a.png", "roster-b.svg" }, IconFiles());

        var json = File.ReadAllText(RosterPath);
        Assert.DoesNotContain("base64", json);
        Assert.Contains("roster-a.png", json);
    }

    [Fact]
    public void SetIcon_ChangingTheMime_LeavesOnlyTheNewFile()
    {
        var store = Store;
        store.SetIcon("a", Png());
        store.SetIcon("a", Svg());
        store.Save(Roster(("a", "Alice", "", 7)));

        Assert.Equal(new[] { "roster-a.svg" }, IconFiles());
        Assert.Equal(Svg(), IconFor("a"));
    }

    [Fact]
    public void SetIcon_WithUnusableData_RemovesTheSavedArtwork()
    {
        var store = Store;
        store.SetIcon("a", Png());
        store.Save(Roster(("a", "Alice", "", 7)));

        store.SetIcon("a", "data:image/png;base64,@@not base64@@");
        store.Save(Roster(("a", "Alice", "", 7)));

        Assert.Empty(IconFiles());
        Assert.Null(IconFor("a"));
        Assert.Equal("Alice", Store.Load().Players[0].Name);
    }

    [Fact]
    public void Save_ForgettingAPlayer_RemovesTheirIcon()
    {
        var store = Store;
        store.SetIcon("a", Png());
        store.SetIcon("b", Png());
        store.Save(Roster(("a", "Alice", "", 7), ("b", "Bob", "", 5)));

        store.Save(Roster(("b", "Bob", "", 5)));

        Assert.Equal(new[] { "roster-b.png" }, IconFiles());
        Assert.Null(IconFor("a"));
        Assert.NotNull(IconFor("b"));
    }

    [Fact]
    public void TrySave_ReportsFailureInsteadOfThrowing_WhenTheFileCannotBeWritten()
    {
        var store = Store;
        var roster = new PlayerRoster();
        roster.Remember("Alice", "Blue Cuers", 7, "alice-id");
        Directory.CreateDirectory(store.RosterPath);

        var listener = new CapturingTraceListener();
        Trace.Listeners.Add(listener);
        try
        {
            Assert.False(store.TrySave(roster));
        }
        finally
        {
            Trace.Listeners.Remove(listener);
        }

        Assert.Contains(listener.Messages, message => message.Contains("Could not write the player roster"));
    }

    private sealed class CapturingTraceListener : TraceListener
    {
        public List<string> Messages { get; } = new();

        public override void Write(string? message)
        {
        }

        public override void WriteLine(string? message)
        {
            if (message != null)
                Messages.Add(message);
        }
    }

    [Fact]
    public void ForgettingAPlayerThroughTheRoster_RemovesThemAndTheirIcon()
    {
        var store = Store;
        var roster = new PlayerRoster();
        var alice = roster.Remember("Alice", "Blue Cuers", 7, "alice-id")!;
        roster.Remember("Bob", "", 5, "bob-id");
        store.SetIcon(alice.Id, Png());
        store.Save(roster);

        Assert.True(roster.Forget(alice.Id));
        Assert.True(store.TrySave(roster));

        var reloaded = Store;
        Assert.Equal(new[] { "Bob" }, reloaded.Load().Players.Select(p => p.Name));
        Assert.Null(reloaded.LoadIcon("alice-id"));
        Assert.Empty(IconFiles().Where(file => file.StartsWith("roster-alice-id", StringComparison.Ordinal)));
    }

    [Fact]
    public void Save_DeletesIconFilesThatNoLongerBelongToAnyone()
    {
        var store = Store;
        store.SetIcon("a", Png());
        store.Save(Roster(("a", "Alice", "", 7)));
        Directory.CreateDirectory(IconsFolder);
        File.WriteAllBytes(Path.Combine(IconsFolder, "roster-ghost.png"), PngBytes);

        store.Save(Roster(("a", "Alice", "", 7)));

        Assert.Contains("roster-a.png", IconFiles());
        Assert.DoesNotContain("roster-ghost.png", IconFiles());
    }

    [Fact]
    public void LoadIcon_MissingFile_ReturnsNull()
    {
        var store = Store;
        store.SetIcon("a", Png());
        store.Save(Roster(("a", "Alice", "", 7)));
        File.Delete(Path.Combine(IconsFolder, "roster-a.png"));

        var reloaded = Store;
        var roster = reloaded.Load();
        Assert.Null(reloaded.LoadIcon("a"));
        Assert.Equal("Alice", roster.Players[0].Name);
    }

    [Fact]
    public void Load_WithNoFile_ReturnsEmptyRoster()
    {
        var store = Store;
        Assert.Empty(store.Load().Players);
        Assert.Null(store.LoadIcon("a"));
    }

    [Fact]
    public void Load_WithUnreadableJson_KeepsTheFileAndItsArtworkAsideAndSaysWhyInTheLog()
    {
        var store = Store;
        store.SetIcon("a", Png());
        store.Save(Roster(("a", "Alice", "", 7)));
        const string original = "{ not json at all";
        File.WriteAllText(RosterPath, original);

        var text = CaptureLog(() => Assert.Empty(Store.Load().Players));

        Assert.False(File.Exists(RosterPath));                     // the next save writes a fresh roster
        Assert.Equal(original, File.ReadAllText(CorruptRosterPath));
        Assert.Equal(PngBytes, File.ReadAllBytes(Path.Combine(CorruptIconsFolder, "roster-a.png")));
        Assert.False(File.Exists(Path.Combine(IconsFolder, "roster-a.png")));
        Assert.Contains("Could not read the player roster", text);
        Assert.Contains("roster.corrupt.json", text);
        Assert.Contains("roster.corrupt.icons", text);
    }

    [Fact]
    public void Save_AfterARejectedRoster_LeavesThePreservedArtworkAlone()
    {
        var store = Store;
        store.SetIcon("a", Png());
        store.Save(Roster(("a", "Alice", "", 7)));
        File.WriteAllText(RosterPath, "{ not json at all");
        Assert.Empty(store.Load().Players);

        // The save that used to prune every roster-*.png now runs against an empty roster.
        Assert.True(store.TrySave(new PlayerRoster()));

        Assert.Equal(PngBytes, File.ReadAllBytes(Path.Combine(CorruptIconsFolder, "roster-a.png")));
    }

    [Fact]
    public void Load_WhenAnIconCannotBeMovedAside_StillFallsBackWithoutThrowing()
    {
        var store = Store;
        store.SetIcon("a", Png());
        store.Save(Roster(("a", "Alice", "", 7)));
        File.WriteAllText(RosterPath, "{ not json at all");
        var iconPath = Path.Combine(IconsFolder, "roster-a.png");

        string text;
        using (new FileStream(iconPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            text = CaptureLog(() => Assert.Empty(Store.Load().Players));
        }

        Assert.Contains("Could not read the player roster", text);
        Assert.DoesNotContain("artwork", text);
        Assert.True(File.Exists(iconPath));                        // left for the usual pruning
    }

    /// <summary>Runs the action with the log attached and returns what the file holds afterwards.</summary>
    private string CaptureLog(Action action)
    {
        var logPath = Path.Combine(_root, "controller.log");
        using (var log = new TraceLogFile(logPath))
        {
            log.Attach();
            action();
        }

        return File.ReadAllText(logPath);
    }

    [Fact]
    public void Load_DropsEntriesThatHaveNoId()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(RosterPath, """
            {
              "version": 1,
              "players": [
                { "id": "a", "name": "Alice", "teamName": "", "raceToTarget": 7 },
                { "id": "", "name": "Nobody", "teamName": "", "raceToTarget": 5 }
              ]
            }
            """);

        var loaded = Store.Load();

        Assert.Equal(new[] { "Alice" }, loaded.Players.Select(p => p.Name));
    }

    [Fact]
    public void Load_CapsAtTheRosterMaximum()
    {
        var players = Enumerable.Range(0, PlayerRoster.MaxEntries + 5)
            .Select(i => (Id: $"p{i}", Name: $"Player {i}", Team: "", RaceTo: 5))
            .ToArray();
        Store.Save(Roster(players));

        Assert.Equal(PlayerRoster.MaxEntries, Store.Load().Players.Count);
    }

    [Fact]
    public void Load_IgnoresAnIconFileThatWouldEscapeTheIconsFolder()
    {
        var store = Store;
        store.SetIcon("a", Png());
        store.Save(Roster(("a", "Alice", "", 7)));

        var outside = Path.Combine(Path.GetDirectoryName(_root)!, "roster-escape.png");
        File.WriteAllBytes(outside, PngBytes);
        try
        {
            File.WriteAllText(RosterPath, File.ReadAllText(RosterPath).Replace("roster-a.png", "../roster-escape.png"));

            var reloaded = Store;
            var roster = reloaded.Load();
            Assert.Null(reloaded.LoadIcon("a"));
            Assert.Equal("Alice", roster.Players[0].Name);
        }
        finally
        {
            File.Delete(outside);
        }
    }

    [Fact]
    public void Load_IgnoresAnIconWhoseMimeIsNotAnImage()
    {
        var store = Store;
        store.SetIcon("a", Png());
        store.Save(Roster(("a", "Alice", "", 7)));

        File.WriteAllText(RosterPath, File.ReadAllText(RosterPath).Replace("\"image/png\"", "\"text/html\""));

        var reloaded = Store;
        reloaded.Load();
        Assert.Null(reloaded.LoadIcon("a"));
    }

    [Fact]
    public void Save_LeavesNoTemporaryFileBehind()
    {
        Store.Save(Roster(("a", "Alice", "", 7)));

        Assert.False(File.Exists(RosterPath + ".tmp"));
        Assert.True(File.Exists(RosterPath));
    }

    [Fact]
    public void RememberedThenSavedPlayer_ComesBackWithEverythingNeededToRestoreTheSide()
    {
        var roster = new PlayerRoster();
        var store = Store;
        var entry = roster.Remember("Alice", "Blue Cuers", 7, "a")!;
        store.SetIcon(entry.Id, Png());
        store.Save(roster);

        var reloaded = Store;
        var saved = reloaded.Load().Players.Single();
        var icon = reloaded.LoadIcon(saved.Id);

        Assert.Equal("Alice", saved.Name);
        Assert.Equal("Blue Cuers", saved.TeamName);
        Assert.Equal(7, saved.RaceToTarget);
        Assert.Equal(Png(), icon);
    }
}
