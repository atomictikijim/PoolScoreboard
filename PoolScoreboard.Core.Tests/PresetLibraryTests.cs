using PoolScoreboard.Core.Persistence;

namespace PoolScoreboard.Core.Tests;

/// <summary>
/// The rules that make the preset picker trustworthy: one entry per name whatever the casing, no unnamed entry nobody
/// could pick, a bounded list that refuses a new name rather than silently dropping one the operator saved.
/// </summary>
public sealed class PresetLibraryTests
{
    private static SetupPreset Preset(string name, int shotClockSeconds = 40) =>
        new() { Name = name, ShotClockSeconds = shotClockSeconds };

    [Fact]
    public void Upsert_AddsANewPreset_AndARepeatOfTheSameNameReplacesIt()
    {
        var library = new PresetLibrary();

        library.Upsert(Preset("House A", 40));
        library.Upsert(Preset("House A", 55));

        var only = Assert.Single(library.Presets);
        Assert.Equal(55, only.ShotClockSeconds);
    }

    [Fact]
    public void Upsert_IgnoresCase_AndKeepsTheNewestSpellingOfTheName()
    {
        var library = new PresetLibrary();
        library.Upsert(Preset("house a"));

        library.Upsert(Preset("House A"));

        // A typo fixed by saving again must rename the entry, not leave two a picker would show twice.
        Assert.Equal("House A", Assert.Single(library.Presets).Name);
        Assert.NotNull(library.Find("HOUSE A"));
    }

    [Fact]
    public void Upsert_RefusesABlankName()
    {
        var library = new PresetLibrary();

        Assert.Null(library.Upsert(Preset("   ")));
        Assert.Empty(library.Presets);
    }

    [Fact]
    public void Upsert_RefusesANewNameWhenTheLibraryIsFull_WithoutDroppingOne()
    {
        var library = new PresetLibrary();
        for (var i = 0; i < PresetLibrary.MaxPresets; i++)
            Assert.NotNull(library.Upsert(Preset($"Venue {i}")));

        Assert.Null(library.Upsert(Preset("One venue too many")));
        Assert.Equal(PresetLibrary.MaxPresets, library.Presets.Count);
        Assert.NotNull(library.Find("Venue 0"));

        // An existing name is still replaceable at the cap: it adds nothing.
        Assert.NotNull(library.Upsert(Preset("Venue 0", 30)));
    }

    [Fact]
    public void Forget_RemovesOnlyTheNamedPreset_AndSaysWhetherItWasThere()
    {
        var library = new PresetLibrary();
        library.Upsert(Preset("House A"));
        library.Upsert(Preset("House B"));

        Assert.True(library.Forget("house b"));
        Assert.Equal(new[] { "House A" }, library.Names());

        Assert.False(library.Forget("House B"));
        Assert.False(library.Forget(null));
    }

    [Fact]
    public void Names_AreAlphabeticalIgnoringCase()
    {
        var library = new PresetLibrary();
        library.Upsert(Preset("zeta"));
        library.Upsert(Preset("Alpha"));
        library.Upsert(Preset("beta"));

        Assert.Equal(new[] { "Alpha", "beta", "zeta" }, library.Names());
    }
}
