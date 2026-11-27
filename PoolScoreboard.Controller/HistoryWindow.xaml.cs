using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using PoolScoreboard.Controller.Persistence;
using PoolScoreboard.Core.Persistence;

namespace PoolScoreboard.Controller;

/// <summary>
/// The recorded matches, newest first, with how many each name has won above them. Reading and deleting only: a
/// match is filed by the save button on the live view, so nothing here creates one — and the setup screen and live
/// view are untouched, which is why this is a window of its own rather than a third screen behind the same toggles.
/// <para>
/// It reads and writes through <see cref="MatchHistoryStore"/> rather than holding a copy of the archive, so
/// deleting a row shows the history as the file now holds it: the row goes, and the win chips are recounted from
/// what is left. The store is passed in, which is what lets a test point it at a throwaway folder.
/// </para>
/// </summary>
public partial class HistoryWindow : Window, INotifyPropertyChanged
{
    private readonly MatchHistoryStore _store;
    private readonly Func<HistoryMatch, bool> _confirmDelete;

    internal HistoryWindow(MatchHistoryStore store, Func<HistoryMatch, bool>? confirmDelete = null)
    {
        InitializeComponent();

        _store = store;
        _confirmDelete = confirmDelete ?? ConfirmWithDialog;

        DataContext = this;
        Reload();
    }

    /// <summary>Newest first, as the archive stores them.</summary>
    public ObservableCollection<HistoryMatch> Matches { get; } = new();

    /// <summary>Most wins first, as <see cref="MatchArchive.Standings"/> orders them.</summary>
    public ObservableCollection<MatchStanding> Standings { get; } = new();

    /// <summary>What the placeholder text binds to, so an empty history says so instead of looking broken.</summary>
    public bool HasNoMatches => Matches.Count == 0;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Removes one match once the operator confirms it. Internal rather than private so a test can delete through
    /// the same path the button takes, with a confirm callback that answers yes or no — a real dialog would block
    /// the test run.
    /// </summary>
    internal void DeleteMatch(HistoryMatch match)
    {
        if (!_confirmDelete(match))
            return;

        if (!_store.TryDelete(match.MatchId))
        {
            MessageBox.Show(
                this,
                "That match could not be removed from the history file. It is unchanged.",
                "Match history",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        Reload();
    }

    private void Reload()
    {
        var archive = _store.Load();

        Matches.Clear();
        foreach (var record in archive.Matches)
            Matches.Add(HistoryMatch.From(record));

        Standings.Clear();
        foreach (var standing in archive.Standings())
            Standings.Add(standing);

        OnPropertyChanged(nameof(HasNoMatches));
    }

    private bool ConfirmWithDialog(HistoryMatch match) =>
        MessageBox.Show(
            this,
            $"Delete {match.Summary} from the history? This cannot be undone.",
            "Delete match",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question) == MessageBoxResult.Yes;

    private void DeleteMatch_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: HistoryMatch match })
            DeleteMatch(match);
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

/// <summary>One row of the match list, formatted the way it reads on screen rather than the way it is stored.</summary>
public sealed class HistoryMatch
{
    private HistoryMatch(string matchId, string when, string home, string score, string away, string winner)
    {
        MatchId = matchId;
        When = when;
        Home = home;
        Score = score;
        Away = away;
        Winner = winner;
        Summary = $"{home} {score} {away} ({when})";
    }

    /// <summary>Which record this row is, so the delete button can name it to the store.</summary>
    public string MatchId { get; }

    /// <summary>Local time: a record is stored with its offset, which is not what an operator reads off a list.</summary>
    public string When { get; }

    public string Home { get; }

    public string Score { get; }

    public string Away { get; }

    public string Winner { get; }

    /// <summary>The row as one sentence, for the delete confirmation.</summary>
    public string Summary { get; }

    public static HistoryMatch From(MatchRecord record)
    {
        var home = Display(record.HomeName);
        var away = Display(record.AwayName);

        return new HistoryMatch(
            record.MatchId,
            record.CompletedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm"),
            home,
            $"{record.HomeScore} – {record.AwayScore}",
            away,
            record.HomeWon ? home : away);
    }

    /// <summary>A side the operator never named still occupies a row, so it gets a dash rather than a blank cell.</summary>
    private static string Display(string? name) =>
        string.IsNullOrWhiteSpace(name) ? "—" : name.Trim();
}
