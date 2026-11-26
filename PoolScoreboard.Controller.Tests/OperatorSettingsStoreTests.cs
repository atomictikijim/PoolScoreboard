using System.IO;
using System.Text;
using System.Text.Json;
using PoolScoreboard.Controller.Diagnostics;
using PoolScoreboard.Controller.Persistence;
using PoolScoreboard.Core;
using PoolScoreboard.Core.Enums;
using PoolScoreboard.Core.Models;
using PoolScoreboard.Core.Persistence;

namespace PoolScoreboard.Controller.Tests;

/// <summary>
/// Exercises <see cref="OperatorSettingsStore"/> against a throwaway folder: real files, no WPF. The
/// store takes its root folder as a constructor argument, so nothing here touches the operator's own
/// <c>%AppData%</c> settings.
/// </summary>
public sealed class OperatorSettingsStoreTests : IDisposable
{
    private static readonly byte[] PngBytes = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x11, 0x22 };

    private static readonly byte[] SvgBytes = Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\"></svg>");

    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "poolscoreboard-store-tests", Guid.NewGuid().ToString("n"));

    private string SettingsPath => Path.Combine(_root, "settings.json");

    private string IconsFolder => Path.Combine(_root, "icons");

    private OperatorSettingsStore Store => new(_root);

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

    private static OperatorProfile Profile() => new()
    {
        ColorTheme = new ColorTheme
        {
            HomeBackground = "#123456",
            AwayBackground = "#654321",
            HomeAccent = "#abcdef",
            AwayAccent = "#fedcba",
            HomeText = "#ffffff",
            AwayText = "#111111"
        },
        ScoreboardStyle = new ScoreboardStyle
        {
            CornerRoundness = 30,
            OverallScale = 130,
            GlossyFinish = false,
            EndCapStyle = EndCapStyle.Hidden,
            ShooterIndicatorStyle = ShooterIndicatorStyle.Both
        },
        Visibility = new ScoreboardVisibility
        {
            ScoreBarVisible = false,
            BallTrackerVisible = true,
            WinnerBannerVisible = false
        },
        HomePlayer = new PlayerProfile { Name = "Alice", TeamName = "Blue Cuers", RaceToTarget = 7 },
        AwayPlayer = new PlayerProfile { Name = "Bob", TeamName = "Red Rackers", RaceToTarget = 5 }
    };

    private static string DataUri(string mime, byte[] bytes) => EndCapIconData.ToDataUri(mime, bytes);

    private JsonElement Json() => JsonDocument.Parse(File.ReadAllText(SettingsPath)).RootElement;

    private string[] IconFiles(string side) =>
        Directory.Exists(IconsFolder)
            ? Directory.GetFiles(IconsFolder, side + ".*").Select(Path.GetFileName).OfType<string>().ToArray()
            : Array.Empty<string>();

    [Fact]
    public void SaveThenLoad_RoundTripsSettingsAndBothIcons()
    {
        var profile = Profile();
        var homeIcon = DataUri("image/png", PngBytes);
        var awayIcon = DataUri("image/svg+xml", SvgBytes);

        Store.Save(profile, homeIcon, awayIcon);

        var loaded = Store.Load();
        Assert.NotNull(loaded);

        var restored = loaded!.Profile;
        Assert.Equal("#123456", restored.ColorTheme.HomeBackground);
        Assert.Equal("#654321", restored.ColorTheme.AwayBackground);
        Assert.Equal("#abcdef", restored.ColorTheme.HomeAccent);
        Assert.Equal("#fedcba", restored.ColorTheme.AwayAccent);
        Assert.Equal("#ffffff", restored.ColorTheme.HomeText);
        Assert.Equal("#111111", restored.ColorTheme.AwayText);

        Assert.Equal(30, restored.ScoreboardStyle.CornerRoundness);
        Assert.Equal(130, restored.ScoreboardStyle.OverallScale);
        Assert.False(restored.ScoreboardStyle.GlossyFinish);
        Assert.Equal(EndCapStyle.Hidden, restored.ScoreboardStyle.EndCapStyle);
        Assert.Equal(ShooterIndicatorStyle.Both, restored.ScoreboardStyle.ShooterIndicatorStyle);

        Assert.False(restored.Visibility.ScoreBarVisible);
        Assert.True(restored.Visibility.BallTrackerVisible);
        Assert.False(restored.Visibility.WinnerBannerVisible);

        Assert.Equal("Alice", restored.HomePlayer.Name);
        Assert.Equal("Blue Cuers", restored.HomePlayer.TeamName);
        Assert.Equal(7, restored.HomePlayer.RaceToTarget);
        Assert.Equal("Bob", restored.AwayPlayer.Name);
        Assert.Equal("Red Rackers", restored.AwayPlayer.TeamName);
        Assert.Equal(5, restored.AwayPlayer.RaceToTarget);

        Assert.Equal(homeIcon, loaded.HomeIconDataUri);
        Assert.Equal(awayIcon, loaded.AwayIconDataUri);
    }

    [Fact]
    public void Save_WritesIconsAsFilesAndKeepsTheirBytesOutOfTheJson()
    {
        Store.Save(Profile(), DataUri("image/png", PngBytes), awayIconDataUri: null);

        Assert.Equal(PngBytes, File.ReadAllBytes(Path.Combine(IconsFolder, "home.png")));
        Assert.Empty(IconFiles("away"));

        var json = File.ReadAllText(SettingsPath);
        Assert.Contains("home.png", json);
        Assert.DoesNotContain("base64", json);
        Assert.DoesNotContain(Convert.ToBase64String(PngBytes), json);
    }

    [Fact]
    public void Save_SvgIcon_KeepsItsMimeAcrossTheRoundTrip()
    {
        var svgIcon = DataUri("image/svg+xml", SvgBytes);

        Store.Save(Profile(), svgIcon, awayIconDataUri: null);

        Assert.Equal(new[] { "home.svg" }, IconFiles("home"));
        Assert.Equal(svgIcon, Store.Load()!.HomeIconDataUri);
    }

    [Fact]
    public void Save_SecondTimeWithTheSameIcon_DoesNotRewriteTheFile()
    {
        Store.Save(Profile(), DataUri("image/png", PngBytes), awayIconDataUri: null);
        var path = Path.Combine(IconsFolder, "home.png");
        var firstWrite = File.GetLastWriteTimeUtc(path);

        Thread.Sleep(30);
        Store.Save(Profile(), DataUri("image/png", PngBytes), awayIconDataUri: null);

        Assert.Equal(firstWrite, File.GetLastWriteTimeUtc(path));
        Assert.Equal(PngBytes, File.ReadAllBytes(path));
    }

    [Fact]
    public void Save_WithANewIconMime_ReplacesTheOldFileInsteadOfLeavingBoth()
    {
        Store.Save(Profile(), DataUri("image/png", PngBytes), awayIconDataUri: null);
        Store.Save(Profile(), DataUri("image/svg+xml", SvgBytes), awayIconDataUri: null);

        Assert.Equal(new[] { "home.svg" }, IconFiles("home"));
        Assert.Equal(DataUri("image/svg+xml", SvgBytes), Store.Load()!.HomeIconDataUri);
    }

    [Fact]
    public void Save_ClearingAnIcon_RemovesTheFileAndRecordsNothing()
    {
        Store.Save(Profile(), DataUri("image/png", PngBytes), DataUri("image/png", PngBytes));

        Store.Save(Profile(), homeIconDataUri: null, awayIconDataUri: null);

        Assert.Empty(IconFiles("home"));
        Assert.Empty(IconFiles("away"));
        Assert.Equal(JsonValueKind.Null, Json().GetProperty("homeIcon").ValueKind);
        Assert.Null(Store.Load()!.HomeIconDataUri);
        Assert.Null(Store.Load()!.AwayIconDataUri);
    }

    [Fact]
    public void Save_UnusableIconData_IsTreatedAsNoIcon()
    {
        Store.Save(Profile(), DataUri("image/png", PngBytes), awayIconDataUri: null);

        Store.Save(Profile(), "data:image/png;base64,!!!not base64!!!", awayIconDataUri: null);

        Assert.Empty(IconFiles("home"));
        Assert.Null(Store.Load()!.HomeIconDataUri);
        Assert.Equal("Alice", Store.Load()!.Profile.HomePlayer.Name);
    }

    [Fact]
    public void Save_LeavesNoTemporaryFileBehind()
    {
        Store.Save(Profile(), DataUri("image/png", PngBytes), awayIconDataUri: null);

        Assert.False(File.Exists(SettingsPath + ".tmp"));
        Assert.True(File.Exists(SettingsPath));
    }

    [Fact]
    public void Load_WithNoSettingsFile_ReturnsNull()
    {
        Assert.Null(Store.Load());
    }

    [Fact]
    public void Load_WithUnreadableJson_ReturnsNullAndSaysWhyInTheLog()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(SettingsPath, "{ this is not json");

        var text = CaptureLog(() => Assert.Null(Store.Load()));

        Assert.Contains("Could not read the operator settings", text);
    }

    [Fact]
    public void TrySave_ReportsAndLogsAFailureInsteadOfThrowing_WhenTheFileCannotBeWritten()
    {
        // A directory where settings.json should be: opening the file fails the way a locked or
        // read-only path does.
        Directory.CreateDirectory(SettingsPath);

        var text = CaptureLog(() => Assert.False(Store.TrySave(Profile(), homeIconDataUri: null, awayIconDataUri: null)));

        Assert.Contains("Could not write the operator settings", text);
    }

    /// <summary>Runs the action with the log attached and returns what the file holds afterwards.</summary>
    private string CaptureLog(Action action)
    {
        var logPath = Path.Combine(_root, "controller.log");
        using (var log = new TraceLogFile(logPath))
        {
            log.Attach();
            action();
        }

        return File.ReadAllText(logPath);
    }

    [Fact]
    public void Load_WithNoProfileInTheFile_ReturnsNull()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(SettingsPath, "{\"version\":1}");

        Assert.Null(Store.Load());
    }

    [Fact]
    public void Load_MissingIconFile_StillRestoresTheRestOfTheProfile()
    {
        Store.Save(Profile(), DataUri("image/png", PngBytes), awayIconDataUri: null);
        File.Delete(Path.Combine(IconsFolder, "home.png"));

        var loaded = Store.Load();

        Assert.NotNull(loaded);
        Assert.Null(loaded!.HomeIconDataUri);
        Assert.Equal("Alice", loaded.Profile.HomePlayer.Name);
        Assert.Equal(7, loaded.Profile.HomePlayer.RaceToTarget);
    }

    [Fact]
    public void Load_IgnoresAnIconPathThatWouldEscapeTheIconsFolder()
    {
        Store.Save(Profile(), DataUri("image/png", PngBytes), awayIconDataUri: null);

        // A hand-edited file must not be able to make the app read artwork from somewhere else.
        var outside = Path.Combine(Path.GetDirectoryName(_root)!, "escape.png");
        File.WriteAllBytes(outside, PngBytes);
        try
        {
            File.WriteAllText(SettingsPath, File.ReadAllText(SettingsPath).Replace("home.png", "../escape.png"));

            var loaded = Store.Load();

            Assert.Null(loaded!.HomeIconDataUri);
            Assert.Equal("Alice", loaded.Profile.HomePlayer.Name);
        }
        finally
        {
            File.Delete(outside);
        }
    }

    [Fact]
    public void Load_IgnoresAnIconWhoseMimeIsNotAnImage()
    {
        Store.Save(Profile(), DataUri("image/png", PngBytes), awayIconDataUri: null);

        File.WriteAllText(
            SettingsPath,
            File.ReadAllText(SettingsPath).Replace("\"image/png\"", "\"text/html\""));

        Assert.Null(Store.Load()!.HomeIconDataUri);
    }

    [Fact]
    public void Save_OverwritesThePreviousProfile()
    {
        var first = Profile();
        Store.Save(first, DataUri("image/png", PngBytes), awayIconDataUri: null);

        var second = Profile();
        second.HomePlayer.Name = "Carol";
        second.ColorTheme.HomeBackground = "#000000";
        second.Visibility.WinnerBannerVisible = true;

        Store.Save(second, awayIconDataUri: null, homeIconDataUri: null);

        var loaded = Store.Load()!.Profile;
        Assert.Equal("Carol", loaded.HomePlayer.Name);
        Assert.Equal("#000000", loaded.ColorTheme.HomeBackground);
        Assert.True(loaded.Visibility.WinnerBannerVisible);
    }

    [Fact]
    public void Save_ThenApplyTo_WritesTheProfileIntoAGameManager()
    {
        Store.Save(Profile(), DataUri("image/png", PngBytes), awayIconDataUri: null);
        var loaded = Store.Load()!;

        var manager = new GameManager();
        loaded.Profile.ApplyTo(manager, loaded.HomeIconDataUri, loaded.AwayIconDataUri);

        var state = manager.GetStateSnapshot();
        Assert.Equal("#123456", state.ColorTheme.HomeBackground);
        Assert.Equal(EndCapStyle.Hidden, state.ScoreboardStyle.EndCapStyle);
        Assert.False(state.Visibility.ScoreBarVisible);
        Assert.Equal("Alice", state.Player1.Name);
        Assert.Equal(7, state.Player1.RaceToTarget);
        Assert.Equal(DataUri("image/png", PngBytes), state.Player1.EndCapIcon);
        Assert.Equal("Bob", state.Player2.Name);
        Assert.Null(state.Player2.EndCapIcon);
    }
}
