using System.IO;
using System.Text;
using PoolScoreboard.Controller.Persistence;

namespace PoolScoreboard.Controller.Tests;

/// <summary>
/// The shared write behind <c>settings.json</c>, <c>roster.json</c>, <c>match.json</c> and the end-cap icons: what
/// lands under the real name is exactly the text or bytes that were handed in, the temporary file is gone, and a
/// failure throws rather than leaving a store believing it saved. The flush itself cannot be observed from a test —
/// losing power is not something a test can stage — so these pin the file a completed write leaves behind.
/// </summary>
public sealed class DurableFileTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "poolscoreboard-durable-tests", Guid.NewGuid().ToString("n"));

    private string SavedPath => Path.Combine(_root, "saved.json");

    private string IconPath => Path.Combine(_root, "home.png");

    [Fact]
    public void WriteAllText_PutsTheExactTextOnDisk_WithoutABomOrATemporaryFile()
    {
        Directory.CreateDirectory(_root);
        const string contents = "{\"name\":\"Jöhn\",\"homeScore\":3}";

        DurableFile.WriteAllText(SavedPath, contents);

        using (var stream = new FileStream(SavedPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.Equal(Encoding.UTF8.GetByteCount(contents), (int)stream.Length);

            using var reader = new StreamReader(stream, Encoding.UTF8);
            Assert.Equal(contents, reader.ReadToEnd());
        }

        // No byte-order mark: a JSON file that starts with one is rejected by anything strict that reads it.
        Assert.Equal((byte)'{', File.ReadAllBytes(SavedPath)[0]);
        Assert.False(File.Exists(SavedPath + ".tmp"));
    }

    [Fact]
    public void WriteAllText_ReplacesThePreviousFile_WithoutLeavingItsTailBehind()
    {
        Directory.CreateDirectory(_root);
        DurableFile.WriteAllText(SavedPath, new string('a', 5000));

        DurableFile.WriteAllText(SavedPath, "{\"players\":[]}");

        Assert.Equal("{\"players\":[]}", File.ReadAllText(SavedPath));
    }

    [Fact]
    public void WriteAllBytes_PutsTheExactIconBytesOnDisk_AndLeavesNoTemporaryFile()
    {
        Directory.CreateDirectory(_root);
        var icon = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0xFF, 0x7F, 0x80 };

        DurableFile.WriteAllBytes(IconPath, icon);

        Assert.Equal(icon, File.ReadAllBytes(IconPath));
        Assert.False(File.Exists(IconPath + ".tmp"));
    }

    [Fact]
    public void WriteAllBytes_ReplacesALargerIcon_WithoutLeavingItsTailBehind()
    {
        Directory.CreateDirectory(_root);
        DurableFile.WriteAllBytes(IconPath, new byte[4096]);
        var smaller = new byte[] { 1, 2, 3 };

        DurableFile.WriteAllBytes(IconPath, smaller);

        Assert.Equal(smaller, File.ReadAllBytes(IconPath));
    }

    [Fact]
    public void WriteAllText_ThrowsWhenTheFilingCannotHappen_SoTheStoreReportsAFailedSave()
    {
        // The stores create their own folders first; this is the contract if one ever forgets - an IOException the
        // store's TrySave turns into a logged warning, not a silent success.
        Assert.ThrowsAny<IOException>(() =>
            DurableFile.WriteAllText(Path.Combine(_root, "missing-folder", "saved.json"), "{}"));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp folder is not worth failing a passing test over.
        }
    }
}
