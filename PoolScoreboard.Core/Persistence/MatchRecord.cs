using PoolScoreboard.Core.Enums;
using PoolScoreboard.Core.Models;

namespace PoolScoreboard.Core.Persistence;

/// <summary>
/// One finished match as the history keeps it: who played, under what rule, and how it ended. Kept in Core with
/// no file access, so what a record captures is unit-testable; the Controller owns the file.
/// <para>
/// The end-cap artwork and the player ids are deliberately not here — the roster still owns both, and a record
/// that carried a copy would keep an icon alive after the operator deleted the player. The ball detail is not
/// here either: a finished match's pocketed balls say nothing about the next one.
/// </para>
/// </summary>
public class MatchRecord
{
    /// <summary>Bumped whenever the shape below changes, so an older build does not read a newer file.</summary>
    public const int Version = 1;

    public int FileVersion { get; set; } = Version;

    /// <summary>When the winning point landed, from the caller's clock — Core has no clock of its own.</summary>
    public DateTimeOffset CompletedAt { get; set; }

    /// <summary>
    /// The match this record is: what an archive checks to tell a repeated event for one match from a rematch
    /// that happens to end with the same names and score.
    /// </summary>
    public string MatchId { get; set; } = string.Empty;

    public GameType GameType { get; set; }

    public RaceToMode RaceToMode { get; set; }

    public string HomeName { get; set; } = string.Empty;

    public string HomeTeamName { get; set; } = string.Empty;

    /// <summary>The side's own target, so a split race is recorded as 7-5 rather than a single shared number.</summary>
    public int HomeRaceTo { get; set; }

    public int HomeScore { get; set; }

    public string AwayName { get; set; } = string.Empty;

    public string AwayTeamName { get; set; } = string.Empty;

    public int AwayRaceTo { get; set; }

    public int AwayScore { get; set; }

    public bool HomeWon { get; set; }

    /// <summary>
    /// Reads a finished match, or null when there is nothing to record. A state counts as finished when it has a
    /// winner, and only then: <see cref="GameManager"/> recomputes the winner after every score change, so a
    /// corrected score takes the winner away again and a match that is merely in progress has none. Requiring
    /// that rather than comparing the scores to the targets here keeps this from disagreeing with the banner the
    /// operator and the overlay are already looking at.
    /// </summary>
    public static MatchRecord? Capture(GameState? state, DateTimeOffset completedAt)
    {
        if (state?.Winner is null)
            return null;

        return new MatchRecord
        {
            CompletedAt = completedAt,
            MatchId = state.MatchId,
            GameType = state.GameType,
            RaceToMode = state.RaceToMode,
            HomeName = state.Player1.Name,
            HomeTeamName = state.Player1.TeamName,
            HomeRaceTo = state.Player1.RaceToTarget,
            HomeScore = state.Player1Score,
            AwayName = state.Player2.Name,
            AwayTeamName = state.Player2.TeamName,
            AwayRaceTo = state.Player2.RaceToTarget,
            AwayScore = state.Player2Score,
            HomeWon = ReferenceEquals(state.Winner, state.Player1)
        };
    }
}
