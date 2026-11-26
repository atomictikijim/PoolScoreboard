namespace PoolScoreboard.Core.Persistence;

/// <summary>
/// A player the operator has used before, as the roster remembers them. <see cref="Id"/> is the stable
/// key the Controller names their end-cap artwork after, so renaming a player keeps their icon.
/// </summary>
public class RosterEntry
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string TeamName { get; set; } = string.Empty;

    public int RaceToTarget { get; set; } = 5;
}

/// <summary>
/// The operator's saved player list, most recently used first. Pure: which entry a name refers to and
/// how the list is ordered live here, so both are unit-tested; the Controller owns the file and the
/// end-cap artwork behind <see cref="RosterEntry.Id"/>.
/// </summary>
public class PlayerRoster
{
    /// <summary>Enough for a season's worth of names without letting a stray file grow without bound.</summary>
    public const int MaxEntries = 100;

    public List<RosterEntry> Players { get; set; } = new();

    /// <summary>
    /// A returning player is the same person when the name matches, ignoring case and surrounding
    /// space. Team is deliberately not part of the identity: a player can change team between matches
    /// and should keep one roster entry (and one icon).
    /// </summary>
    public RosterEntry? FindByName(string? name)
    {
        var trimmed = name?.Trim();
        return string.IsNullOrEmpty(trimmed)
            ? null
            : Players.FirstOrDefault(player => string.Equals(player.Name, trimmed, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Adds the player, or updates the matching one and moves them to the front. Returns null for a
    /// blank name — that is how the setup screen represents a side that hasn't been filled in, and it
    /// must not create roster entries.
    /// </summary>
    public RosterEntry? Remember(string? name, string? teamName, int raceToTarget, string newId)
    {
        var trimmedName = name?.Trim();
        if (string.IsNullOrEmpty(trimmedName) || string.IsNullOrWhiteSpace(newId))
            return null;

        var entry = FindByName(trimmedName) ?? new RosterEntry { Id = newId };

        // Every match goes, not just the first: a roster file that already holds the same name twice
        // (hand-edited, or written before this rule existed) must end up with exactly one entry.
        Players.RemoveAll(player => string.Equals(player.Name, trimmedName, StringComparison.OrdinalIgnoreCase));

        entry.Name = trimmedName;
        entry.TeamName = teamName?.Trim() ?? string.Empty;
        entry.RaceToTarget = raceToTarget;
        Players.Insert(0, entry);

        while (Players.Count > MaxEntries)
            Players.RemoveAt(Players.Count - 1);

        return entry;
    }

}
