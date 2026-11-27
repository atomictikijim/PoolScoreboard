using System.Diagnostics;
using System.IO;
using System.Text.Json;
using PoolScoreboard.Core.Persistence;

namespace PoolScoreboard.Controller.Persistence;

/// <summary>
/// The match in progress, kept as <c>match.json</c> under <c>%AppData%\PoolScoreboard</c> so a crash or a
/// forced restart mid-match doesn't cost the scores. It is small — a few numbers and a ball list — which is
/// what lets it be rewritten after every change, unlike the profile next to it, whose icon files are up to
/// 1.5 MB.
/// </summary>
internal sealed class MatchStateStore : IMatchSnapshotStore
{
    /// <summary>
    /// Serializes saves and deletes against each other. The debounce timer and <c>OnExit</c> can both reach this
    /// store at once — a point scored just before the app closes — and two writers sharing one temporary file
    /// would race: one moves it while the other is still writing, and the loser's snapshot is lost.
    /// </summary>
    private readonly object _sync = new();

    public MatchStateStore(string? rootFolder = null)
    {
        RootFolder = rootFolder ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PoolScoreboard");
        MatchPath = Path.Combine(RootFolder, "match.json");
    }

    public string RootFolder { get; }

    public string MatchPath { get; }

    /// <summary>
    /// Where a match file that could not be read is kept, so a later save cannot destroy the only copy of a
    /// match that was still in progress.
    /// </summary>
    public string CorruptMatchPath => Path.Combine(
        Path.GetDirectoryName(MatchPath) ?? string.Empty,
        Path.GetFileNameWithoutExtension(MatchPath) + ".corrupt" + Path.GetExtension(MatchPath));

    /// <summary>Returns null when no match was saved, or when the file can't be used.</summary>
    public ResumableMatch? Load()
    {
        try
        {
            if (!File.Exists(MatchPath))
                return null;

            var match = JsonSerializer.Deserialize<ResumableMatch>(File.ReadAllText(MatchPath), PersistenceJson.Options);
            if (match == null)
                return null;

            if (match.FileVersion != ResumableMatch.Version)
            {
                // Left where it is rather than moved aside: it may be a sound match written by a newer build,
                // and this one can neither read it nor overwrite it with a snapshot it doesn't understand.
                Trace.TraceWarning(
                    $"The saved match is version {match.FileVersion}; this build reads version " +
                    $"{ResumableMatch.Version} and did not resume it.");
                return null;
            }

            return match;
        }
        catch (Exception exception) when (exception is IOException
                                             or UnauthorizedAccessException
                                             or JsonException)
        {
            // Startup must not be blocked by an unreadable or hand-edited file, but it must not be silent
            // either — and the next save must not be the only place this file ends up.
            var preserved = PreserveUnreadableFile();
            Trace.TraceWarning(
                $"Could not read the saved match: {exception.Message}" +
                (preserved ? $" The file was kept as {Path.GetFileName(CorruptMatchPath)} for recovery." : string.Empty));
            return null;
        }
    }

    /// <summary>
    /// Saves, but never throws: this runs from the debounce timer and again from <c>OnExit</c>, where a locked
    /// or full disk must not take the app down mid-match. Returns whether it wrote; a failure is logged,
    /// because the previous snapshot staying in place is otherwise indistinguishable from a successful save.
    /// </summary>
    public bool TrySave(ResumableMatch match)
    {
        lock (_sync)
        {
            try
            {
                Directory.CreateDirectory(RootFolder);

                // Write then move, like the other files here: a crash mid-save leaves the previous snapshot
                // intact rather than a truncated file that would fail to load next time.
                var temporaryPath = MatchPath + ".tmp";

                // Flush(true) is what makes this survive a power cut and not just a process kill: it pushes the
                // bytes, and the file's new length, through the OS cache to the disk before the move gives the
                // name to them. Without it a machine that loses power can come back to a file that exists and is
                // empty or half written, because the rename was recorded while the contents were still in memory.
                using (var stream = new FileStream(
                           temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    JsonSerializer.Serialize(stream, match, PersistenceJson.Options);
                    stream.Flush(flushToDisk: true);
                }

                // The rename itself is metadata, which NTFS journals rather than something this can flush; the
                // guarantee above is about the snapshot's contents being on the disk under that name.
                File.Move(temporaryPath, MatchPath, overwrite: true);
                return true;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                Trace.TraceWarning($"Could not write the saved match: {exception.Message}");
                return false;
            }
        }
    }

    /// <summary>
    /// Removes the snapshot when the state is back at setup, so a finished or reset match is not resumed on
    /// the next launch. Safe when there is no file, and a failure only costs a stale resume, not a session.
    /// </summary>
    public void Delete()
    {
        lock (_sync)
        {
            try
            {
                if (File.Exists(MatchPath))
                    File.Delete(MatchPath);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                Trace.TraceWarning($"Could not delete the saved match: {exception.Message}");
            }
        }
    }

    /// <summary>
    /// Moves a file that could not be read aside as <c>match.corrupt.json</c>, replacing an older rejected
    /// copy. A file that cannot be moved — still held by whatever made the read fail — is left where it is.
    /// </summary>
    private bool PreserveUnreadableFile()
    {
        try
        {
            if (!File.Exists(MatchPath))
                return false;

            File.Move(MatchPath, CorruptMatchPath, overwrite: true);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
