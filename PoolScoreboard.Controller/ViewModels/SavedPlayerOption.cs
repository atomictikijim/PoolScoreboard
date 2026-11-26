namespace PoolScoreboard.Controller.ViewModels;

/// <summary>
/// One saved player as the setup screen's picker shows them. Deliberately holds no artwork: the icon
/// data URI is loaded from <c>PlayerRosterStore</c> only when the operator picks the player.
/// </summary>
public sealed record SavedPlayerOption(string Id, string Name, string TeamName, int RaceToTarget)
{
    public string DisplayName =>
        string.IsNullOrWhiteSpace(TeamName) ? Name : $"{Name} — {TeamName}";

    /// <summary>
    /// Load-bearing, not cosmetic: the ComboBox's item template (and UI Automation) read the item's
    /// text from here, so removing it makes the picker list records as
    /// "SavedPlayerOption { Id = …, … }" instead of the player's name.
    /// </summary>
    public override string ToString() => DisplayName;
}
