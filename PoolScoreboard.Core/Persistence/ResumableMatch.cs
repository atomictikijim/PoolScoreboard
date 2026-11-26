using PoolScoreboard.Core.Enums;
using PoolScoreboard.Core.Models;

namespace PoolScoreboard.Core.Persistence;

/// <summary>
/// The part of a match in progress worth surviving a crash: the scores and everything the overlay would get
/// wrong if it came back blank — game type, Race-To mode, ball groups, pocketed balls and whose turn it is.
/// Kept in Core with no file access, so capture and restore are unit-testable; the Controller owns reading
/// and writing it.
/// <para>
/// The players' names, Race-To targets and artwork are not here: they belong to the operator profile, which
/// is restored before this and is what the scores are judged against.
/// </para>
/// </summary>
public class ResumableMatch
{
    /// <summary>Bumped whenever the shape below changes, so an older build does not resume a newer file.</summary>
    public const int Version = 1;

    public int FileVersion { get; set; } = Version;

    public GameType GameType { get; set; }

    public RaceToMode RaceToMode { get; set; }

    public int HomeScore { get; set; }

    public int AwayScore { get; set; }

    public bool HomeIsCurrentShooter { get; set; }

    public BallGroup HomeBallGroup { get; set; }

    public BallGroup AwayBallGroup { get; set; }

    public List<int> PocketedBalls { get; set; } = new();

    /// <summary>
    /// Reads a snapshot, returning null when there is no match to resume. A state at setup is deliberately
    /// not saved, which is what lets the caller treat "nothing to resume" as "delete the file".
    /// </summary>
    public static ResumableMatch? Capture(GameState state)
    {
        if (!state.IsGameActive)
            return null;

        return new ResumableMatch
        {
            GameType = state.GameType,
            RaceToMode = state.RaceToMode,
            HomeScore = state.Player1Score,
            AwayScore = state.Player2Score,
            HomeIsCurrentShooter = ReferenceEquals(state.CurrentShooter, state.Player1),
            HomeBallGroup = state.Player1.BallGroup,
            AwayBallGroup = state.Player2.BallGroup,
            PocketedBalls = state.PocketedBalls.OrderBy(number => number).ToList()
        };
    }
}
