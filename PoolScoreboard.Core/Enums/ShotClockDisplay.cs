namespace PoolScoreboard.Core.Enums;

/// <summary>
/// How the overlay draws a running shot clock: the countdown as numbers, or as a line that drains and turns red as
/// the shot runs out. A display choice rather than a rule — it says nothing about how long a shot gets — so it is
/// the operator's, it can be flipped mid-match, and it is remembered between runs with the rest of the look.
/// </summary>
public enum ShotClockDisplay
{
    Time,
    Line
}
