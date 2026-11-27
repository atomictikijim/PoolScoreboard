using System.IO;
using System.Text.Json;
using PoolScoreboard.Controller.Persistence;
using PoolScoreboard.Core.Enums;
using PoolScoreboard.Core.Persistence;

namespace PoolScoreboard.Controller.Tests;

/// <summary>
/// Exercises <see cref="MatchStateStore"/> against a throwaway folder: real files, no WPF, and nothing near the
/// operator's own <c>%AppData%</c>.
/// </summary>
public sealed class MatchStateStoreTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "poolscoreboard-match-tests", Guid.NewGuid().ToString("n"));

    private string MatchPath => Path.Combine(_root, "match.json");

    private string CorruptMatchPath => Path.Combine(_root, "match.corrupt.json");

    private MatchStateStore Store => new(_root);

    [Fact]
    public void Load_ReturnsNullWhenNothingWasSaved()
    {
        Assert.Null(Store.Load());
    }

    [Fact]
    public void SaveThenLoad_KeepsTheMatch()
    {
        var match = new ResumableMatch
        {
            GameType = GameType.EightBall,
            RaceToMode = RaceToMode.Split,
            HomeScore = 3,
            AwayScore = 2,
            HomeIsCurrentShooter = false,
            HomeBallGroup = BallGroup.Solids,
            AwayBallGroup = BallGroup.Stripes,
            PocketedBalls = new List<int> { 1, 9 }
        };

        Assert.True(Store.TrySave(match));

        var loaded = Store.Load();
        Assert.NotNull(loaded);
        Assert.Equal(GameType.EightBall, loaded.GameType);
        Assert.Equal(RaceToMode.Split, loaded.RaceToMode);
        Assert.Equal(3, loaded.HomeScore);
        Assert.Equal(2, loaded.AwayScore);
        Assert.False(loaded.HomeIsCurrentShooter);
        Assert.Equal(BallGroup.Solids, loaded.HomeBallGroup);
        Assert.Equal(BallGroup.Stripes, loaded.AwayBallGroup);
        Assert.Equal(new[] { 1, 9 }, loaded.PocketedBalls);

        // The write goes through a temporary file, so a crash mid-save cannot truncate the previous snapshot.
        Assert.False(File.Exists(MatchPath + ".tmp"));
    }

    [Fact]
    public void TrySave_LeavesTheWholeSnapshotOnDisk_SoAFreshReadLoadsIt()
    {
        var match = new ResumableMatch
        {
            GameType = GameType.EightBall,
            RaceToMode = RaceToMode.Split,
            HomeScore = 4,
            AwayScore = 3,
            PocketedBalls = Enumerable.Range(1, 15).ToList()
        };

        Assert.True(Store.TrySave(match));

        // Read the published file through a handle of its own, with no sharing: what a machine that lost power
        // has to find is a name pointing at every byte the serializer produced, not at an empty or short file.
        var expected = JsonSerializer.Serialize(match, PersistenceJson.Options);
        using (var stream = new FileStream(MatchPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.Equal(System.Text.Encoding.UTF8.GetByteCount(expected), (int)stream.Length);

            using var reader = new StreamReader(stream);
            var reloaded = JsonSerializer.Deserialize<ResumableMatch>(reader.ReadToEnd(), PersistenceJson.Options);
            Assert.NotNull(reloaded);
            Assert.Equal(match.HomeScore, reloaded.HomeScore);
            Assert.Equal(match.PocketedBalls, reloaded.PocketedBalls);
        }

        Assert.NotNull(Store.Load());
        Assert.False(File.Exists(MatchPath + ".tmp"));
    }

    [Fact]
    public void Delete_RemovesTheSavedMatch_AndIsSafeWhenThereIsNone()
    {
        Assert.True(Store.TrySave(new ResumableMatch { HomeScore = 1 }));

        Store.Delete();

        Assert.False(File.Exists(MatchPath));
        Assert.Null(Store.Load());

        Store.Delete();
    }

    [Fact]
    public void Load_KeepsAnUnreadableMatchAside()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(MatchPath, "{ not json");

        Assert.Null(Store.Load());

        Assert.False(File.Exists(MatchPath));
        Assert.Equal("{ not json", File.ReadAllText(CorruptMatchPath));
    }

    [Fact]
    public void Load_IgnoresAMatchWrittenByAnotherVersion_AndLeavesItInPlace()
    {
        Directory.CreateDirectory(_root);
        var future = new ResumableMatch { FileVersion = ResumableMatch.Version + 1, HomeScore = 5 };
        File.WriteAllText(MatchPath, JsonSerializer.Serialize(future, PersistenceJson.Options));

        Assert.Null(Store.Load());

        // Not moved aside: a newer build may be the one that can read it.
        Assert.True(File.Exists(MatchPath));
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
