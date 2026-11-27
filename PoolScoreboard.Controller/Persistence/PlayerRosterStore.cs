using System.Diagnostics;
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

    /// <summary>The folder, beside <c>roster.corrupt.json</c>, that keeps a rejected roster's artwork.</summary>
    private const string CorruptIconsFolderName = "roster.corrupt.icons";

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
    /// Where a roster file that could not be read is kept, so the next save cannot destroy the saved players.
    /// </summary>
    public string CorruptRosterPath => Path.Combine(
        Path.GetDirectoryName(RosterPath) ?? string.Empty,
        Path.GetFileNameWithoutExtension(RosterPath) + ".corrupt" + Path.GetExtension(RosterPath));

    /// <summary>
    /// Where a rejected roster's icon files are kept, under their original names, so the artwork is
    /// recoverable next to the preserved JSON. Outside <c>icons\</c> on purpose: the next roster save prunes
    /// anything there that no saved player references.
    /// </summary>
    public string CorruptIconsFolder => Path.Combine(
        Path.GetDirectoryName(RosterPath) ?? string.Empty,
        CorruptIconsFolderName);

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
            // The app still runs, but every saved player is gone for this session and the next save makes
            // the empty list the new roster: say so, and keep the file rather than let that save be the end
            // of it.
            var preserved = PreserveUnreadableFile();
            var artwork = preserved && PreserveIconFiles();
            Trace.TraceWarning(
                $"Could not read the player roster: {exception.Message}" +
                (preserved ? $" The file was kept as {Path.GetFileName(CorruptRosterPath)} for recovery." : string.Empty) +
                (artwork ? $" Its artwork was moved to {CorruptIconsFolderName}." : string.Empty));
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

        // Flushed to the disk before the move (see DurableFile): a crash mid-save keeps the previous roster, and a
        // power cut cannot leave roster.json naming players that were never written.
        DurableFile.WriteAllText(RosterPath, JsonSerializer.Serialize(document, PersistenceJson.Options));
    }

    /// <summary>
    /// Saves, but never throws: the roster is rewritten from a click and again while the window is
    /// closing, and a locked or full disk must not take the app down there. Returns whether it wrote.
    /// </summary>
    public bool TrySave(PlayerRoster roster)
    {
        try
        {
            Save(roster);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Trace.TraceWarning($"Could not write the player roster: {exception.Message}");
            return false;
        }
    }

    /// <summary>
    /// Moves a roster that could not be read aside as <c>roster.corrupt.json</c>, replacing an older rejected
    /// copy, so the saved players survive the next save. A file that cannot be moved — still held by whatever
    /// made the read fail — is left in place; the warning already says the read failed.
    /// </summary>
    private bool PreserveUnreadableFile()
    {
        try
        {
            if (!File.Exists(RosterPath))
                return false;

            File.Move(RosterPath, CorruptRosterPath, overwrite: true);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// Moves the rejected roster's artwork out of <c>icons\</c> into <see cref="CorruptIconsFolder"/>, keeping
    /// the file names the preserved JSON refers to. Without this the next save, running against the empty
    /// roster, prunes every <c>roster-*</c> file and the preserved entries point at artwork that is gone.
    /// Each file is moved independently: one that cannot be moved stays for the usual pruning instead of
    /// aborting the rest. Returns whether anything was moved.
    /// </summary>
    private bool PreserveIconFiles()
    {
        var moved = false;

        try
        {
            if (!Directory.Exists(_iconsFolder))
                return false;

            var files = Directory.GetFiles(_iconsFolder, IconFilePrefix + "*");
            if (files.Length == 0)
                return false;

            Directory.CreateDirectory(CorruptIconsFolder);

            foreach (var file in files)
            {
                try
                {
                    File.Move(file, Path.Combine(CorruptIconsFolder, Path.GetFileName(file)), overwrite: true);
                    moved = true;
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    // Left behind deliberately: still better than failing the load over one file.
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The icons folder itself could not be read; the roster file was still preserved.
        }

        return moved;
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
