using PoolScoreboard.Core.Models;

namespace PoolScoreboard.Core.Persistence;

/// <summary>
/// The recorded matches, newest first. Pure: which match becomes a record and how many are kept live here so both
/// are unit-tested; the Controller owns the file they are read from and written to.
/// </summary>
public class MatchArchive
{
    /// <summary>Bumped whenever the shape below changes, so an older build does not read a newer file.</summary>
    public const int Version = 1;

    /// <summary>
    /// Enough for a long run of play without letting a stray file grow without bound — the same reasoning as the
    /// roster's cap, with a higher number because several matches fit in one evening.
    /// </summary>
    public const int MaxRecords = 200;

    public int FileVersion { get; set; } = Version;

    public List<MatchRecord> Matches { get; set; } = new();

    /// <summary>
    /// The archive call: turns a finished match into a record, files it where the newest is read first, and drops
    /// the oldest once the list is full. Returns the record, or null when the state holds no finished match —
    /// a caller that archives whenever the state changes must be able to tell "nothing to do" from "wrote one",
    /// or it would fill the file with blank entries.
    /// </summary>
    public MatchRecord? Archive(GameState? state, DateTimeOffset completedAt)
    {
        var record = MatchRecord.Capture(state, completedAt);
        if (record is null)
            return null;

        Matches.Insert(0, record);
        if (Matches.Count > MaxRecords)
            Matches.RemoveRange(MaxRecords, Matches.Count - MaxRecords);

        return record;
    }
}
