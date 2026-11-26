namespace PoolScoreboard.Overlay.Models;

/// <summary>
/// Decodes the operator-supplied end-cap icon data URI into raw bytes for serving. Icons arrive as
/// <c>data:&lt;mime&gt;;base64,&lt;payload&gt;</c> — either a raster file the operator picked or a
/// bundled flag SVG the Controller base64-encoded. Nothing here leaves the machine, so beyond
/// requiring a well-formed base64 payload there is no validation to do.
/// </summary>
internal static class EndCapIconDataUri
{
    private const string Prefix = "data:";
    private const string Base64Marker = ";base64";

    /// <summary>
    /// Splits a base64 data URI into its bytes and declared MIME type. Returns <c>false</c> for a
    /// missing/empty icon or anything that isn't a base64 data URI (the caller answers 404).
    /// </summary>
    public static bool TryDecode(string? dataUri, out byte[] bytes, out string contentType)
    {
        bytes = Array.Empty<byte>();
        contentType = "application/octet-stream";

        if (string.IsNullOrEmpty(dataUri) || !dataUri.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
            return false;

        var comma = dataUri.IndexOf(',');
        if (comma < 0)
            return false;

        var metadata = dataUri[Prefix.Length..comma];
        if (!metadata.EndsWith(Base64Marker, StringComparison.OrdinalIgnoreCase))
            return false;

        var mime = metadata[..^Base64Marker.Length];
        contentType = mime.Length > 0 ? mime : "application/octet-stream";

        try
        {
            bytes = Convert.FromBase64String(dataUri[(comma + 1)..]);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
