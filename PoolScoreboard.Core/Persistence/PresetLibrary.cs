namespace PoolScoreboard.Core.Persistence;

/// <summary>
/// The saved setups: what <c>presets.json</c> holds, in the order the operator saved them. Kept in Core with no file
/// access, so the rules that make a picker trustworthy — one entry per name whatever its casing, a blank name refused,
/// a bounded list — are unit-testable.
/// <para>
/// A name is the identity, ignoring case, the way the roster treats a player's. Saving under a name that already
/// exists replaces that entry instead of adding a second one a picker would show twice, and the newest spelling of the
/// name wins, so a typo can be corrected by saving again.
/// </para>
/// </summary>
public class PresetLibrary
{
    /// <summary>Bumped whenever the shape below changes, so an older build does not read a newer file.</summary>
    public const int Version = 1;

    /// <summary>
    /// How many presets one machine keeps. Small on purpose: a preset is a venue, and a picker with a hundred of them
    /// is not a picker. A new name past the cap is refused rather than silently dropping one the operator saved.
    /// </summary>
    public const int MaxPresets = 50;

    public int FileVersion { get; set; } = Version;

    public List<SetupPreset> Presets { get; set; } = new();

    /// <summary>
    /// Adds <paramref name="preset"/>, or replaces the entry with the same name ignoring case. Returns the stored
    /// preset, or null when it could not be stored: a blank name, or a full library with a name it does not already
    /// hold. Never drops an existing preset to make room.
    /// </summary>
    public SetupPreset? Upsert(SetupPreset preset)
    {
        var name = SetupPreset.NormalizeName(preset.Name);
        if (name.Length == 0)
            return null;

        preset.Name = name;

        var index = Presets.FindIndex(existing =>
            string.Equals(existing.Name, name, StringComparison.OrdinalIgnoreCase));

        if (index >= 0)
        {
            Presets[index] = preset;
            return preset;
        }

        if (Presets.Count >= MaxPresets)
            return null;

        Presets.Add(preset);
        return preset;
    }

    /// <summary>The preset saved under <paramref name="name"/>, ignoring case, or null.</summary>
    public SetupPreset? Find(string? name)
    {
        var wanted = SetupPreset.NormalizeName(name);
        if (wanted.Length == 0)
            return null;

        return Presets.Find(existing =>
            string.Equals(existing.Name, wanted, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Removes the preset saved under <paramref name="name"/>. False when there was no such preset.</summary>
    public bool Forget(string? name)
    {
        var wanted = SetupPreset.NormalizeName(name);
        if (wanted.Length == 0)
            return false;

        var index = Presets.FindIndex(existing =>
            string.Equals(existing.Name, wanted, StringComparison.OrdinalIgnoreCase));

        if (index < 0)
            return false;

        Presets.RemoveAt(index);
        return true;
    }

    /// <summary>The names, alphabetically ignoring case — the order a picker should offer them in.</summary>
    public IReadOnlyList<string> Names() =>
        Presets
            .Select(preset => preset.Name)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();
}
