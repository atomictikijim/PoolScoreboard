using PoolScoreboard.Core.Enums;

namespace PoolScoreboard.Core.Models;

public class Player
{
    private string? _endCapIcon;

    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "";
    public string TeamName { get; set; } = "";
    public int RaceToTarget { get; set; } = 5;
    public BallGroup BallGroup { get; set; } = BallGroup.Unassigned;

    /// <summary>
    /// Optional end-cap image (team logo, country flag, etc.) as a data URI. Null means the
    /// overlay falls back to the accent-colored dot. The overlay serves these bytes from
    /// <c>/overlay/api/players/{Id}/icon</c> rather than embedding them in state snapshots.
    /// </summary>
    public string? EndCapIcon
    {
        get => _endCapIcon;
        set
        {
            _endCapIcon = value;
            EndCapIconVersion++;
        }
    }

    /// <summary>
    /// Bumped whenever <see cref="EndCapIcon"/> is assigned, so the icon URL handed to the overlay
    /// changes with the artwork — letting the browser cache the image between score updates
    /// without serving a stale logo after the operator picks a new one.
    /// </summary>
    public int EndCapIconVersion { get; private set; }

    /// <summary>
    /// Copies this player without going through the <see cref="EndCapIcon"/> setter, so copying
    /// state doesn't look like an artwork change (which would invalidate the overlay's cached icon).
    /// </summary>
    public Player Copy() => new()
    {
        Id = Id,
        Name = Name,
        TeamName = TeamName,
        RaceToTarget = RaceToTarget,
        BallGroup = BallGroup,
        _endCapIcon = _endCapIcon,
        EndCapIconVersion = EndCapIconVersion
    };
}
