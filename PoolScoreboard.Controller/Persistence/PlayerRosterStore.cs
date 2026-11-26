using System.IO;
using System.Text.Json;
using PoolScoreboard.Core.Persistence;

namespace PoolScoreboard.Controller.Persistence;

/// <summary>
/// Reads and writes the operator's saved player list: <c>roster.json</c> plus one icon file per player
/// under <c>icons/</c>. Icon bytes stay out of the JSON for the same reason the settings file keeps
/// them out — they are large, and the roster is rewritten whenever a player is remembered or dropped.
/// </summary>
public sealed class PlayerRosterStore
{
    private const string IconsFolderName = "icons";
    private const string IconFilePrefix = "roster-";

    private readonly string _iconsFolder;

    /// <summary>Icon metadata per entry id, the only icon state kept in memory; bytes are read on demand.</summary>
    private readonly Dictionary<string, PersistedIcon> _icons = new(StringComparer.Ordinal);

    public PlayerRosterStore(string? rootFolder = null)
    {
        var root = rootFolder ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PoolScoreboard");
        _iconsFolder = Path.Combine(root, IconsFolderName);
        RosterPath = Path.Combine(root, "roster.json");
    }

    public string RosterPath { get; }

    /// <summary>
    /// Returns the saved players, most recently used first. A missing or unusable file yields an empty
    /// roster rather than blocking startup, matching how the settings store behaves.
    /// </summary>
    public PlayerRoster Load()
    {
        _icons.Clear();

        try
        {
            if (!File.Exists(RosterPath))
                return new PlayerRoster();

            var document = JsonSerializer.Deserialize<PersistedRoster>(File.ReadAllText(RosterPath), PersistenceJson.Options);
            if (document?.Players == null)
                return new PlayerRoster();

            var roster = new PlayerRoster();
            foreach (var stored in document.Players)
            {
                if (string.IsNullOrWhiteSpace(stored.Id))
                    continue;

                var id = stored.Id;
                roster.Players.Add(new RosterEntry
                {
                    Id = id,
                    Name = stored.Name ?? string.Empty,
                    TeamName = stored.TeamName ?? string.Empty,
                    RaceToTarget = stored.RaceToTarget
                });

                if (stored.Icon != null
                    && EndCapIconData.IsSupportedMime(stored.Icon.Mime)
                    && !string.IsNullOrEmpty(stored.Icon.File))
                {
                    _icons[id] = stored.Icon;
                }
            }

            if (roster.Players.Count > PlayerRoster.MaxEntries)
                roster.Players.RemoveRange(PlayerRoster.MaxEntries, roster.Players.Count - PlayerRoster.MaxEntries);

            return roster;
        }
        catch (Exception exception) when (exception is IOException
                                             or UnauthorizedAccessException
                                             or JsonException)
        {
            _icons.Clear();
            return new PlayerRoster();
        }
    }

    public void Save(PlayerRoster roster)
    {
        Directory.CreateDirectory(_iconsFolder);
        var known = roster.Players.Select(player => player.Id).ToHashSet(StringComparer.Ordinal);

        // A player who has been forgotten loses their artwork, and so does a hand-copied leftover file.
        foreach (var id in _icons.Keys.Where(id => !known.Contains(id)).ToList())
        {
            DeleteIconFile(_icons[id]);
            _icons.Remove(id);
        }

        foreach (var file in Directory.EnumerateFiles(_iconsFolder, IconFilePrefix + "*"))
        {
            var id = Path.GetFileNameWithoutExtension(file)[IconFilePrefix.Length..];
            if (!known.Contains(id) || !_icons.ContainsKey(id))
                File.Delete(file);
        }

        var document = new PersistedRoster
        {
            Players = roster.Players.Select(player => new PersistedRosterEntry
            {
                Id = player.Id,
                Name = player.Name,
                TeamName = player.TeamName,
                RaceToTarget = player.RaceToTarget,
                Icon = _icons.GetValueOrDefault(player.Id)
            }).ToList()
        };

        var temporaryPath = RosterPath + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(document, PersistenceJson.Options));
        File.Move(temporaryPath, RosterPath, overwrite: true);
    }

    /// <summary>Decodes one saved icon on demand, so only the player actually being picked is loaded.</summary>
    public string? LoadIcon(string? entryId)
    {
        if (string.IsNullOrEmpty(entryId) || !_icons.TryGetValue(entryId, out var icon))
            return null;

        try
        {
            var path = Path.Combine(_iconsFolder, Path.GetFileName(icon.File));
            return File.Exists(path)
                ? EndCapIconData.ToDataUri(icon.Mime, File.ReadAllBytes(path))
                : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Stores (or clears) the artwork for one player; null/unusable data removes what was there.</summary>
    public void SetIcon(string entryId, string? dataUri)
    {
        if (string.IsNullOrWhiteSpace(entryId))
            return;

        var safeId = SafeId(entryId);
        if (safeId.Length == 0)
        {
            ForgetIcon(entryId);
            return;
        }

        Directory.CreateDirectory(_iconsFolder);

        if (!EndCapIconData.TryParse(dataUri, out var mime, out var bytes))
        {
            ForgetIcon(entryId);
            return;
        }

        var fileName = IconFilePrefix + safeId + EndCapIconData.ExtensionFor(mime);
        var path = Path.Combine(_iconsFolder, fileName);
        if (!HasSameBytes(path, bytes))
            File.WriteAllBytes(path, bytes);

        if (_icons.TryGetValue(entryId, out var previous)
            && !string.Equals(previous.File, fileName, StringComparison.OrdinalIgnoreCase))
        {
            DeleteIconFile(previous);
        }

        _icons[entryId] = new PersistedIcon { File = fileName, Mime = mime };
    }

    private void ForgetIcon(string entryId)
    {
        if (_icons.Remove(entryId, out var icon))
            DeleteIconFile(icon);
    }

    /// <summary>Entry ids are generated as hex, but a hand-edited file could hold anything; never trust it as a path.</summary>
    private static string SafeId(string entryId) =>
        new(entryId.Where(character => char.IsLetterOrDigit(character) || character is '-' or '_').ToArray());

    private void DeleteIconFile(PersistedIcon icon)
    {
        var path = Path.Combine(_iconsFolder, Path.GetFileName(icon.File));
        if (File.Exists(path))
            File.Delete(path);
    }

    private static bool HasSameBytes(string path, byte[] bytes)
    {
        try
        {
            return File.Exists(path) && File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private sealed class PersistedRoster
    {
        public List<PersistedRosterEntry>? Players { get; set; } = new();
    }

    private sealed class PersistedRosterEntry
    {
        public string? Id { get; set; }

        public string? Name { get; set; }

        public string? TeamName { get; set; }

        public int RaceToTarget { get; set; } = 5;

        public PersistedIcon? Icon { get; set; }
    }

    private sealed class PersistedIcon
    {
        public string File { get; set; } = string.Empty;

        public string Mime { get; set; } = string.Empty;
    }
}
