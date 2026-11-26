using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using PoolScoreboard.Core.Persistence;

namespace PoolScoreboard.Controller.Persistence;

/// <summary>
/// Reads and writes the operator profile under <c>%AppData%\PoolScoreboard</c>.
/// End-cap icons are kept as files next to the JSON rather than inline: an icon is up to 1.5 MB of
/// base64, and the settings file is rewritten whenever the operator tweaks a colour or a name.
/// </summary>
internal sealed class OperatorSettingsStore
{
    private const string IconsFolderName = "icons";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _iconsFolder;

    public OperatorSettingsStore(string? rootFolder = null)
    {
        RootFolder = rootFolder ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PoolScoreboard");
        _iconsFolder = Path.Combine(RootFolder, IconsFolderName);
        SettingsPath = Path.Combine(RootFolder, "settings.json");
    }

    public string RootFolder { get; }

    public string SettingsPath { get; }

    public sealed record LoadedProfile(OperatorProfile Profile, string? HomeIconDataUri, string? AwayIconDataUri);

    /// <summary>Returns null when nothing has been saved yet, or when the file can't be used.</summary>
    public LoadedProfile? Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
                return null;

            var settings = JsonSerializer.Deserialize<PersistedSettings>(File.ReadAllText(SettingsPath), Options);
            if (settings?.Profile == null)
                return null;

            return new LoadedProfile(
                settings.Profile,
                ReadIcon(settings.HomeIcon),
                ReadIcon(settings.AwayIcon));
        }
        catch (Exception exception) when (exception is IOException
                                             or UnauthorizedAccessException
                                             or JsonException)
        {
            // Startup must not be blocked by an unreadable or hand-edited file; the app opens on
            // defaults and the next save overwrites it.
            return null;
        }
    }

    public void Save(OperatorProfile profile, string? homeIconDataUri, string? awayIconDataUri)
    {
        Directory.CreateDirectory(_iconsFolder);

        var settings = new PersistedSettings
        {
            Profile = profile,
            HomeIcon = WriteIcon("home", homeIconDataUri),
            AwayIcon = WriteIcon("away", awayIconDataUri)
        };

        // Write then move, so a crash mid-save leaves the previous settings intact rather than a
        // truncated file that would fail to load next time.
        var temporaryPath = SettingsPath + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(settings, Options));
        File.Move(temporaryPath, SettingsPath, overwrite: true);
    }

    /// <summary>
    /// Writes this side's icon and returns what the settings file should record. A null/unusable
    /// data URI means the operator cleared the icon, so the cached file goes too. The new bytes are
    /// written before the old file is pruned: an unreadable icon must never destroy a good one that
    /// was already saved.
    /// </summary>
    private PersistedIcon? WriteIcon(string side, string? dataUri)
    {
        if (!EndCapIconData.TryParse(dataUri, out var mime, out var bytes))
        {
            DeleteIcons(side);
            return null;
        }

        var fileName = side + EndCapIconData.ExtensionFor(mime);
        var path = Path.Combine(_iconsFolder, fileName);

        if (!HasSameBytes(path, bytes))
            File.WriteAllBytes(path, bytes);

        DeleteIcons(side, keep: fileName);
        return new PersistedIcon { File = fileName, Mime = mime };
    }

    private void DeleteIcons(string side, string? keep = null)
    {
        foreach (var existing in Directory.EnumerateFiles(_iconsFolder, side + ".*"))
        {
            if (string.Equals(Path.GetFileName(existing), keep, StringComparison.OrdinalIgnoreCase))
                continue;

            File.Delete(existing);
        }
    }

    private static bool HasSameBytes(string path, byte[] bytes)
    {
        try
        {
            return File.Exists(path) && File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Unreadable is treated as "different": rewriting it is the next best attempt.
            return false;
        }
    }

    private string? ReadIcon(PersistedIcon? icon)
    {
        // GetFileName keeps a hand-edited settings file from pointing outside the icons folder, and
        // the mime is re-validated because it is echoed straight into a data URI.
        if (icon == null || string.IsNullOrEmpty(icon.File) || !EndCapIconData.IsSupportedMime(icon.Mime))
            return null;

        try
        {
            var path = Path.Combine(_iconsFolder, Path.GetFileName(icon.File));
            if (!File.Exists(path))
                return null;

            return EndCapIconData.ToDataUri(icon.Mime, File.ReadAllBytes(path));
        }
        catch (IOException)
        {
            return null;
        }
    }

    private sealed class PersistedSettings
    {
        public int Version { get; set; } = 1;
        public OperatorProfile? Profile { get; set; }
        public PersistedIcon? HomeIcon { get; set; }
        public PersistedIcon? AwayIcon { get; set; }
    }

    private sealed class PersistedIcon
    {
        public string File { get; set; } = string.Empty;
        public string Mime { get; set; } = string.Empty;
    }
}
