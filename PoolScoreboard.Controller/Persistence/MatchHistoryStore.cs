using System.Diagnostics;
using System.IO;
using System.Text.Json;
using PoolScoreboard.Core.Models;
using PoolScoreboard.Core.Persistence;

namespace PoolScoreboard.Controller.Persistence;

/// <summary>
/// The finished matches, kept as <c>history.json</c> under <c>%AppData%\PoolScoreboard</c>. It is small — one
/// entry per match, capped by <see cref="MatchArchive.MaxRecords"/> — which is what lets the whole file be
/// rewritten after each match instead of appended to, and rewritten through <see cref="DurableFile"/> so a crash
/// or a power cut leaves the previous history rather than a truncated one.
/// <para>
/// Archiving is a read-modify-write, so two of them at once would lose one match; <see cref="_sync"/> serialises
/// them. There is one writer by construction (the single instance guard), and the file is only touched when a
/// match finishes, so this never runs on the overlay's path.
/// </para>
/// </summary>
internal sealed class MatchHistoryStore
{
    private readonly object _sync = new();

    public MatchHistoryStore(string? rootFolder = null)
    {
        RootFolder = rootFolder ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PoolScoreboard");
        HistoryPath = Path.Combine(RootFolder, "history.json");
    }

    public string RootFolder { get; }

    public string HistoryPath { get; }

    /// <summary>
    /// Where a history that could not be read is kept, so the next match archived cannot destroy the only copy of
    /// the matches already played.
    /// </summary>
    public string CorruptHistoryPath => Path.Combine(
        Path.GetDirectoryName(HistoryPath) ?? string.Empty,
        Path.GetFileNameWithoutExtension(HistoryPath) + ".corrupt.json");

    /// <summary>
    /// Returns the recorded matches, newest first, or an empty archive when nothing has been played yet, when the
    /// file can't be used, or when it was written by another version. An empty archive is deliberately what a
    /// failed read returns: recording the next match then starts a new history instead of blocking play.
    /// </summary>
    public MatchArchive Load()
    {
        try
        {
            if (!File.Exists(HistoryPath))
                return new MatchArchive();

            var archive = JsonSerializer.Deserialize<MatchArchive>(File.ReadAllText(HistoryPath), PersistenceJson.Options);
            if (archive is null)
                return new MatchArchive();

            if (archive.FileVersion != MatchArchive.Version)
            {
                // Left where it is rather than moved aside: it may be a sound history written by a newer build,
                // and this one can neither read it nor overwrite it with a list it doesn't understand.
                Trace.TraceWarning(
                    $"The match history is version {archive.FileVersion}; this build reads version " +
                    $"{MatchArchive.Version} and did not read it.");
                return new MatchArchive();
            }

            // A hand-edited file could name no list at all.
            archive.Matches ??= new();
            return archive;
        }
        catch (Exception exception) when (exception is IOException
                                             or UnauthorizedAccessException
                                             or JsonException)
        {
            var preserved = PreserveUnreadableFile();
            Trace.TraceWarning(
                $"Could not read the match history: {exception.Message}" +
                (preserved ? $" The file was kept as {Path.GetFileName(CorruptHistoryPath)} for recovery." : string.Empty));
            return new MatchArchive();
        }
    }

    /// <summary>
    /// Archives one finished match and returns its record, or null when there was nothing to record or the file
    /// could not be written — the log tells the two apart. Never throws: this is called at the moment a match ends,
    /// where a full or locked disk must not take the app down.
    /// </summary>
    public MatchRecord? TryArchive(GameState state, DateTimeOffset completedAt)
    {
        lock (_sync)
        {
            var archive = Load();
            var record = archive.Archive(state, completedAt);
            if (record is null)
                return null;

            return TrySave(archive) ? record : null;
        }
    }

    /// <summary>
    /// Removes one recorded match by id and rewrites the rest, returning false when the history held no such
    /// match or could not be written. The history is rewritten whole rather than patched in place, so a crash
    /// mid-removal leaves the previous history rather than a half-edited file.
    /// </summary>
    public bool TryDelete(string? matchId)
    {
        lock (_sync)
        {
            var archive = Load();
            return archive.Forget(matchId) && TrySave(archive);
        }
    }

    /// <summary>Rewrites the whole history, through the temporary file <see cref="DurableFile"/> uses.</summary>
    public bool TrySave(MatchArchive archive)
    {
        lock (_sync)
        {
            try
            {
                Directory.CreateDirectory(RootFolder);
                DurableFile.WriteAllText(HistoryPath, JsonSerializer.Serialize(archive, PersistenceJson.Options));
                return true;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                Trace.TraceWarning($"Could not write the match history: {exception.Message}");
                return false;
            }
        }
    }

    /// <summary>
    /// Moves a file that could not be read aside as <c>history.corrupt.json</c>, replacing an older rejected copy.
    /// A file that cannot be moved — still held by whatever made the read fail — is left where it is.
    /// </summary>
    private bool PreserveUnreadableFile()
    {
        try
        {
            if (!File.Exists(HistoryPath))
                return false;

            File.Move(HistoryPath, CorruptHistoryPath, overwrite: true);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
