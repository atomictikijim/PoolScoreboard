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

    private PersistedIcon? WriteIcon(string side, string? dataUri)
    {
        foreach (var existing in Directory.EnumerateFiles(_iconsFolder, side + ".*"))
            File.Delete(existing);

        if (string.IsNullOrEmpty(dataUri) || !dataUri.StartsWith("data:", StringComparison.Ordinal))
            return null;

        var separator = dataUri.IndexOf(";base64,", StringComparison.Ordinal);
        if (separator < 0)
            return null;

        var mime = dataUri["data:".Length..separator];
        var base64 = dataUri[(separator + ";base64,".Length)..];
        var fileName = side + ExtensionFor(mime);

        try
        {
            File.WriteAllBytes(Path.Combine(_iconsFolder, fileName), Convert.FromBase64String(base64));
        }
        catch (FormatException)
        {
            // Not valid base64, so there is nothing worth keeping; the match still has the icon in
            // memory, it just won't come back next run.
            return null;
        }

        return new PersistedIcon { File = fileName, Mime = mime };
    }

    private string? ReadIcon(PersistedIcon? icon)
    {
        if (icon == null || string.IsNullOrEmpty(icon.File) || string.IsNullOrEmpty(icon.Mime))
            return null;

        try
        {
            var path = Path.Combine(_iconsFolder, icon.File);
            if (!File.Exists(path))
                return null;

            return $"data:{icon.Mime};base64,{Convert.ToBase64String(File.ReadAllBytes(path))}";
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static string ExtensionFor(string mime) => mime.ToLowerInvariant() switch
    {
        "image/png" => ".png",
        "image/jpeg" => ".jpg",
        "image/gif" => ".gif",
        "image/bmp" => ".bmp",
        "image/svg+xml" => ".svg",
        _ => ".bin"
    };

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
