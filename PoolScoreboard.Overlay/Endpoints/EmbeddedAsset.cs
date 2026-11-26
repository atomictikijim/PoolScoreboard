namespace PoolScoreboard.Overlay.Endpoints;

/// <summary>
/// Reads one of the overlay pages' assets, which are embedded in this assembly rather than copied to
/// disk so a single-file publish needs no extra content files. Lookup is by file name because the
/// manifest resource name carries the project's root namespace as a prefix.
/// </summary>
internal static class EmbeddedAsset
{
    public static string Read(string fileName)
    {
        var assembly = typeof(EmbeddedAsset).Assembly;
        var resourceName = assembly.GetManifestResourceNames()
            .Single(n => n.EndsWith(fileName, StringComparison.OrdinalIgnoreCase));

        using var stream = assembly.GetManifestResourceStream(resourceName)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
