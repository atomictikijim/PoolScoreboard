using PoolScoreboard.Core.Persistence;

namespace PoolScoreboard.Controller.Persistence;

/// <summary>
/// The little bit of storage the match autosave needs: load what was saved, replace it, or delete it. An interface
/// so the debounce, the delete-on-reset rule and resume can be tested against a store that counts calls, while the
/// real one (<see cref="MatchStateStore"/>) keeps its own tests for the file itself.
/// </summary>
internal interface IMatchSnapshotStore
{
    /// <summary>The saved match, or null when there is none or it cannot be used.</summary>
    ResumableMatch? Load();

    /// <summary>Writes a snapshot; false when the write failed (it never throws).</summary>
    bool TrySave(ResumableMatch match);

    /// <summary>Removes the snapshot; safe when there is none (it never throws).</summary>
    void Delete();
}
