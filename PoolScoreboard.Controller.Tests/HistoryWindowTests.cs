using System.IO;
using System.Windows;
using System.Windows.Controls;
using PoolScoreboard.Controller.Persistence;
using PoolScoreboard.Core.Enums;
using PoolScoreboard.Core.Persistence;

namespace PoolScoreboard.Controller.Tests;

/// <summary>
/// Reads the history window the way an operator reads it off the screen: built on the shared WPF thread against
/// the app's theme (see <see cref="WpfTestThread"/>), shown off the visible desktop so its bindings resolve, and
/// the list and win chips read back through the named controls. The order asserted is the order on screen — the
/// archive's newest-first storage is only right if it survives being displayed.
/// <para>
/// The window is given a store rooted in a throwaway folder rather than an archive, because what it now has to do
/// is delete: a test that hands it a list could not tell whether the removal reached the file, or whether the win
/// chips were recounted from what is left.
/// </para>
/// </summary>
public sealed class HistoryWindowTests
{
    private static readonly DateTimeOffset At = new(2026, 11, 27, 21, 30, 0, TimeSpan.FromHours(1));

    [Fact]
    public void ShowsTheMatchesNewestFirst_WithEachNamesWins()
    {
        using var folder = new TempFolder();

        // Newest first, the order the archive stores and the screen has to show.
        var archive = new MatchArchive();
        archive.Matches.Add(Record("Bob", "Alice", homeWon: true, At.AddMinutes(40)));
        archive.Matches.Add(Record("Alice", "Bob", homeWon: true, At.AddMinutes(20)));
        archive.Matches.Add(Record("Alice", "Bob", homeWon: false, At));
        Assert.True(folder.Store.TrySave(archive));

        var (rows, standings, placeholderVisible) = WpfTestThread.Invoke(() =>
        {
            var window = CreateWindow(folder.Store);

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
        using var folder = new TempFolder();

        var (rowCount, placeholderText, placeholderVisible) = WpfTestThread.Invoke(() =>
        {
            var window = CreateWindow(folder.Store);

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

    [Fact]
    public void DeletingARowRemovesItFromTheHistory_AndRecountsTheWins()
    {
        using var folder = new TempFolder();
        SeedFolder(folder.Store);

        var (rows, standings, placeholderVisible) = WpfTestThread.Invoke(() =>
        {
            // The confirmation is the one part a test cannot click, so it answers yes here; the real window asks.
            var window = CreateWindow(folder.Store, _ => true);

            try
            {
                window.Show();
                window.UpdateLayout();

                window.DeleteMatch(window.Matches[0]);
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

        // The older match stays, and it is the one on screen.
        var kept = Assert.Single(rows);
        Assert.Equal("Alice", kept.Home);
        Assert.Equal("Alice", kept.Winner);
        Assert.False(placeholderVisible);

        // Bob's only win was the match that was deleted, so his chip is gone rather than reading 1.
        Assert.Equal(new[] { "Alice 1" }, standings.Select(chip => $"{chip.Name} {chip.Wins}"));

        // And the removal is on disk, not just in the list: a fresh store sees one match.
        var onDisk = new MatchHistoryStore(folder.Path).Load();
        Assert.Single(onDisk.Matches);
        Assert.Equal(new[] { "Alice 1" }, onDisk.Standings().Select(standing => $"{standing.Name} {standing.Wins}"));
    }

    [Fact]
    public void DeletingTheLastMatch_LeavesThePlaceholder()
    {
        using var folder = new TempFolder();
        folder.Store.TrySave(new MatchArchive
        {
            Matches = { Record("Bob", "Alice", homeWon: true, At) }
        });

        var visible = WpfTestThread.Invoke(() =>
        {
            var window = CreateWindow(folder.Store, _ => true);

            try
            {
                window.Show();
                window.UpdateLayout();

                window.DeleteMatch(window.Matches[0]);
                window.UpdateLayout();

                return ((TextBlock)window.FindName("NoMatchesText")).Visibility == Visibility.Visible;
            }
            finally
            {
                window.Close();
            }
        });

        Assert.True(visible);
        Assert.Empty(folder.Store.Load().Matches);
    }

    [Fact]
    public void DecliningTheConfirmation_KeepsTheMatch()
    {
        using var folder = new TempFolder();
        SeedFolder(folder.Store);

        var (rowCount, onDisk) = WpfTestThread.Invoke(() =>
        {
            var window = CreateWindow(folder.Store, _ => false);

            try
            {
                window.Show();
                window.UpdateLayout();

                window.DeleteMatch(window.Matches[0]);
                window.UpdateLayout();

                return (((ItemsControl)window.FindName("MatchesList")).Items.Count, window.Matches.Count);
            }
            finally
            {
                window.Close();
            }
        });

        Assert.Equal(2, rowCount);
        Assert.Equal(2, onDisk);
        Assert.Equal(2, new MatchHistoryStore(folder.Path).Load().Matches.Count);
    }

    private static HistoryWindow CreateWindow(MatchHistoryStore store, Func<HistoryMatch, bool>? confirmDelete = null) =>
        new(store, confirmDelete)
        {
            WindowStartupLocation = WindowStartupLocation.Manual,
            ShowActivated = false,
            ShowInTaskbar = false,
            Left = -20000,
            Top = -20000
        };

    /// <summary>Two matches: Bob wins the newer one, Alice the older.</summary>
    private static void SeedFolder(MatchHistoryStore store)
    {
        var archive = new MatchArchive();
        archive.Matches.Add(Record("Bob", "Alice", homeWon: true, At.AddMinutes(20)));
        archive.Matches.Add(Record("Alice", "Bob", homeWon: true, At));
        Assert.True(store.TrySave(archive));
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

    private sealed class TempFolder : IDisposable
    {
        public TempFolder() =>
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), "poolscoreboard-history-window-tests", Guid.NewGuid().ToString("n"));

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
