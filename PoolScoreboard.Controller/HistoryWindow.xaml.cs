using System.Windows;
using PoolScoreboard.Core.Persistence;

namespace PoolScoreboard.Controller;

/// <summary>
/// The recorded matches, newest first, with how many each name has won above them. Read-only by design: the
/// history is a record of what was played, so nothing here edits it — and nothing here touches the live view or
/// the setup screen, which is why it is a window of its own rather than a third screen behind the same toggles.
/// <para>
/// The archive is passed in rather than loaded here, so the window can be built from a test with no file behind
/// it and the reading side of the history has the same shape as the writing side.
/// </para>
/// </summary>
public partial class HistoryWindow : Window
{
    public HistoryWindow(MatchArchive archive)
    {
        InitializeComponent();

        Matches = archive.Matches.Select(HistoryMatch.From).ToArray();

        // Bound as Core computed them: the chip shows Name and Wins and adds nothing, so a second type here
        // would only be a copy of one that has to be kept in step with it.
        Standings = archive.Standings();

        DataContext = this;
    }

    /// <summary>Newest first, as the archive stores them.</summary>
    public IReadOnlyList<HistoryMatch> Matches { get; }

    /// <summary>Most wins first, as <see cref="MatchArchive.Standings"/> orders them.</summary>
    public IReadOnlyList<MatchStanding> Standings { get; }

    /// <summary>What the placeholder text binds to, so an empty history says so instead of looking broken.</summary>
    public bool HasNoMatches => Matches.Count == 0;

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}

/// <summary>One row of the match list, formatted the way it reads on screen rather than the way it is stored.</summary>
public sealed class HistoryMatch
{
    private HistoryMatch(string when, string home, string score, string away, string winner)
    {
        When = when;
        Home = home;
        Score = score;
        Away = away;
        Winner = winner;
    }

    /// <summary>Local time: a record is stored with its offset, which is not what an operator reads off a list.</summary>
    public string When { get; }

    public string Home { get; }

    public string Score { get; }

    public string Away { get; }

    public string Winner { get; }

    public static HistoryMatch From(MatchRecord record)
    {
        var home = Display(record.HomeName);
        var away = Display(record.AwayName);

        return new HistoryMatch(
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
