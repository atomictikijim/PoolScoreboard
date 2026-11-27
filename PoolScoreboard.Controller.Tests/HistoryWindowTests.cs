using System.Windows;
using System.Windows.Controls;
using PoolScoreboard.Core.Enums;
using PoolScoreboard.Core.Persistence;

namespace PoolScoreboard.Controller.Tests;

/// <summary>
/// Reads the history window the way an operator reads it off the screen: built on the shared WPF thread against
/// the app's theme (see <see cref="WpfTestThread"/>), shown off the visible desktop so its bindings resolve, and
/// the list and win chips read back through the named controls. The order asserted is the order on screen — the
/// archive's newest-first storage is only right if it survives being displayed.
/// </summary>
public sealed class HistoryWindowTests
{
    private static readonly DateTimeOffset At = new(2026, 11, 27, 21, 30, 0, TimeSpan.FromHours(1));

    [Fact]
    public void ShowsTheMatchesNewestFirst_WithEachNamesWins()
    {
        // Newest first, the order the archive stores and the screen has to show.
        var archive = new MatchArchive();
        archive.Matches.Add(Record("Bob", "Alice", homeWon: true, At.AddMinutes(40)));
        archive.Matches.Add(Record("Alice", "Bob", homeWon: true, At.AddMinutes(20)));
        archive.Matches.Add(Record("Alice", "Bob", homeWon: false, At));

        var (rows, standings, placeholderVisible) = WpfTestThread.Invoke(() =>
        {
            var window = new HistoryWindow(archive)
            {
                WindowStartupLocation = WindowStartupLocation.Manual,
                ShowActivated = false,
                ShowInTaskbar = false,
                Left = -20000,
                Top = -20000
            };

            try
            {
                window.Show();
                window.UpdateLayout();

                var list = (ItemsControl)window.FindName("MatchesList");
                var chips = (ItemsControl)window.FindName("StandingsList");
                var placeholder = (TextBlock)window.FindName("NoMatchesText");

                return (
                    list.Items.Cast<HistoryMatch>().ToArray(),
                    chips.Items.Cast<MatchStanding>().ToArray(),
                    placeholder.Visibility == Visibility.Visible);
            }
            finally
            {
                window.Close();
            }
        });

        Assert.Equal(3, rows.Length);
        Assert.Equal("Bob", rows[0].Home);
        Assert.Equal("2 – 1", rows[0].Score);
        Assert.Equal("Bob", rows[0].Winner);
        Assert.Equal("Alice", rows[2].Home);
        Assert.Equal("1 – 2", rows[2].Score);
        Assert.Equal("Bob", rows[2].Winner);
        Assert.False(placeholderVisible);

        // Alice won the middle match, Bob the newest and the oldest.
        Assert.Equal(new[] { "Bob 2", "Alice 1" }, standings.Select(chip => $"{chip.Name} {chip.Wins}"));
    }

    [Fact]
    public void AnEmptyHistorySaysSoInsteadOfShowingNothing()
    {
        var (rowCount, placeholderText, placeholderVisible) = WpfTestThread.Invoke(() =>
        {
            var window = new HistoryWindow(new MatchArchive())
            {
                WindowStartupLocation = WindowStartupLocation.Manual,
                ShowActivated = false,
                ShowInTaskbar = false,
                Left = -20000,
                Top = -20000
            };

            try
            {
                window.Show();
                window.UpdateLayout();

                var list = (ItemsControl)window.FindName("MatchesList");
                var placeholder = (TextBlock)window.FindName("NoMatchesText");
                return (list.Items.Count, placeholder.Text, placeholder.Visibility == Visibility.Visible);
            }
            finally
            {
                window.Close();
            }
        });

        Assert.Equal(0, rowCount);
        Assert.True(placeholderVisible);
        Assert.Contains("No matches recorded yet", placeholderText, StringComparison.Ordinal);
    }

    private static MatchRecord Record(string home, string away, bool homeWon, DateTimeOffset when) => new()
    {
        MatchId = Guid.NewGuid().ToString("n"),
        CompletedAt = when,
        GameType = GameType.EightBall,
        RaceToMode = RaceToMode.Split,
        HomeName = home,
        HomeTeamName = "Team H",
        HomeRaceTo = 2,
        HomeScore = homeWon ? 2 : 1,
        AwayName = away,
        AwayTeamName = "Team A",
        AwayRaceTo = 2,
        AwayScore = homeWon ? 1 : 2,
        HomeWon = homeWon
    };
}
