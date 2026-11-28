using PoolScoreboard.Core.Enums;
using PoolScoreboard.Core.Models;
using PoolScoreboard.Core.Persistence;

namespace PoolScoreboard.Core;

/// <summary>
/// Orchestrator for all game state. Mutations can arrive from different threads — the WPF UI, its
/// keyboard shortcuts and the local HTTP control API the Stream Deck drives all call into the same
/// instance — so every mutation runs under one internal lock. <see cref="GameStateChanged"/> is
/// raised inside that lock with a snapshot copied at mutation time, which is what keeps deliveries
/// in mutation order: a later change can't notify subscribers before an earlier one, so the UI and
/// the overlay streams never apply a stale snapshot last. Subscribers must therefore not block;
/// the current ones only marshal to a dispatcher or enqueue for an SSE stream.
/// </summary>
public class GameManager
{
    private readonly object _sync = new();

    /// <summary>
    /// What "now" means to the shot clock. Injected so a test can say exactly when the clock was started and
    /// assert the deadline it produced, rather than sleeping and comparing with a tolerance.
    /// </summary>
    private readonly Func<DateTimeOffset> _clock;

    private GameState _currentGame = new();

    public GameManager(Func<DateTimeOffset>? clock = null) => _clock = clock ?? (() => DateTimeOffset.Now);

    /// <summary>
    /// How many commands undo can take back. A cap rather than an unbounded log: a long match would otherwise keep
    /// every shot of it in memory for the sake of a mistake made in the last minute.
    /// </summary>
    public const int MaxUndoDepth = 50;

    /// <summary>
    /// The state as it was before each command undo can reverse, newest last. Snapshots rather than one entry per
    /// kind of command: a wrong point, shooter, group or ball all become the same line, and a command added later is
    /// covered without writing its own inverse. Internal bookkeeping only — not part of
    /// <see cref="GameState"/>/the overlay payload — and cleared when a match starts, is reset or resumed, or a new
    /// rack begins.
    /// </summary>
    private readonly List<GameState> _undoHistory = new();

    /// <summary>
    /// Remembers the state as it was before a command undo can take back. Called under the lock, before the mutation.
    /// The oldest entry goes at the cap: it is the mistake an operator is least likely to still want.
    /// </summary>
    private void RecordForUndo()
    {
        if (_undoHistory.Count == MaxUndoDepth)
            _undoHistory.RemoveAt(0);

        _undoHistory.Add(_currentGame.Clone());
    }

    public event EventHandler<GameStateChangedEventArgs>? GameStateChanged;

    /// <summary>
    /// Starts the match with the two players already in state — the ones the setup screen has been
    /// writing through <see cref="SetPlayerDetails" />, so the screen never holds a copy of them.
    /// </summary>
    public void InitializeGame(GameType gameType, RaceToMode raceToMode)
    {
        lock (_sync)
        {
            _undoHistory.Clear();

            var next = new GameState
            {
                Player1 = _currentGame.Player1,
                Player2 = _currentGame.Player2,
                GameType = gameType,
                RaceToMode = raceToMode,
                CurrentShooter = _currentGame.Player1,

                // Setup choices too, like the styling carried below: the lengths were chosen on this screen
                // moments ago, and a fresh state would quietly fall back to 40/30 on the first START MATCH.
                ShotClockSeconds = _currentGame.ShotClockSeconds,
                ShotClockExtensionSeconds = _currentGame.ShotClockExtensionSeconds,
                MatchId = Guid.NewGuid().ToString("n"),
                IsGameActive = true
            };
            CarryOperatorStyling(_currentGame, next);

            _currentGame = next;
            RaiseGameStateChanged(_currentGame.Clone());
        }
    }

    /// <summary>
    /// Moves the operator's look — colour theme, scoreboard style, show/hide settings, how the shot clock is drawn
    /// and whether a dead stream is marked — onto a new state, as copies. Both ways of leaving a match (starting one and resetting one) keep
    /// these, because they describe the show rather than the score, and the HTTP control API paths have no screen
    /// behind them to re-apply anything.
    /// </summary>
    private static void CarryOperatorStyling(GameState from, GameState to)
    {
        to.ColorTheme = from.ColorTheme.Copy();
        to.ScoreboardStyle = from.ScoreboardStyle.Copy();
        to.Visibility = from.Visibility.Copy();
        to.ShotClockDisplay = from.ShotClockDisplay;
        to.StaleCueEnabled = from.StaleCueEnabled;
    }

    public void AddPoint(bool isPlayer1)
    {
        lock (_sync)
        {
            if (!_currentGame.IsGameActive)
                return;

            RecordForUndo();

            _currentGame.CueBallSpin = null;

            if (isPlayer1)
                _currentGame.Player1Score++;
            else
                _currentGame.Player2Score++;

            UpdateWinner();

            // A point ends a shot and the next one begins, so a clock that is running restarts for it — unless the
            // point won the match, because a won match has no next shot: the clock goes with the winner banner
            // rather than counting down behind it.
            if (_currentGame.Winner is not null)
                HideShotClock();
            else
                RestartRunningShotClock();

            RaiseGameStateChanged(_currentGame.Clone());
        }
    }

    /// <summary>
    /// Corrects one side's score downwards: an explicit per-side minus, which is itself undoable, so a minus pressed
    /// by mistake is taken back the same way a point is. Reaching Race-To only sets the winner banner, so the match
    /// stays editable and a mistake can be corrected here without a full reset.
    /// </summary>
    public void SubtractPoint(bool isPlayer1)
    {
        lock (_sync)
        {
            if (!_currentGame.IsGameActive)
                return;

            // Only a minus that takes a point off is a command; one that floors at zero changes nothing and would
            // otherwise fill the history with presses that undo has nothing to restore.
            if (isPlayer1 ? _currentGame.Player1Score > 0 : _currentGame.Player2Score > 0)
                RecordForUndo();

            if (isPlayer1)
            {
                if (_currentGame.Player1Score > 0)
                    _currentGame.Player1Score--;
            }
            else
            {
                if (_currentGame.Player2Score > 0)
                    _currentGame.Player2Score--;
            }

            UpdateWinner();
            RaiseGameStateChanged(_currentGame.Clone());
        }
    }

    /// <summary>
    /// Mirrors the setup screen's game type and Race-To mode into the preview state. It takes no
    /// players because the screen writes those into state itself, under this same lock, through
    /// <see cref="SetPlayerDetails" />.
    /// </summary>
    public void SetMatchPreview(GameType gameType, RaceToMode raceToMode)
    {
        lock (_sync)
        {
            if (_currentGame.IsGameActive)
                return;

            _currentGame.GameType = gameType;
            _currentGame.RaceToMode = raceToMode;

            RaiseGameStateChanged(_currentGame.Clone());
        }
    }

    /// <summary>
    /// The two shot-clock lengths Match Setup offers: how long a shot gets, and what calling an extension adds.
    /// Setup-only, like <see cref="SetMatchPreview" /> — a length that changed mid-rack would move the deadline
    /// under the player already counting down. Both are clamped rather than rejected, so a slipped digit still
    /// leaves a clock that can be used.
    /// </summary>
    public void SetShotClockLengths(int shotClockSeconds, int extensionSeconds)
    {
        lock (_sync)
        {
            if (_currentGame.IsGameActive)
                return;

            _currentGame.ShotClockSeconds = Math.Clamp(
                shotClockSeconds, GameState.MinShotClockSeconds, GameState.MaxShotClockSeconds);
            _currentGame.ShotClockExtensionSeconds = Math.Clamp(
                extensionSeconds, GameState.MinShotClockSeconds, GameState.MaxShotClockSeconds);

            RaiseGameStateChanged(_currentGame.Clone());
        }
    }

    /// <summary>
    /// Writes one side's Match Setup fields — name, team, Race-To target and end-cap icon — into the
    /// state the overlay previews. The setup screen goes through here instead of holding a
    /// <see cref="Player" /> of its own, so these writes take the same lock as every other mutation
    /// and a snapshot being serialized for an overlay never catches a half-updated player (a new
    /// icon version paired with the previous artwork, say).
    /// </summary>
    public void SetPlayerDetails(bool isHome, string name, string teamName, int raceToTarget, string? endCapIcon)
    {
        lock (_sync)
        {
            var player = isHome ? _currentGame.Player1 : _currentGame.Player2;

            // Names are operator free text shown on three surfaces — the Controller's live view, the
            // overlay's name pills and its winner banner — and each has to fall back to HOME/AWAY when
            // a name is missing. Trim here, at the single write path, so none of them has to decide
            // for itself what "blank" means; a spaces-only name is stored as an empty one.
            player.Name = name.Trim();
            player.TeamName = teamName.Trim();
            player.RaceToTarget = raceToTarget;

            // Only a real artwork change counts as one: assigning the icon on every keystroke would
            // bump EndCapIconVersion and make every overlay re-download an unchanged icon.
            if (!string.Equals(player.EndCapIcon, endCapIcon, StringComparison.Ordinal))
                player.EndCapIcon = endCapIcon;

            RaiseGameStateChanged(_currentGame.Clone());
        }
    }

    /// <summary>
    /// Switches to the other player as one atomic step, so two overlapping button presses can't both
    /// act on the same stale read.
    /// </summary>
    public void ToggleCurrentShooter()
    {
        lock (_sync)
        {
            if (!_currentGame.IsGameActive)
                return;

            RecordForUndo();

            _currentGame.CurrentShooter = ReferenceEquals(_currentGame.CurrentShooter, _currentGame.Player1)
                ? _currentGame.Player2
                : _currentGame.Player1;

            // The other player is at the table, so the clock on screen belongs to nobody: it restarts for the shot
            // they are about to play, the same way a point does.
            RestartRunningShotClock();

            RaiseGameStateChanged(_currentGame.Clone());
        }
    }

    /// <summary>
    /// Takes back the last command the operator can change their mind about — a point or a minus, a shooter toggle, a
    /// ball group, a ball on or off the table — by putting back the state recorded before it. One key and one Stream
    /// Deck button for all of them, because a wrong shooter mid-stream is corrected the same way a wrong point is.
    /// <para>
    /// What does not come back is the clock and the look: a deadline restored from a minute ago would draw a clock
    /// that is already expired, and a colour chosen after the mistake is not part of it, so the snapshot only
    /// supplies what describes the match. Nothing happens at setup, after a reset or resume, or once a new rack has
    /// started, where the history has been cleared.
    /// </para>
    /// </summary>
    public void Undo()
    {
        lock (_sync)
        {
            if (_undoHistory.Count == 0)
                return;

            var previous = _undoHistory[^1];
            _undoHistory.RemoveAt(_undoHistory.Count - 1);

            RestoreMatchShape(previous);
            RaiseGameStateChanged(_currentGame.Clone());
        }
    }

    /// <summary>
    /// Puts the parts of a snapshot that describe the match back onto the current state: scores, shooter, ball groups,
    /// the balls off the table and the winner. Field by field rather than swapping the state object, because the
    /// Controller holds the players it was set up with; the shooter and the winner are translated by the side they
    /// stood for, since a snapshot holds copies.
    /// </summary>
    private void RestoreMatchShape(GameState snapshot)
    {
        _currentGame.Player1Score = snapshot.Player1Score;
        _currentGame.Player2Score = snapshot.Player2Score;

        _currentGame.Player1.BallGroup = snapshot.Player1.BallGroup;
        _currentGame.Player2.BallGroup = snapshot.Player2.BallGroup;

        _currentGame.PocketedBalls.Clear();
        foreach (var ballNumber in snapshot.PocketedBalls)
            _currentGame.PocketedBalls.Add(ballNumber);

        _currentGame.CurrentShooter = snapshot.CurrentShooter switch
        {
            null => null,
            var shooter when ReferenceEquals(shooter, snapshot.Player1) => _currentGame.Player1,
            _ => _currentGame.Player2
        };

        _currentGame.Winner = snapshot.Winner switch
        {
            null => null,
            var winner when ReferenceEquals(winner, snapshot.Player1) => _currentGame.Player1,
            _ => _currentGame.Player2
        };
    }

    /// <summary>
    /// Assigns one side's 8-ball group, flipping the other side to the complement. Side-based rather
    /// than player-based so a caller never has to read the current players (and possibly act on a
    /// player that a concurrent reset has already replaced).
    /// </summary>
    public void AssignBallGroup(bool isHome, BallGroup group)
    {
        lock (_sync)
        {
            if (_currentGame.GameType != GameType.EightBall)
                return;

            var player = isHome ? _currentGame.Player1 : _currentGame.Player2;
            var other = isHome ? _currentGame.Player2 : _currentGame.Player1;

            // Only a change is a command: pressing the group a side already has would otherwise leave an undo that
            // restores what is already on screen.
            if (player.BallGroup != group)
                RecordForUndo();

            player.BallGroup = group;
            other.BallGroup = group switch
            {
                BallGroup.Solids => BallGroup.Stripes,
                BallGroup.Stripes => BallGroup.Solids,
                _ => BallGroup.Unassigned
            };

            RaiseGameStateChanged(_currentGame.Clone());
        }
    }

    /// <summary>
    /// Marks a ball as off the table, which ends the shot it was potted in: see <see cref="BallLeftTheTable"/>.
    /// Marking a ball that is already pocketed is not a shot and leaves the clock alone.
    /// </summary>
    public void PocketBall(int ballNumber)
    {
        lock (_sync)
        {
            // Bringing the ball back is undoable too, so both halves of a per-ball button are commands; a press on a
            // ball that is already off the table changes nothing and records nothing.
            if (!_currentGame.PocketedBalls.Contains(ballNumber))
                RecordForUndo();

            if (_currentGame.PocketedBalls.Add(ballNumber))
                BallLeftTheTable(ballNumber);

            RaiseGameStateChanged(_currentGame.Clone());
        }
    }

    /// <summary>
    /// Puts a ball back on the table. Deliberately leaves the clock alone: that is the operator correcting a
    /// mistake, not a shot, and the player still has the time they had.
    /// </summary>
    public void UnpocketBall(int ballNumber)
    {
        lock (_sync)
        {
            if (_currentGame.PocketedBalls.Contains(ballNumber))
                RecordForUndo();

            _currentGame.PocketedBalls.Remove(ballNumber);
            RaiseGameStateChanged(_currentGame.Clone());
        }
    }

    /// <summary>
    /// Flips a ball's pocketed state as one atomic step, for the HTTP control API's per-ball buttons
    /// (a separate read-then-write would let two overlapping calls both see "not pocketed").
    /// </summary>
    public void ToggleBall(int ballNumber)
    {
        lock (_sync)
        {
            RecordForUndo();

            // Only the pocketing half is a shot; bringing a ball back is a correction and leaves the clock running.
            if (!_currentGame.PocketedBalls.Remove(ballNumber) &&
                _currentGame.PocketedBalls.Add(ballNumber))
            {
                BallLeftTheTable(ballNumber);
            }

            RaiseGameStateChanged(_currentGame.Clone());
        }
    }

    /// <summary>
    /// A new rack is a new game: the previous shot's countdown has nothing left to time, so it is stopped — but the
    /// new rack is not started either. The undo history goes with the old rack, because walking back into it would put
    /// the last rack's balls back on a table that has just been cleared. The clock waits at its full length (armed, not running) until the operator
    /// starts it after the break, which is both what the rack's allowance is worth showing and what stops a
    /// countdown running while a player is still chalking up. Both players also get their extension back, because
    /// the rule is one each per rack.
    /// </summary>
    public void ResetBalls()
    {
        lock (_sync)
        {
            _undoHistory.Clear();
            _currentGame.PocketedBalls.Clear();
            _currentGame.ShotClockEndsAt = null;
            _currentGame.ShotClockArmed = true;
            _currentGame.ShotClockPausedRemaining = null;
            _currentGame.HomeShotClockExtensionUsed = false;
            _currentGame.AwayShotClockExtensionUsed = false;
            RaiseGameStateChanged(_currentGame.Clone());
        }
    }

    /// <summary>
    /// Starts, or restarts, the shot clock: the overlay counts down from <see cref="GameState.ShotClockSeconds"/> as
    /// of now, and keeps counting on its own — no frame per second is sent for it. Restarting is the point: each
    /// shot of a match gets its own countdown, started by the operator (the Controller's key or a Stream Deck
    /// button) as the player addresses the ball. Ignored at setup, where there is no shot to time, and once the
    /// match has been won: the banner is up, and a clock behind it would be timing nothing.
    /// </summary>
    public void StartShotClock()
    {
        lock (_sync)
        {
            if (!_currentGame.IsGameActive || _currentGame.Winner is not null)
                return;

            // Starting takes over from an armed clock and from a paused one: the idle full-length display a new
            // rack leaves behind, and the frozen one a pause leaves behind, are the same clock — a fresh start is a
            // fresh shot, from the full length.
            _currentGame.ShotClockArmed = false;
            _currentGame.ShotClockPausedRemaining = null;
            _currentGame.ShotClockEndsAt = _clock().AddSeconds(_currentGame.ShotClockSeconds);
            RaiseGameStateChanged(_currentGame.Clone());
        }
    }

    /// <summary>
    /// Freezes the running clock where it is: the deadline comes off the state and the time left goes into
    /// <see cref="GameState.ShotClockPausedRemaining"/>, so the overlay can draw a clock that is visibly not
    /// counting. It exists for what a match stops for that is nobody's shot — a ref call, a video review, a cue
    /// change — where the alternatives are both wrong: a counting clock times something nobody is playing, and a
    /// clear-and-restart claims the shot has its full length again.
    /// <para>
    /// Only a running clock can be paused: one waiting at its full length after a new rack is already not counting,
    /// and there is nothing to freeze. Nothing is raised when there is no clock to pause, so a press at the wrong
    /// moment cannot wake the overlay pages.
    /// </para>
    /// </summary>
    public void PauseShotClock()
    {
        lock (_sync)
        {
            if (!_currentGame.IsGameActive || _currentGame.Winner is not null)
                return;

            if (_currentGame.ShotClockEndsAt is not { } deadline)
                return;

            // Clamped at zero: pausing a clock that has already run out leaves it expired rather than handing back
            // time it never had.
            var remaining = deadline - _clock();
            _currentGame.ShotClockEndsAt = null;
            _currentGame.ShotClockPausedRemaining = remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
            RaiseGameStateChanged(_currentGame.Clone());
        }
    }

    /// <summary>
    /// Puts a paused clock back to counting from the time it was frozen at, not from the full length: a shot paused
    /// with twenty seconds left resumes with twenty. Ignored when nothing is paused, so a second press cannot start
    /// a shot nobody meant to start.
    /// </summary>
    public void ResumeShotClock()
    {
        lock (_sync)
        {
            if (_currentGame.ShotClockPausedRemaining is not { } remaining)
                return;

            _currentGame.ShotClockPausedRemaining = null;
            _currentGame.ShotClockEndsAt = _clock() + remaining;
            RaiseGameStateChanged(_currentGame.Clone());
        }
    }

    /// <summary>
    /// Grants the current shooter their extension: the clock already running gains
    /// <see cref="GameState.ShotClockExtensionSeconds" /> rather than starting over, because an extension is more
    /// time on the shot in progress.
    /// <para>
    /// One per player per rack, enforced by the state's two used-flags: a second call from the same player changes
    /// nothing and raises nothing, while the other player still has theirs, and a new rack gives both of them one
    /// again. Ignored when there is no clock to add to, so an extension called at the wrong moment is not spent on
    /// nothing — the player can still call it once the clock is running.
    /// </para>
    /// </summary>
    public void ExtendShotClock()
    {
        lock (_sync)
        {
            if (!_currentGame.IsGameActive || _currentGame.Winner is not null)
                return;

            var deadline = _currentGame.ShotClockEndsAt;
            var paused = _currentGame.ShotClockPausedRemaining;
            if ((deadline is null && paused is null) || _currentGame.CurrentShooter is null)
                return;

            var isHome = ReferenceEquals(_currentGame.CurrentShooter, _currentGame.Player1);
            if (isHome ? _currentGame.HomeShotClockExtensionUsed : _currentGame.AwayShotClockExtensionUsed)
                return;

            if (isHome)
                _currentGame.HomeShotClockExtensionUsed = true;
            else
                _currentGame.AwayShotClockExtensionUsed = true;

            // An extension is more time on the shot in progress, so it lands on whichever of the two is holding
            // that time: the deadline on screen, or the pause that froze it.
            var extension = TimeSpan.FromSeconds(_currentGame.ShotClockExtensionSeconds);
            if (paused is { } held)
                _currentGame.ShotClockPausedRemaining = held + extension;
            else
                _currentGame.ShotClockEndsAt = deadline!.Value + extension;

            RaiseGameStateChanged(_currentGame.Clone());
        }
    }

    /// <summary>
    /// What a ball leaving the table does to a running clock, whether it was the Controller's button or a Stream
    /// Deck one. The money ball ends the rack — the 8 in 8-ball, the 9 in 9-ball, the 10 in 10-ball — so the clock
    /// stops with it rather than restarting: there is no next shot to time, and a countdown running on over the
    /// end of the rack would be timing a game that is already over. Every other ball ends the shot, not the rack,
    /// and restarts the clock for the next one.
    /// </summary>
    private void BallLeftTheTable(int ballNumber)
    {
        if (ballNumber == _currentGame.MoneyBallNumber)
        {
            HideShotClock();
        }
        else
        {
            RestartRunningShotClock();
        }
    }

    /// <summary>
    /// Restarts a clock that is showing, from the full shot length. Does nothing when none is showing: a match the
    /// operator runs without a clock must not sprout one from a point, a ball or a shooter change.
    /// </summary>
    private void RestartRunningShotClock()
    {
        if (_currentGame.ShotClockEndsAt is null && _currentGame.ShotClockPausedRemaining is null)
            return;

        // A paused clock counts as showing, and the shot ending ends the pause with it: the next shot is timed
        // normally rather than picking up the frozen one, which is what keeps a pause meaning "this shot, held"
        // instead of leaking into the rest of the rack.
        _currentGame.ShotClockPausedRemaining = null;
        _currentGame.ShotClockEndsAt = _clock().AddSeconds(_currentGame.ShotClockSeconds);
    }

    /// <summary>Takes the clock off the screen: not counting, not waiting, not paused.</summary>
    private void HideShotClock()
    {
        _currentGame.ShotClockEndsAt = null;
        _currentGame.ShotClockArmed = false;
        _currentGame.ShotClockPausedRemaining = null;
    }

    /// <summary>
    /// Hides the clock, whether it is counting, paused or waiting at its full length after a new rack. Nothing is
    /// raised when there was none, so a repeated clear cannot wake the overlay pages for a state they already show.
    /// </summary>
    public void ClearShotClock()
    {
        lock (_sync)
        {
            if (_currentGame.ShotClockEndsAt is null && !_currentGame.ShotClockArmed &&
                _currentGame.ShotClockPausedRemaining is null)
            {
                return;
            }

            HideShotClock();
            RaiseGameStateChanged(_currentGame.Clone());
        }
    }

    /// <summary>
    /// Turns the overlay's dead-stream cue on or off. A look setting like the clock's drawing rather than a rule:
    /// allowed mid-match, carried into the next state by <see cref="CarryOperatorStyling"/>, and saved with the
    /// profile.
    /// </summary>
    public void SetStaleCue(bool enabled)
    {
        lock (_sync)
        {
            _currentGame.StaleCueEnabled = enabled;
            RaiseGameStateChanged(_currentGame.Clone());
        }
    }

    /// <summary>
    /// Chooses how a running clock is drawn: the countdown as numbers, or as a line that drains and turns red in its
    /// last five seconds. Deliberately allowed mid-match, unlike the lengths — it changes nothing about the shot,
    /// only how it reads on the overlay — and it rides along with the rest of the look into the next match.
    /// </summary>
    public void SetShotClockDisplay(ShotClockDisplay display)
    {
        lock (_sync)
        {
            _currentGame.ShotClockDisplay = display;
            RaiseGameStateChanged(_currentGame.Clone());
        }
    }

    /// <summary>
    /// Clears the match — scores, undo history, pocketed balls, winner — and returns to the setup
    /// screen. What the operator configured rather than played is carried over: the two players
    /// (they are the ones the setup screen writes through <see cref="SetPlayerDetails" />, so
    /// replacing them would blank the names typed in whenever the reset comes from the HTTP control
    /// API, which has no screen state to restore them from), the chosen game type and Race-To mode
    /// (so the overlay's preview still matches the setup screen), and the colour theme, scoreboard
    /// style and show/hide settings (a reset ends a match, it doesn't restyle the show). Their ball
    /// groups are cleared, so the next 8-ball rack starts unassigned exactly as a brand-new state
    /// would.
    /// </summary>
    public void ResetGame()
    {
        lock (_sync)
        {
            var player1 = _currentGame.Player1;
            var player2 = _currentGame.Player2;

            player1.BallGroup = BallGroup.Unassigned;
            player2.BallGroup = BallGroup.Unassigned;
            _undoHistory.Clear();

            var next = new GameState
            {
                Player1 = player1,
                Player2 = player2,

                // Setup, not play. Without this the reset state fell back to the enum defaults —
                // 8-ball, single Race-To — while the setup screen still showed what was selected, so
                // a reset over HTTP (which has no screen to re-push anything) left the overlay
                // previewing 8-ball until the operator started a new match.
                GameType = _currentGame.GameType,
                RaceToMode = _currentGame.RaceToMode,
                ShotClockSeconds = _currentGame.ShotClockSeconds,
                ShotClockExtensionSeconds = _currentGame.ShotClockExtensionSeconds
            };
            CarryOperatorStyling(_currentGame, next);

            _currentGame = next;
            RaiseGameStateChanged(_currentGame.Clone());
        }
    }

    /// <summary>
    /// Puts a saved match back, for the autosave that has to survive a crash mid-match. Scores are floored at
    /// zero and the winner is recomputed from them rather than trusted from the file; the undo history starts
    /// empty, because the points it would undo are no longer on screen. Players, styling and show/hide settings
    /// are left alone — the operator profile restored them moments earlier, and the scores are judged against
    /// those players' Race-To targets.
    /// </summary>
    public void RestoreMatch(ResumableMatch match)
    {
        lock (_sync)
        {
            _undoHistory.Clear();

            var next = new GameState
            {
                Player1 = _currentGame.Player1,
                Player2 = _currentGame.Player2,
                GameType = match.GameType,
                RaceToMode = match.RaceToMode,

                // The lengths are Match Setup choices this match started with, so a resumed one keeps them even
                // though the clock itself — a countdown from another session — is deliberately not saved.
                ShotClockSeconds = _currentGame.ShotClockSeconds,
                ShotClockExtensionSeconds = _currentGame.ShotClockExtensionSeconds,

                Player1Score = Math.Max(0, match.HomeScore),
                Player2Score = Math.Max(0, match.AwayScore),
                CurrentShooter = match.HomeIsCurrentShooter ? _currentGame.Player1 : _currentGame.Player2,

                // The id the match was played under, so a restored match that already finished is not a second
                // record on the next launch. A file from before the id existed gets a fresh one: nothing can
                // know better, and a duplicate is likelier than a missed record.
                MatchId = string.IsNullOrEmpty(match.MatchId) ? Guid.NewGuid().ToString("n") : match.MatchId,
                IsGameActive = true
            };

            // Groups are an 8-ball idea; a stale or hand-edited file must not put them on a 9-ball match where
            // nothing reads them.
            if (match.GameType == GameType.EightBall)
            {
                next.Player1.BallGroup = match.HomeBallGroup;
                next.Player2.BallGroup = match.AwayBallGroup;
            }

            foreach (var ball in match.PocketedBalls)
            {
                if (ball is >= 1 and <= 15)
                    next.PocketedBalls.Add(ball);
            }

            CarryOperatorStyling(_currentGame, next);

            _currentGame = next;
            UpdateWinner();
            RaiseGameStateChanged(_currentGame.Clone());
        }
    }

    public void SetColorTheme(ColorTheme theme)
    {
        lock (_sync)
        {
            // Copied like the scoreboard style, so a caller that keeps its own reference can't go on
            // mutating state — off the lock, with no change event — after this returns.
            _currentGame.ColorTheme = theme.Copy();
            RaiseGameStateChanged(_currentGame.Clone());
        }
    }

    public void SetScoreboardStyle(ScoreboardStyle style)
    {
        lock (_sync)
        {
            _currentGame.ScoreboardStyle = new ScoreboardStyle
            {
                CornerRoundness = Math.Clamp(style.CornerRoundness, 0, 100),
                OverallScale = Math.Clamp(style.OverallScale, 50, 200),
                GlossyFinish = style.GlossyFinish,
                EndCapStyle = style.EndCapStyle,
                ShooterIndicatorStyle = style.ShooterIndicatorStyle
            };
            RaiseGameStateChanged(_currentGame.Clone());
        }
    }

    public void SetScoreBarVisible(bool visible)
    {
        lock (_sync)
        {
            _currentGame.Visibility.ScoreBarVisible = visible;
            RaiseGameStateChanged(_currentGame.Clone());
        }
    }

    public void SetBallTrackerVisible(bool visible)
    {
        lock (_sync)
        {
            _currentGame.Visibility.BallTrackerVisible = visible;
            RaiseGameStateChanged(_currentGame.Clone());
        }
    }

    public void SetWinnerBannerVisible(bool visible)
    {
        lock (_sync)
        {
            _currentGame.Visibility.WinnerBannerVisible = visible;
            RaiseGameStateChanged(_currentGame.Clone());
        }
    }

    public void SetCueBallSpin(double x, double y)
    {
        lock (_sync)
        {
            _currentGame.CueBallSpin = new CueBallSpin
            {
                X = Math.Clamp(x, 0.0, 1.0),
                Y = Math.Clamp(y, 0.0, 1.0)
            };
            RaiseGameStateChanged(_currentGame.Clone());
        }
    }

    public void ClearCueBallSpin()
    {
        lock (_sync)
        {
            _currentGame.CueBallSpin = null;
            RaiseGameStateChanged(_currentGame.Clone());
        }
    }

    /// <summary>A copied view of the current state, safe to read or serialize on any thread.</summary>
    public GameState GetStateSnapshot()
    {
        lock (_sync)
        {
            return _currentGame.Clone();
        }
    }

    /// <summary>
    /// Recomputes the winner from the scores after every score change rather than latching it: a
    /// reached Race-To only shows the banner, and the match stays active so a later correction can
    /// move or clear it.
    /// </summary>
    private void UpdateWinner()
    {
        if (_currentGame.Player1Score >= _currentGame.Player1.RaceToTarget)
            _currentGame.Winner = _currentGame.Player1;
        else if (_currentGame.Player2Score >= _currentGame.Player2.RaceToTarget)
            _currentGame.Winner = _currentGame.Player2;
        else
            _currentGame.Winner = null;
    }

    private void RaiseGameStateChanged(GameState snapshot)
    {
        GameStateChanged?.Invoke(this, new GameStateChangedEventArgs { GameState = snapshot });
    }
}

public class GameStateChangedEventArgs : EventArgs
{
    public GameState GameState { get; set; } = new();
}
