using System.IO;
using System.Windows;
using System.Windows.Controls;
using PoolScoreboard.Controller.Persistence;
using PoolScoreboard.Core.Enums;
using PoolScoreboard.Core.Persistence;

namespace PoolScoreboard.Controller.Tests;

/// <summary>
/// Reads the records window the way an operator reads it: built on the shared WPF thread against the app's theme
/// (see <see cref="WpfTestThread"/>), shown off the visible desktop so its bindings resolve, and the table and the
/// head-to-head panel read back through the named controls. What the counts are is <c>MatchArchiveTests</c>' job;
/// what this checks is that the window shows them and follows the two picks.
/// </summary>
public sealed class RecordsWindowTests
{
    private static readonly DateTimeOffset At = new(2026, 11, 27, 21, 30, 0, TimeSpan.FromHours(1));

    [Fact]
    public void ShowsEveryNameThatPlayed_WithItsMatchesWonLostAndRate()
    {
        using var folder = new TempFolder();

        var archive = new MatchArchive();
        archive.Matches.Add(Record("Bob", "Alice", homeWon: true, At.AddMinutes(40)));
        archive.Matches.Add(Record("Alice", "Bob", homeWon: true, At.AddMinutes(20)));
        archive.Matches.Add(Record("Alice", "Carol", homeWon: false, At));
        Assert.True(folder.Store.TrySave(archive));

        var (rows, placeholderVisible) = WpfTestThread.Invoke(() =>
        {
            var window = CreateWindow(folder.Store);

            try
            {
                window.Show();
                window.UpdateLayout();

                var list = (ItemsControl)window.FindName("RecordsList");
                var placeholder = (TextBlock)window.FindName("NoRecordsText");

                return (list.Items.Cast<PlayerRecordRow>().ToArray(), placeholder.Visibility == Visibility.Visible);
            }
            finally
            {
                window.Close();
            }
        });

        // Most wins first, then most matches — Alice played three and won one, and leads a three-way tie on wins.
        Assert.Equal(new[] { "Alice", "Bob", "Carol" }, rows.Select(row => row.Name));
        Assert.Equal((3, 1, 2), (rows[0].Played, rows[0].Won, rows[0].Lost));
        Assert.Equal("33%", rows[0].Rate);
        Assert.Equal("50%", rows[1].Rate);
        Assert.Equal("100%", rows[2].Rate);
        Assert.False(placeholderVisible);
    }

    [Fact]
    public void TheHeadToHeadPanelFollowsTheTwoPickedNames()
    {
        using var folder = new TempFolder();

        var archive = new MatchArchive();
        archive.Matches.Add(Record("Bob", "Alice", homeWon: true, At.AddMinutes(40)));
        archive.Matches.Add(Record("Alice", "Bob", homeWon: true, At.AddMinutes(20)));
        archive.Matches.Add(Record("Alice", "Carol", homeWon: true, At.AddMinutes(10)));
        Assert.True(folder.Store.TrySave(archive));

        var (openingTitle, openingDetail, pickedTitle, pickedDetail, sameNameTitle) = WpfTestThread.Invoke(() =>
        {
            var window = CreateWindow(folder.Store);

            try
            {
                window.Show();
                window.UpdateLayout();

                var title = (TextBlock)window.FindName("HeadToHeadTitleText");
                var detail = (TextBlock)window.FindName("HeadToHeadDetailText");

                // The two who have won the most are picked when it opens, so the panel says something at once.
                var opening = (title.Text, detail.Text);

                window.SecondPlayer = "Carol";
                window.UpdateLayout();
                var againstCarol = (title.Text, detail.Text);

                window.SecondPlayer = "Alice";
                window.UpdateLayout();

                return (opening.Item1, opening.Item2, againstCarol.Item1, againstCarol.Item2, title.Text);
            }
            finally
            {
                window.Close();
            }
        });

        // Alice won two of the three they played; the newest of those was Bob's win. The date is formatted in the
        // machine's own time zone, so the expectation is built the same way rather than from a fixed string.
        var lastAgainstBob = At.AddMinutes(40).ToLocalTime().ToString("yyyy-MM-dd HH:mm");
        var lastAgainstCarol = At.AddMinutes(10).ToLocalTime().ToString("yyyy-MM-dd HH:mm");

        Assert.Equal("Alice 1 – 1 Bob", openingTitle);
        Assert.Equal($"2 matches · last played {lastAgainstBob}, Bob 2 – 1 Alice", openingDetail);

        Assert.Equal("Alice 1 – 0 Carol", pickedTitle);
        Assert.Equal($"1 match · last played {lastAgainstCarol}, Alice 2 – 1 Carol", pickedDetail);

        // The same name twice is not a pairing: the archive refuses it, and the panel says so rather than
        // counting a player against themselves.
        Assert.Equal("Pick two different players.", sameNameTitle);
    }

    [Fact]
    public void AnEmptyHistorySaysSo_WithoutAskingForPicksItCannotOffer()
    {
        using var folder = new TempFolder();

        var (rowCount, placeholderVisible, placeholderText, titleText, detailText) = WpfTestThread.Invoke(() =>
        {
            var window = CreateWindow(folder.Store);

            try
            {
                window.Show();
                window.UpdateLayout();

                var placeholder = (TextBlock)window.FindName("NoRecordsText");
                var title = (TextBlock)window.FindName("HeadToHeadTitleText");

                return (
                    ((ItemsControl)window.FindName("RecordsList")).Items.Count,
                    placeholder.Visibility == Visibility.Visible,
                    placeholder.Text,
                    title.Text,
                    ((TextBlock)window.FindName("HeadToHeadDetailText")).Text);
            }
            finally
            {
                window.Close();
            }
        });

        Assert.Equal(0, rowCount);
        Assert.True(placeholderVisible);
        Assert.Contains("No matches recorded yet", placeholderText);

        // Telling an operator to pick two names from an empty list is advice they cannot follow.
        Assert.Equal("No two players to compare yet.", titleText);
        Assert.Equal("Nothing recorded yet.", detailText);
    }

    [Fact]
    public void OneNameOnItsOwn_IsStillNotAPairingToPick()
    {
        using var folder = new TempFolder();

        // A side the operator never named occupies the record but is not a player, so this history holds one name.
        var archive = new MatchArchive();
        archive.Matches.Add(Record("Alice", "   ", homeWon: true, At));
        Assert.True(folder.Store.TrySave(archive));

        var (rowCount, titleText, detailText) = WpfTestThread.Invoke(() =>
        {
            var window = CreateWindow(folder.Store);

            try
            {
                window.Show();
                window.UpdateLayout();

                return (
                    ((ItemsControl)window.FindName("RecordsList")).Items.Count,
                    ((TextBlock)window.FindName("HeadToHeadTitleText")).Text,
                    ((TextBlock)window.FindName("HeadToHeadDetailText")).Text);
            }
            finally
            {
                window.Close();
            }
        });

        // One name is a row and nothing to pick against, so the panel says what is missing rather than asking.
        Assert.Equal(1, rowCount);
        Assert.Equal("No two players to compare yet.", titleText);
        Assert.Equal("One more player and there is something to compare.", detailText);
    }

    private static RecordsWindow CreateWindow(MatchHistoryStore store) =>
        new(store)
        {
            WindowStartupLocation = WindowStartupLocation.Manual,
            ShowActivated = false,
            ShowInTaskbar = false,
            Left = -20000,
            Top = -20000
        };

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

    private sealed class TempFolder : IDisposable
    {
        public TempFolder() =>
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), "poolscoreboard-records-window-tests", Guid.NewGuid().ToString("n"));

        public string Path { get; }

        public MatchHistoryStore Store => new(Path);

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Path))
                    Directory.Delete(Path, recursive: true);
            }
            catch (IOException)
            {
                // A leftover temp folder is not worth failing a passing test over.
            }
        }
    }
}
