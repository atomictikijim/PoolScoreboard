using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using PoolScoreboard.Controller.Persistence;
using PoolScoreboard.Core.Persistence;

namespace PoolScoreboard.Controller;

/// <summary>
/// What the archive knows about the people in it: a row per name that has played, and the record between any two
/// picked names. Read-only — a match is filed from the live view and deleted from the History window, so nothing
/// here changes the file; it reads through <see cref="MatchHistoryStore"/> so it shows the history as the file now
/// holds it, and both views come from <see cref="MatchArchive"/> rather than being counted again here.
/// <para>
/// A separate window rather than part of the History one on purpose: that window is a list you can delete from, and
/// this one answers questions about the list without offering to change it.
/// </para>
/// </summary>
public partial class RecordsWindow : Window, INotifyPropertyChanged
{
    private readonly MatchHistoryStore _store;
    private MatchArchive _archive = new();
    private string? _firstPlayer;
    private string? _secondPlayer;

    internal RecordsWindow(MatchHistoryStore store)
    {
        InitializeComponent();

        _store = store;
        DataContext = this;
        Reload();
    }

    /// <summary>One row per name that has played, most wins first, as <see cref="MatchArchive.PlayerRecords"/> orders them.</summary>
    public ObservableCollection<PlayerRecordRow> Rows { get; } = new();

    /// <summary>The two pickers' contents: every name that has played, alphabetical, in its latest spelling.</summary>
    public ObservableCollection<string> PlayerNames { get; } = new();

    /// <summary>What the placeholder text binds to, so a history with nothing in it says so instead of looking broken.</summary>
    public bool HasNoRecords => Rows.Count == 0;

    public string? FirstPlayer
    {
        get => _firstPlayer;
        set
        {
            if (_firstPlayer == value)
                return;

            _firstPlayer = value;
            OnPropertyChanged();
            RefreshHeadToHead();
        }
    }

    public string? SecondPlayer
    {
        get => _secondPlayer;
        set
        {
            if (_secondPlayer == value)
                return;

            _secondPlayer = value;
            OnPropertyChanged();
            RefreshHeadToHead();
        }
    }

    /// <summary>The scoreline — "Alice 3 – 2 Bob" — or why there is not one yet.</summary>
    public string HeadToHeadTitle
    {
        get
        {
            var headToHead = CurrentHeadToHead;
            if (headToHead is null)
                return "Pick two different players.";

            return headToHead.Matches == 0
                ? $"{headToHead.FirstName} and {headToHead.SecondName} have not played."
                : $"{headToHead.FirstName} {headToHead.FirstWins} – {headToHead.SecondWins} {headToHead.SecondName}";
        }
    }

    /// <summary>How many matches that scoreline covers and how the last of them went.</summary>
    public string HeadToHeadDetail
    {
        get
        {
            var headToHead = CurrentHeadToHead;
            if (headToHead is null)
                return "Two names, and how they have fared against each other.";

            if (headToHead.LastMeeting is not { } last)
                return "No recorded match between them.";

            return $"{Matches(headToHead.Matches)} · last played {last.CompletedAt.ToLocalTime():yyyy-MM-dd HH:mm}, " +
                $"{Display(last.HomeName)} {last.HomeScore} – {last.AwayScore} {Display(last.AwayName)}";
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private MatchHeadToHead? CurrentHeadToHead
    {
        get
        {
            var headToHead = _archive.HeadToHead(FirstPlayer, SecondPlayer);
            if (headToHead is null)
                return null;

            // The pickers hold the names the archive spells them with, so the record's own strings are only used
            // when a caller set a name by hand.
            return headToHead;
        }
    }

    private void Reload()
    {
        _archive = _store.Load();

        var records = _archive.PlayerRecords();

        Rows.Clear();
        foreach (var record in records)
            Rows.Add(PlayerRecordRow.From(record));

        PlayerNames.Clear();
        foreach (var name in _archive.Players())
            PlayerNames.Add(name);

        OnPropertyChanged(nameof(HasNoRecords));

        // The two who have won the most, so the window has something in it the moment it opens rather than asking
        // for two picks before it says anything.
        FirstPlayer = records.Count > 0 ? records[0].Name : null;
        SecondPlayer = records.Count > 1 ? records[1].Name : null;
        RefreshHeadToHead();
    }

    private void RefreshHeadToHead()
    {
        OnPropertyChanged(nameof(HeadToHeadTitle));
        OnPropertyChanged(nameof(HeadToHeadDetail));
    }

    private static string Matches(int count) => count == 1 ? "1 match" : $"{count} matches";

    /// <summary>A side the operator never named still appears in a record, so it reads as a dash rather than blank.</summary>
    private static string Display(string? name) =>
        string.IsNullOrWhiteSpace(name) ? "—" : name.Trim();

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

/// <summary>One row of the player table, formatted the way it reads on screen rather than the way it is counted.</summary>
public sealed class PlayerRecordRow
{
    private PlayerRecordRow(string name, int played, int won, int lost, string rate)
    {
        Name = name;
        Played = played;
        Won = won;
        Lost = lost;
        Rate = rate;
    }

    public string Name { get; }

    public int Played { get; }

    public int Won { get; }

    public int Lost { get; }

    /// <summary>Wins as a whole percent: a table reads "67%", not "0.6666666666666666".</summary>
    public string Rate { get; }

    public static PlayerRecordRow From(MatchPlayerRecord record) => new(
        record.Name,
        record.Played,
        record.Wins,
        record.Losses,
        $"{Math.Round(record.WinRate * 100, MidpointRounding.AwayFromZero):0}%");
}
