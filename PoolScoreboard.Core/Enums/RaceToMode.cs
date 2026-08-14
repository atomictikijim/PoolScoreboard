namespace PoolScoreboard.Core.Enums;

/// <summary>
/// Determines how Race-To targets are set: a single shared target, or separate targets per player.
/// </summary>
public enum RaceToMode
{
    /// <summary>Both players race to the same target score.</summary>
    Single,

    /// <summary>Each player has their own target score (typically different based on skill/preference).</summary>
    Split
}
