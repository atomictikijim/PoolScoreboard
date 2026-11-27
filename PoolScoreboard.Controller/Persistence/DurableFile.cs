using System.IO;
using System.Text;

namespace PoolScoreboard.Controller.Persistence;

/// <summary>
/// The one write every saved file here goes through — <c>settings.json</c>, <c>roster.json</c>, <c>match.json</c> and
/// the end-cap icons beside them: the bytes go to the disk under a temporary name and are only then moved onto the
/// real one. A crash mid-write leaves the previous file intact, and a power cut cannot leave the name pointing at
/// content that was still sitting in the OS cache.
/// </summary>
internal static class DurableFile
{
    /// <summary>UTF-8 without a byte-order mark, the same encoding <see cref="File.WriteAllText(string, string)"/> uses.</summary>
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    /// <summary>
    /// Writes <paramref name="contents"/> over <paramref name="path"/>, replacing it in one step once the bytes are
    /// on the disk. Throws whatever the file system throws — each store decides whether that becomes a logged
    /// warning and a failed save.
    /// </summary>
    public static void WriteAllText(string path, string contents) =>
        Write(path, Utf8NoBom.GetBytes(contents));

    /// <summary>
    /// Writes an icon's bytes over <paramref name="path"/> the same way, so a saved icon is either the previous
    /// image or the new one, never half of either.
    /// </summary>
    public static void WriteAllBytes(string path, byte[] bytes) =>
        Write(path, bytes);

    private static void Write(string path, byte[] bytes)
    {
        var temporaryPath = path + ".tmp";

        // Flush(true) is what makes this survive a power cut and not just a process kill: it pushes the bytes,
        // and the file's new length, through the OS cache to the disk before the move gives the name to them.
        using (var stream = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            stream.Write(bytes);
            stream.Flush(flushToDisk: true);
        }

        // The rename itself is metadata, which NTFS journals rather than something this can flush; the guarantee
        // is about the contents being on the disk under that name.
        File.Move(temporaryPath, path, overwrite: true);
    }
}
