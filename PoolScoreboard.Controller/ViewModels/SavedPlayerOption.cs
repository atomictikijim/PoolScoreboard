namespace PoolScoreboard.Controller.ViewModels;

/// <summary>
/// One saved player as the setup screen's picker shows them. Deliberately holds no artwork: the icon
/// data URI is loaded from <c>PlayerRosterStore</c> only when the operator picks the player.
/// </summary>
public sealed record SavedPlayerOption(string Id, string Name, string TeamName, int RaceToTarget)
{
    public string DisplayName =>
        string.IsNullOrWhiteSpace(TeamName) ? Name : $"{Name} — {TeamName}";

    /// <summary>ComboBox without a display member still shows something usable.</summary>
    public override string ToString() => DisplayName;
}
