using PoolScoreboard.Core.Models;

namespace PoolScoreboard.Core.Persistence;

/// <summary>
/// The recorded matches, newest first. Pure: which match becomes a record, how many are kept and who has won what
/// live here so all three are unit-tested; the Controller owns the file they are read from and written to.
/// </summary>
public class MatchArchive
{
    /// <summary>Bumped whenever the shape below changes, so an older build does not read a newer file.</summary>
    public const int Version = 1;

    /// <summary>
    /// Enough for a long run of play without letting a stray file grow without bound — the same reasoning as the
    /// roster's cap, with a higher number because several matches fit in one evening.
    /// </summary>
    public const int MaxRecords = 200;

    public int FileVersion { get; set; } = Version;

    public List<MatchRecord> Matches { get; set; } = new();

    /// <summary>
    /// The archive call: turns a finished match into a record, files it where the newest is read first, and drops
    /// the oldest once the list is full. Returns the record, or null when there is nothing to file — no finished
    /// match, or a match this archive already holds — because a caller that archives whenever the state changes
    /// must be able to tell "nothing to do" from "wrote one", or it would fill the file with blank or repeated
    /// entries.
    /// <para>
    /// Recording a match once is not the same as recording the moment it was won. The winner stays in state —
    /// that is what keeps the banner up — so every later change arrives with it: a ball toggled, a rack reset,
    /// a relaunch that restores the same match. The match's own id is what separates those from a rematch, whose
    /// names and score may be identical because it is the same two people playing the same race again.
    /// </para>
    /// </summary>
    public MatchRecord? Archive(GameState? state, DateTimeOffset completedAt)
    {
        var record = MatchRecord.Capture(state, completedAt);
        if (record is null)
            return null;

        // A record with no id can only come from a hand-made or pre-history state; it is filed rather than
        // dropped, because two unknown matches in one history are better than a silently missing win.
        if (!string.IsNullOrEmpty(record.MatchId) &&
            Matches.Any(match => string.Equals(match.MatchId, record.MatchId, StringComparison.Ordinal)))
        {
            return null;
        }

        Matches.Insert(0, record);
        if (Matches.Count > MaxRecords)
            Matches.RemoveRange(MaxRecords, Matches.Count - MaxRecords);

        return record;
    }

    /// <summary>Whether this archive holds the match — what tells a match already saved from one that is not.</summary>
    public bool Contains(string? matchId) =>
        !string.IsNullOrEmpty(matchId) &&
        Matches.Any(match => string.Equals(match.MatchId, matchId, StringComparison.Ordinal));

    /// <summary>
    /// Drops one recorded match, by id. Returns false when nothing has that id, so a caller can tell a stale
    /// row from a real removal — the same answer the roster's <c>Forget</c> gives for a name it does not hold.
    /// </summary>
    public bool Forget(string? matchId)
    {
        if (string.IsNullOrEmpty(matchId))
            return false;

        return Matches.RemoveAll(match => string.Equals(match.MatchId, matchId, StringComparison.Ordinal)) > 0;
    }

    /// <summary>
    /// Who won what across the whole archive, most wins first and names in order within a tie. A player is their
    /// name, compared the way the roster compares names — ignoring case and surrounding space — because that is
    /// the only identity a record carries: the roster's ids are not written into the history, so deleting a saved
    /// player does not take their wins with them. Only names that actually won something appear, and a blank name
    /// (a side the operator never filled in) counts for nobody. The spelling shown is the one from the most recent
    /// match, because the list is walked newest first — a corrected name is what the operator last typed.
    /// </summary>
    public IReadOnlyList<MatchStanding> Standings()
    {
        var wins = new Dictionary<string, MatchStanding>(StringComparer.OrdinalIgnoreCase);

        foreach (var match in Matches)
        {
            var name = (match.HomeWon ? match.HomeName : match.AwayName)?.Trim();
            if (string.IsNullOrEmpty(name))
                continue;

            if (wins.TryGetValue(name, out var standing))
                standing.Wins++;
            else
                wins[name] = new MatchStanding { Name = name, Wins = 1 };
        }

        return wins.Values
            .OrderByDescending(standing => standing.Wins)
            .ThenBy(standing => standing.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Every name that has played a recorded match, with what it has played, won and lost — the same list
    /// <see cref="Standings"/> counts, read for a table rather than a chip: a name that has only ever lost appears
    /// here too, and so does the count of matches behind a win. Most wins first, then most matches, then name.
    /// <para>
    /// A name is its spelling ignoring case and surrounding space, the way the roster and the chips compare them,
    /// and the spelling shown is the one from the player's most recent match, because the list is walked newest
    /// first. A blank side (a name the operator never filled in) counts for nobody, and a record whose two sides
    /// hold the same name is one person who played one match — two rows for them would make every count wrong.
    /// </para>
    /// </summary>
    public IReadOnlyList<MatchPlayerRecord> PlayerRecords()
    {
        var records = new Dictionary<string, MatchPlayerRecord>(StringComparer.OrdinalIgnoreCase);

        void Count(string name, bool won, DateTimeOffset when)
        {
            if (!records.TryGetValue(name, out var record))
            {
                // First sighting, and the list is newest first: this is the name's latest spelling and the time
                // they last played.
                record = new MatchPlayerRecord { Name = name, LastPlayed = when };
                records[name] = record;
            }

            record.Played++;
            if (won)
                record.Wins++;
        }

        foreach (var match in Matches)
        {
            var home = NameOf(match.HomeName);
            var away = NameOf(match.AwayName);

            if (home.Length == 0 && away.Length == 0)
                continue;

            if (home.Length == 0)
            {
                Count(away, won: !match.HomeWon, match.CompletedAt);
                continue;
            }

            if (away.Length == 0)
            {
                Count(home, won: match.HomeWon, match.CompletedAt);
                continue;
            }

            if (string.Equals(home, away, StringComparison.OrdinalIgnoreCase))
            {
                Count(home, won: true, match.CompletedAt);
                continue;
            }

            Count(home, match.HomeWon, match.CompletedAt);
            Count(away, !match.HomeWon, match.CompletedAt);
        }

        return records.Values
            .OrderByDescending(record => record.Wins)
            .ThenByDescending(record => record.Played)
            .ThenBy(record => record.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// The names the archive holds, alphabetical and in their latest spelling, for a picker — every player who has
    /// played, winners or not.
    /// </summary>
    public IReadOnlyList<string> Players() =>
        PlayerRecords()
            .Select(record => record.Name)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>
    /// How two names have fared against each other, or null when the question does not mean anything: either name
    /// is blank, or the two are the same person — nobody plays themselves, so a count of one name against itself
    /// would be a second, contradicting answer to the one <see cref="PlayerRecords"/> already gives.
    /// <para>
    /// Only matches those two played each other count; a match one of them played against somebody else says
    /// nothing about this pairing. Both sides are compared, either way round, so which end of a record a name was
    /// on does not decide whose win it is.
    /// </para>
    /// </summary>
    public MatchHeadToHead? HeadToHead(string? first, string? second)
    {
        var one = NameOf(first);
        var other = NameOf(second);

        if (one.Length == 0 || other.Length == 0 || string.Equals(one, other, StringComparison.OrdinalIgnoreCase))
            return null;

        var headToHead = new MatchHeadToHead { FirstName = one, SecondName = other };

        foreach (var match in Matches)
        {
            var home = NameOf(match.HomeName);
            var away = NameOf(match.AwayName);

            var firstPlayedHome = string.Equals(home, one, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(away, other, StringComparison.OrdinalIgnoreCase);
            var firstPlayedAway = string.Equals(home, other, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(away, one, StringComparison.OrdinalIgnoreCase);

            if (!firstPlayedHome && !firstPlayedAway)
                continue;

            headToHead.Matches++;

            // Newest first, so the first match counted is the last time they met.
            headToHead.LastMeeting ??= match;

            if (firstPlayedHome ? match.HomeWon : !match.HomeWon)
                headToHead.FirstWins++;
            else
                headToHead.SecondWins++;
        }

        return headToHead;
    }

    private static string NameOf(string? name) => (name ?? string.Empty).Trim();
}

/// <summary>One name's record across the archive: how many of the recorded matches they won.</summary>
public class MatchStanding
{
    public string Name { get; set; } = string.Empty;

    public int Wins { get; set; }
}

/// <summary>
/// One name's record across the archive: the matches they played, how many they won, and when they last played.
/// Derived from the records rather than stored, so a hand-edited or older-version file cannot carry counts that
/// disagree with the matches beside them.
/// </summary>
public class MatchPlayerRecord
{
    public string Name { get; set; } = string.Empty;

    public int Played { get; set; }

    public int Wins { get; set; }

    public int Losses => Played - Wins;

    /// <summary>Wins as a fraction of the matches played; zero for a record with none, which is what its row reads as.</summary>
    public double WinRate => Played == 0 ? 0 : (double)Wins / Played;

    /// <summary>When they last played — the newest recorded match they appear in.</summary>
    public DateTimeOffset LastPlayed { get; set; }
}

/// <summary>
/// How two names have fared against each other: how many matches they played, how many each won, and the last one.
/// </summary>
public class MatchHeadToHead
{
    public string FirstName { get; set; } = string.Empty;

    public string SecondName { get; set; } = string.Empty;

    /// <summary>Matches the two played each other — always <see cref="FirstWins"/> plus <see cref="SecondWins"/>.</summary>
    public int Matches { get; set; }

    public int FirstWins { get; set; }

    public int SecondWins { get; set; }

    /// <summary>The newest match they played, or null when they have not met.</summary>
    public MatchRecord? LastMeeting { get; set; }
}
