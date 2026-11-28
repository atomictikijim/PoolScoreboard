using System.IO;
using PoolScoreboard.Controller.Persistence;
using PoolScoreboard.Core.Enums;
using PoolScoreboard.Core.Models;
using PoolScoreboard.Core.Persistence;

namespace PoolScoreboard.Controller.Tests;

/// <summary>
/// <c>presets.json</c> is the only place a saved venue exists, so a write that loses a field — or a file left truncated
/// by a crash — costs the operator a setup they cannot get back. These pin the file the way
/// <see cref="MatchHistoryStoreTests"/> pins the history: a fresh read gets every value, no temporary file is left,
/// a name is replaced rather than duplicated on disk, and an unreadable file is kept aside instead of overwritten.
/// </summary>
public sealed class SetupPresetStoreTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "poolscoreboard-preset-tests", Guid.NewGuid().ToString("n"));

    private SetupPresetStore NewStore() => new(_root);

    private static SetupPreset HouseA() => new()
    {
        Name = "House A",
        GameType = GameType.TenBall,
        RaceToMode = RaceToMode.Split,
        ShotClockSeconds = 35,
        ShotClockExtensionSeconds = 25,
        OverlayPort = 51241,
        ColorTheme = new ColorTheme { HomeAccent = "#123456" },
        ShotClockDisplay = ShotClockDisplay.Line,
        StaleCueEnabled = true
    };

    [Fact]
    public void TryUpsert_LeavesEveryValueForTheNextRead_AndNoTemporaryFile()
    {
        Assert.NotNull(NewStore().TryUpsert(HouseA()));

        var saved = NewStore().Load().Find("House A");

        Assert.NotNull(saved);
        Assert.Equal(GameType.TenBall, saved!.GameType);
        Assert.Equal(RaceToMode.Split, saved.RaceToMode);
        Assert.Equal(35, saved.ShotClockSeconds);
        Assert.Equal(25, saved.ShotClockExtensionSeconds);
        Assert.Equal(51241, saved.OverlayPort);
        Assert.Equal("#123456", saved.ColorTheme.HomeAccent);
        Assert.Equal(ShotClockDisplay.Line, saved.ShotClockDisplay);
        Assert.True(saved.StaleCueEnabled);
        Assert.Empty(Directory.GetFiles(_root, "*.tmp"));
    }

    [Fact]
    public void TryUpsert_ReplacesTheSameNameOnDisk_RatherThanAddingASecondEntry()
    {
        var store = NewStore();
        store.TryUpsert(HouseA());

        var renamed = HouseA();
        renamed.Name = "house a";
        renamed.ShotClockSeconds = 50;
        Assert.NotNull(store.TryUpsert(renamed));

        var library = NewStore().Load();

        Assert.Equal(50, Assert.Single(library.Presets).ShotClockSeconds);
    }

    [Fact]
    public void TryDelete_RemovesTheNamedPresetAndKeepsTheRest()
    {
        var store = NewStore();
        store.TryUpsert(HouseA());
        var second = HouseA();
        second.Name = "House B";
        store.TryUpsert(second);

        Assert.True(store.TryDelete("House A"));

        Assert.Equal(new[] { "House B" }, NewStore().Load().Names());
        Assert.False(store.TryDelete("House A"));
    }

    [Fact]
    public void AnUnreadableFileIsKeptAside_AndTheNextSaveStartsANewList()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "presets.json");
        File.WriteAllText(path, "{ this is not json");

        var store = NewStore();

        Assert.Empty(store.Load().Presets);
        Assert.Equal("{ this is not json", File.ReadAllText(store.CorruptPresetsPath));

        Assert.NotNull(store.TryUpsert(HouseA()));
        Assert.Equal(new[] { "House A" }, NewStore().Load().Names());
    }

    [Fact]
    public void AFileFromAnotherVersionIsLeftInPlace_AndReadsAsEmpty()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "presets.json");
        File.WriteAllText(path, "{ \"fileVersion\": 99, \"presets\": [ { \"name\": \"From a newer build\" } ] }");

        var store = NewStore();

        // Nothing is deleted: the newer build's file may be the only copy of what it saved.
        Assert.Empty(store.Load().Presets);
        Assert.True(File.Exists(path));
        Assert.False(File.Exists(store.CorruptPresetsPath));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp folder is not worth failing a passing test over.
        }
    }
}
