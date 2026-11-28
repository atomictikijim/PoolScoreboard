using PoolScoreboard.Core.Enums;
using PoolScoreboard.Core.Models;
using PoolScoreboard.Core.Persistence;

namespace PoolScoreboard.Core.Tests;

/// <summary>
/// A preset is the look plus the four choices that otherwise get retyped at every venue. What capture has to get
/// right is completeness and copying: a preset missing a field hands back half a setup, and one holding references
/// into game state would change the saved venue every time the operator edited the colours afterwards.
/// </summary>
public sealed class SetupPresetTests
{
    private static GameState StateWithADistinctSettingEverywhere() => new()
    {
        GameType = GameType.EightBall,
        RaceToMode = RaceToMode.Split,
        ShotClockSeconds = 35,
        ShotClockExtensionSeconds = 25,
        ColorTheme = new ColorTheme
        {
            HomeBackground = "#111111",
            AwayBackground = "#222222",
            HomeAccent = "#333333",
            AwayAccent = "#444444",
            HomeText = "#555555",
            AwayText = "#666666"
        },
        ScoreboardStyle = new ScoreboardStyle
        {
            CornerRoundness = 42,
            OverallScale = 88,
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
        ShotClockDisplay = ShotClockDisplay.Line,
        StaleCueEnabled = true
    };

    [Fact]
    public void Capture_KeepsTheLookAndTheFourMatchChoices()
    {
        var preset = SetupPreset.Capture("  House A  ", StateWithADistinctSettingEverywhere(), overlayPort: 51241);

        Assert.Equal("House A", preset.Name);
        Assert.Equal(GameType.EightBall, preset.GameType);
        Assert.Equal(RaceToMode.Split, preset.RaceToMode);
        Assert.Equal(35, preset.ShotClockSeconds);
        Assert.Equal(25, preset.ShotClockExtensionSeconds);
        Assert.Equal(51241, preset.OverlayPort);

        Assert.Equal("#111111", preset.ColorTheme.HomeBackground);
        Assert.Equal("#666666", preset.ColorTheme.AwayText);
        Assert.Equal(42, preset.ScoreboardStyle.CornerRoundness);
        Assert.Equal(88, preset.ScoreboardStyle.OverallScale);
        Assert.False(preset.ScoreboardStyle.GlossyFinish);
        Assert.Equal(EndCapStyle.Hidden, preset.ScoreboardStyle.EndCapStyle);
        Assert.Equal(ShooterIndicatorStyle.Both, preset.ScoreboardStyle.ShooterIndicatorStyle);
        Assert.False(preset.Visibility.ScoreBarVisible);
        Assert.True(preset.Visibility.BallTrackerVisible);
        Assert.False(preset.Visibility.WinnerBannerVisible);
        Assert.Equal(ShotClockDisplay.Line, preset.ShotClockDisplay);
        Assert.True(preset.StaleCueEnabled);
    }

    [Fact]
    public void Capture_CopiesTheLook_SoEditingTheColoursAfterwardsLeavesTheSavedPresetAlone()
    {
        var state = StateWithADistinctSettingEverywhere();

        var preset = SetupPreset.Capture("House A", state, overlayPort: 51234);

        // The same edit an operator would make on the next screen: the preset is what was saved, not a window onto
        // whatever the state says now.
        state.ColorTheme.HomeBackground = "#ABCDEF";
        state.ScoreboardStyle.CornerRoundness = 7;
        state.Visibility.ScoreBarVisible = false;

        Assert.Equal("#111111", preset.ColorTheme.HomeBackground);
        Assert.Equal(42, preset.ScoreboardStyle.CornerRoundness);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(80)]
    [InlineData(1023)]
    [InlineData(65536)]
    public void Capture_TurnsAPortThatCannotBeBoundIntoTheDefault(int saved)
    {
        // A preset that cannot serve is worse than one serving on the usual port: this is captured from whatever the
        // port field says, and a hand-edited or pre-range value must not travel into a preset.
        var preset = SetupPreset.Capture("House A", StateWithADistinctSettingEverywhere(), saved);

        Assert.Equal(OperatorProfile.DefaultOverlayPort, preset.OverlayPort);
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData("   ", "")]
    [InlineData("  House A ", "House A")]
    public void NormalizeName_Trims_AndABlankNameStaysEmpty(string? typed, string expected)
    {
        Assert.Equal(expected, SetupPreset.NormalizeName(typed));
    }
}
