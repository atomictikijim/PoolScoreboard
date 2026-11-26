namespace PoolScoreboard.Core.Persistence;

/// <summary>
/// Converts between the data URI an end-cap icon is held as in state and the bytes a store writes to
/// disk. Pure and I/O-free, so the Controller does not have to re-implement the parsing and the
/// failure cases (bad base64, a non-image type, a missing marker) can be pinned by tests.
/// </summary>
public static class EndCapIconData
{
    private const string DataPrefix = "data:";
    private const string Base64Marker = ";base64,";

    /// <summary>
    /// Splits a base64 <c>data:</c> URI into its image mime type and bytes. Returns false for
    /// anything that is not a base64 image URI — including the non-base64 form
    /// <c>data:image/svg+xml,&lt;svg…&gt;</c>, which this app never produces.
    /// </summary>
    public static bool TryParse(string? dataUri, out string mime, out byte[] bytes)
    {
        mime = string.Empty;
        bytes = Array.Empty<byte>();

        if (string.IsNullOrEmpty(dataUri) || !dataUri.StartsWith(DataPrefix, StringComparison.Ordinal))
            return false;

        var marker = dataUri.IndexOf(Base64Marker, StringComparison.Ordinal);
        if (marker <= DataPrefix.Length)
            return false;

        // Trimmed so a stray space can't yield a data URI whose mime the browser then rejects.
        var candidateMime = dataUri[DataPrefix.Length..marker].Trim();
        if (!IsSupportedMime(candidateMime))
            return false;

        var payload = dataUri[(marker + Base64Marker.Length)..];
        var buffer = new byte[(payload.Length * 3 / 4) + 3];
        if (!Convert.TryFromBase64String(payload, buffer, out var written))
            return false;

        mime = candidateMime;
        bytes = buffer[..written];
        return true;
    }

    /// <summary>
    /// End-cap artwork is always an image; anything else in the settings file is treated as absent
    /// rather than echoed back into state (the icon is served to the overlay over local HTTP).
    /// </summary>
    public static bool IsSupportedMime(string? mime) =>
        !string.IsNullOrWhiteSpace(mime)
        && mime.StartsWith("image/", StringComparison.OrdinalIgnoreCase);

    public static string ToDataUri(string mime, byte[] bytes) =>
        $"{DataPrefix}{mime}{Base64Marker}{Convert.ToBase64String(bytes)}";

    /// <summary>File extension for a saved icon; <c>.bin</c> keeps an unknown image type lossless.</summary>
    public static string ExtensionFor(string mime) => mime.ToLowerInvariant() switch
    {
        "image/png" => ".png",
        "image/jpeg" or "image/jpg" => ".jpg",
        "image/gif" => ".gif",
        "image/bmp" => ".bmp",
        "image/svg+xml" => ".svg",
        _ => ".bin"
    };
}
