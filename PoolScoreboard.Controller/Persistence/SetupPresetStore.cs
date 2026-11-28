using System.Diagnostics;
using System.IO;
using System.Text.Json;
using PoolScoreboard.Core.Persistence;

namespace PoolScoreboard.Controller.Persistence;

/// <summary>
/// The saved setups, kept as <c>presets.json</c> under <c>%AppData%\PoolScoreboard</c> — read at startup into the
/// picker on Match Setup, and rewritten whole after a save or a delete through <see cref="DurableFile"/>, so a crash
/// mid-write leaves the previous presets rather than a truncated file.
/// <para>
/// Read-modify-write, so the two writers (save and delete) take a lock: there is one writer by construction, but a
/// double-clicked button is enough to overlap them.
/// </para>
/// </summary>
internal sealed class SetupPresetStore
{
    private readonly object _sync = new();

    public SetupPresetStore(string? rootFolder = null)
    {
        RootFolder = rootFolder ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PoolScoreboard");
        PresetsPath = Path.Combine(RootFolder, "presets.json");
    }

    public string RootFolder { get; }

    public string PresetsPath { get; }

    /// <summary>
    /// Where a preset list that could not be read is kept, so the next save cannot destroy the only copy of the
    /// operator's venues.
    /// </summary>
    public string CorruptPresetsPath => Path.Combine(
        Path.GetDirectoryName(PresetsPath) ?? string.Empty,
        Path.GetFileNameWithoutExtension(PresetsPath) + ".corrupt.json");

    /// <summary>
    /// The saved presets, or an empty library when nothing has been saved yet, when the file cannot be used, or when it
    /// was written by another version. Empty rather than throwing because a broken preset file must not stop a match
    /// being set up by hand; the log says what happened.
    /// </summary>
    public PresetLibrary Load()
    {
        try
        {
            if (!File.Exists(PresetsPath))
                return new PresetLibrary();

            var library = JsonSerializer.Deserialize<PresetLibrary>(File.ReadAllText(PresetsPath), PersistenceJson.Options);
            if (library is null)
                return new PresetLibrary();

            if (library.FileVersion != PresetLibrary.Version)
            {
                // Left in place: it may be a sound file from a newer build, and this one can neither read it nor
                // overwrite it with a list it does not understand.
                Trace.TraceWarning(
                    $"The saved presets are version {library.FileVersion}; this build reads version " +
                    $"{PresetLibrary.Version} and did not read them.");
                return new PresetLibrary();
            }

            // A hand-edited file could name no list at all.
            library.Presets ??= new();
            return library;
        }
        catch (Exception exception) when (exception is IOException
                                             or UnauthorizedAccessException
                                             or JsonException)
        {
            var preserved = PreserveUnreadableFile();
            Trace.TraceWarning(
                $"Could not read the setup presets: {exception.Message}" +
                (preserved ? $" The file was kept as {Path.GetFileName(CorruptPresetsPath)} for recovery." : string.Empty));
            return new PresetLibrary();
        }
    }

    /// <summary>
    /// Saves <paramref name="preset"/> under its name — replacing the entry that name already holds — and returns the
    /// stored preset, or null when it could not be stored (a blank name, a full library, a file that cannot be written).
    /// Never throws: this runs from a button press, where a locked or full disk must not take the app down.
    /// </summary>
    public SetupPreset? TryUpsert(SetupPreset preset)
    {
        lock (_sync)
        {
            var library = Load();
            var stored = library.Upsert(preset);
            if (stored is null)
                return null;

            return TrySave(library) ? stored : null;
        }
    }

    /// <summary>
    /// Removes the preset saved under <paramref name="name"/> and rewrites the rest, returning false when the library
    /// held no such preset or could not be written.
    /// </summary>
    public bool TryDelete(string? name)
    {
        lock (_sync)
        {
            var library = Load();
            return library.Forget(name) && TrySave(library);
        }
    }

    /// <summary>Rewrites the whole library, through the temporary file <see cref="DurableFile"/> uses.</summary>
    public bool TrySave(PresetLibrary library)
    {
        lock (_sync)
        {
            try
            {
                Directory.CreateDirectory(RootFolder);
                DurableFile.WriteAllText(PresetsPath, JsonSerializer.Serialize(library, PersistenceJson.Options));
                return true;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                Trace.TraceWarning($"Could not write the setup presets: {exception.Message}");
                return false;
            }
        }
    }

    /// <summary>
    /// Moves a file that could not be read aside as <c>presets.corrupt.json</c>, replacing an older rejected copy. A file
    /// that cannot be moved — still held by whatever made the read fail — is left where it is.
    /// </summary>
    private bool PreserveUnreadableFile()
    {
        try
        {
            if (!File.Exists(PresetsPath))
                return false;

            File.Move(PresetsPath, CorruptPresetsPath, overwrite: true);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
