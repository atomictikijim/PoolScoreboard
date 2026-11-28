# CLAUDE.md

This file provides guidance to Claude Code when working with code in this repository.

## Project Overview

**PoolScoreboard** is a Windows desktop application for running a pool-match scoreboard as
an OBS Studio browser-source overlay, controllable from a desktop window, the keyboard, or
an Elgato Stream Deck. It must work fully offline — no calls to any external service, ever.
It is a three-project .NET 8.0 solution: a game-logic Core, a WPF Controller UI that also
hosts the overlay server in-process, and an ASP.NET Core Overlay module serving the OBS
browser sources and the local Stream Deck control API.

**This is a reset of an earlier attempt at this project.** The original build was
league-based (APA/USAPL/BCA/TAP rulesets with skill-level or Fargo-rating-driven auto
Race-To calculations). That direction is discarded — see NOTES.md, "2026-08-13 — Project
scope reset". Nothing here should reference leagues, skill levels, or Fargo ratings; if you
see that in the existing code, it is legacy and slated for removal (PROGRESS.md Phase 0).

## Project Layout

- `PoolScoreboard.sln` — solution file
- `PoolScoreboard.Core/` — game logic, state, and data models (class library, no UI, no I/O)
- `PoolScoreboard.Controller/` — WPF desktop application; operator's control window, and the
  process that hosts the Overlay's Kestrel server in-process
- `PoolScoreboard.Overlay/` — ASP.NET Core module: OBS browser-source pages + local HTTP
  control API for the Stream Deck (hosted inside the Controller process, not a separate exe)
- `streamdeck/` — Stream Deck profile documentation (button → local endpoint mapping)
- `README.md` / `CLAUDE.md` / `NOTES.md` / `PROGRESS.md` / `UAT.md` — project docs, with `UAT.md` holding the
  manual checks (what a window looks like, what survives a relaunch) that the unit tests cannot reach

## Build & Run Commands

```powershell
# Build the solution
dotnet build

# Run the Controller (starts the WPF UI and the in-process overlay/control server)
dotnet run --project PoolScoreboard.Controller/PoolScoreboard.Controller.csproj

# Run tests (once PoolScoreboard.Core.Tests exists)
dotnet test

# Clean build artifacts
dotnet clean
```

## C# / Windows Best Practices

- **Target an explicit TFM** in every `.csproj` (`net8.0` for Core, `net8.0-windows` for
  Controller with WPF, `net8.0` for Overlay).
- **Enable nullable reference types** (`<Nullable>enable</Nullable>`) on all projects.
- **Implicit usings enabled** (`<ImplicitUsings>enable</ImplicitUsings>`).
- **File-scoped namespaces** — use `namespace X;` syntax throughout.
- **Separate UI from logic:** Core contains all game rules and state (testable, UI-agnostic);
  Controller binds UI to Core via event subscriptions/data binding; Overlay reads/writes
  state through the same in-process `GameManager` instance — never a second copy of state.
- **Event-based communication:** `GameManager` raises `GameStateChanged` whenever state
  mutates; the Controller UI, the OBS overlay pages, and the Stream Deck API all subscribe
  to or read from this instead of polling each other.
- **Localhost only:** the Overlay's Kestrel server must bind to `localhost`/`127.0.0.1`
  only — never `0.0.0.0` — so the app stays usable and safe with no network connection and
  isn't reachable from outside the machine.
- **No CDN/external references** anywhere in the overlay HTML/CSS/JS (fonts, JS libraries,
  images) — everything must be bundled locally so the overlay renders with no internet
  connection.

## Domain Model (target design — see PROGRESS.md Phase 0/1 for current state)

- `GameType` — `EightBall`, `NineBall`, `TenBall`.
- `RaceToMode` — `Single` (one shared target) or `Split` (each player has their own target).
- `BallGroup` — `Unassigned`, `Solids`, `Stripes`. Only meaningful for 8-ball; the 8-ball
  itself is never assigned to a group and always renders center.
- `Player` — `Id` (stable per player; the end-cap icon URL is keyed on it), `Name`,
  `TeamName` (optional), `RaceToTarget`, `BallGroup` (8-ball only), `EndCapIcon` (the team logo
  or flag the operator picked, held as a data URI) and `EndCapIconVersion`, which changes only
  when the artwork does so the served icon URL can be cache-busted with `?v=`.
- `GameState` — both `Player`s, `GameType`, `RaceToMode`, scores, `CurrentShooter`,
  `ColorTheme`, `ScoreboardStyle` (corner roundness, scale, gloss finish, end-cap style,
  shooter-indicator style), `Visibility` (score bar / ball tracker / winner banner shown or
  hidden), `PocketedBalls` (ball numbers 1-15 currently off the table), `CueBallSpin` (the
  overlay's contact point, or null), `ShotClockSeconds` and `ShotClockExtensionSeconds` (how long a shot gets and
  what calling an extension adds — both chosen at Match Setup, 40 and 30 by default) and `ShotClockEndsAt` (when the
  current shot's countdown runs out, or null when no clock is showing), active/winner status. Two flags,
  `HomeShotClockExtensionUsed` and `AwayShotClockExtensionUsed`, say who has already called their one extension this
  rack. `ShotClockDisplay` says how the running clock is drawn — `Time` for the numbers, `Line` for a line that
  drains and turns red in its last five seconds. It is a look setting rather than a rule: it can be flipped mid-match
  and is carried into the next state with the theme and the show/hide settings.
- `ColorTheme` — operator-set background/accent/text colors, applied to the overlay pages.
- `GameState.MatchId` — which match this is. Assigned when a match starts, carried into `ResumableMatch` so a
  relaunch restores the same one, and stored on each history record, which is what keeps a finished match from
  being filed twice. Empty at setup.
- No league, skill level, or Fargo rating anywhere in this model.

## Ball Display Detail

- Every ball display (8-ball's grouped layout and the numeric 9/10-ball row) supports
  marking individual balls **pocketed**: a pocketed ball renders **greyed out** in place
  (not removed/collapsed from the layout), so the operator and viewers can see at a glance
  what's left on the table.
- `GameManager.PocketBall(ballNumber)` / `UnpocketBall(ballNumber)` toggle membership in
  `GameState.PocketedBalls`; `ResetBalls()` clears it for a new rack. `GameState.MoneyBallNumber` names the ball the
  rack is really counting towards — the 8 in 8-ball, the 9 in 9-ball, the 10 in 10-ball — and the shot clock reads
  it: any other ball leaving the table restarts a running clock for the next shot, while the money ball stops it.
- This applies independently of `BallGroup` assignment in 8-ball — a ball's group (which
  player it's displayed under) and its pocketed state are separate flags.

## Architecture & Key Files

### PoolScoreboard.Core (Game Logic)

- `GameManager` — orchestrator for all game state and operations: `InitializeGame`,
  `AddPoint`/`SubtractPoint`, `UndoPoint`, `ToggleCurrentShooter`, `AssignBallGroup`, `ResetGame`,
  `StartShotClock`/`ExtendShotClock`/`ClearShotClock`, and `SetShotClockLengths` for the two lengths Match Setup
  offers (clamped to 5-300 seconds; ignored once a match is running, because the clock on screen is counting down
  for a player). Raises `GameStateChanged` after every mutation.
  The shot clock is a deadline, not a tick: `StartShotClock` stamps `now + ShotClockSeconds` and the overlay
  counts it down locally, so no frame per second is sent for it. It restarts from the full length whenever a
  running clock's shot ends — a point, a ball leaving the table, or a shooter toggle — and it is ignored at setup
  and once the match has been won, where there is no shot to time. The money ball is the exception to the ball
  rule: potting it ends the rack, so `BallLeftTheTable` stops the clock (null deadline) instead of restarting it,
  and both ball routes — the Controller's buttons and the Stream Deck's per-ball toggle — go through that one
  method. A point that wins the match clears the clock
  rather than restarting it, so nothing counts down behind the winner banner; a reset clears it too, while a new
  rack leaves it **armed**: on screen at the full length, not counting, until the operator starts it after the
  break.
  `ExtendShotClock` adds `ShotClockExtensionSeconds` to the clock already running for the current shooter, once per
  player per rack (a new rack hands both of them one back), and is ignored when no clock is running so a call at
  the wrong moment is not spent. Its "now" comes from a clock injected into the constructor, so a test can assert
  the exact instant rather than sleep and tolerate.
- No database, no async I/O — pure, fast, unit-testable.
- `Persistence/OperatorProfile` (capture/apply of the saved profile), `Persistence/PlayerRoster`
  (saved-player identity — a name, ignoring case — ordering and the 100-entry cap),
  `Persistence/EndCapIconData` (data-URI ↔ icon bytes) and `Persistence/ResumableMatch` (the part of a match in
  progress worth surviving a crash: scores, game type and Race-To mode, ball groups, pocketed balls, shooter,
  with a file version) live here too, deliberately I/O-free so all of them are unit-tested. `Persistence/MatchRecord`
  and `Persistence/MatchArchive` join them for the match history: one finished match as it was played, and the list
  of them newest first under a 200-record cap — what counts as finished and what is kept are both unit-tested
  there.
  `GameManager.RestoreMatch` puts one back atomically — floors scores at zero, drops ball numbers off the
  table, clears the undo history (the points it would undo are no longer on screen) and recomputes the winner
  from the scores rather than trusting the file.

### PoolScoreboard.Controller (WPF Desktop Application)

- MVVM: ViewModels for match setup, player setup, and the live game view.
- Match setup: game type, Race-To mode (single/split) and value(s), the two shot-clock lengths (shot length and
  what an extension adds), color theme picker.
- Live view: score +/- per player, current-shooter toggle with a CALL EXTENSION button beside it, ball-group
  assignment buttons (8-ball only) or ball display (9/10-ball), new-rack/reset-match controls. The shared OVERLAY
  row (on both screens) ends with a **Shot Clock: Time / Line** button that swaps the countdown's drawing; it is a
  look setting, so it works mid-match and is saved with the profile.
- The header also carries a small **shot-clock readout** — `0:32 · running`, `0:40 · waiting` after a new rack, or
  `0:00 · time up`, hidden when there is no clock — so the operator does not have to look at the overlay to know
  where the shot stands. Beside it, one chip per player says who still has their extension (`Alice: 1 left`, dimmed
  to `Alice: used` once spent), because a second call from the same player is silently a no-op and the operator
  needs to know that before pressing CALL EXTENSION rather than after. CALL EXTENSION itself greys out, and the `E`
  key stops answering, while the player at the table has spent theirs — the command's `CanExecute` is that rule
  (`GameViewModel.CanCallExtension`), so the key and the button cannot disagree; a spent extension is a press that
  cannot be made rather than one Core silently ignores. The deadline is one instant and no state change arrives as it passes, so the readout is
  recomputed by a 250 ms `DispatcherTimer` (the same reason the overlay page ticks itself); `GameViewModel` takes
  the "now" for it as an injected clock, the way `GameManager` does, so a test can put it at an exact second.
- On startup, starts the Overlay's Kestrel host in-process, passing it the same
  `GameManager` instance the UI is bound to. `OverlayHostRunner.Start` returns null when the host cannot bind
  (the overlay port, 51234 by default, already taken — usually a leftover Controller) and logs the reason, so startup continues
  without the overlay instead of dying before a window exists. `OverlayHostSession` keeps that failure: the main
  window's top bar shows it (naming the process holding the port, via `Diagnostics/PortOwner`) beside a Retry
  button, so a dead overlay is visible and recoverable without restarting the app. While a host is up the
  session also probes `/overlay/api/scoreboard/state` every 3 s (2 s timeout, one confirming retry), because the
  host is started rather than run and its stopping token is never observed: a host that stops answering reports
  the port, releases itself, raises `Stopped`, and the view model shows the same banner and Retry again
  mid-match. A stop the app asked for is not reported. If the port turns out to be held by another
  `PoolScoreboard.Controller` that shows **no window** — a leftover that outlived its window, or one from a build
  older than the guard — the session closes it and retries the bind once, so the banner cannot become permanent over
  a port only a dead copy of this app is holding (`Diagnostics/StaleController`). A copy that still has a window is
  deliberately left running: that is somebody's session (an older build in use, an instance never shut down), and
  closing it would end their window mid-match — so the banner names it and asks for that window to be closed, and
  Retry takes the port back as soon as it is. The port is the operator's too: an OVERLAY PORT field in the top bar
  (1024–65535, `OperatorProfile.IsUsablePort`) rebinds the host as soon as it loses focus, the banner always names
  the port actually tried, and the choice is saved in `settings.json` — so another program sitting on 51234 costs
  one edit rather than the overlay for the session. Never
  this process, never a program that is not this app, and never a holder whose process cannot be read at all — "no
  window" and "cannot tell" are different answers, and only the first is acted on. The name and the window state
  come from one look at the process (`Diagnostics/StaleController.Observe`), so a holder exiting between them cannot
  have the decision describe one process and act on another; the name is checked once more immediately before the
  close, so a PID the OS has recycled cannot be hit. Window state is `Process.MainWindowHandle`, which enumerates
  the process's visible top-level windows and sends it no message: a minimized Controller (still visible, so still
  windowed) and a hung one both count as having a window, and only a hidden or gone window counts as none.
  `IOverlaySession` is what the window depends on, so the banner and Retry can be tested without a socket.
- Only one Controller runs per desktop session (`SingleInstanceGuard`): a named mutex elects the first process,
  and a second launch sets a named event so the running window comes to the front, logs that it is exiting and
  shuts down before restoring or saving the profile — a duplicate must never write defaults over the running
  instance's `settings.json`/`roster.json`.
- `Persistence/OperatorSettingsStore` reads/writes `%AppData%\PoolScoreboard\settings.json` plus the
  end-cap icon files; the saved profile carries the overlay port as well as the look and the last players. `App`
  applies the saved profile before the window exists and saves on a 750 ms debounce and on exit. The two steps that
touch the port are `Persistence/OverlayPortSettings`, extracted the way `MatchAutosave` was: `ApplySavedPort` hands
the profile's port to the session before anything binds (normalized, so one that cannot be bound becomes the
default), and `RecordPortInUse` stamps the session's port onto a profile being saved, so the next launch comes back
on the port the overlay was really served on rather than the default or a value that never reached the host. `settings.json`, `roster.json` and `match.json` all go through
  `Persistence/DurableFile`, which writes the text under a `.tmp` name, flushes it to the disk and only then moves
  it into place — so a crash mid-save keeps the previous file, and a power cut cannot publish a half-written one.
  End-cap icon bytes go through the same helper (`WriteAllBytes`), so an icon a saved file names is either the
  previous image or the new one, never a truncated file left by a power cut.
- `Persistence/MatchStateStore` is the file; `Persistence/MatchAutosave` is the wiring around it, kept out of
  `App` so its rules are testable. From `Restore()` on it watches `GameStateChanged` and only re-arms a pool timer,
  so a burst of points costs one small write 500 ms later (the event arrives inside `GameManager`'s lock, often from
  an HTTP control thread, and the write never runs on the UI thread); `SaveNow` is the snapshot `App` takes in
  `OnExit`, when the debounce may not have run; and a state back at setup deletes the file, so a reset match is not
  resumed. Watching starts at `Restore()` rather than in the constructor on purpose: applying the operator profile
  fires the event while the match is still at setup, and that snapshot deletes the file the same launch is about to
  load. A snapshot is written through a stream and `Flush(flushToDisk: true)` before the move gives `match.json`
  its name, so a power cut cannot leave a published file that is empty or half written; the rename's own metadata
  is left to NTFS. `App` builds one, calls `Restore()` at startup after the profile and before the window, and
  disposes it on exit.
  `GameViewModel` therefore takes the game type and Race-To mode from state — a resumed 8-ball match shows its
  groups — and fills the live view from the restored snapshot, since no event fires until the operator touches
  something. An unreadable `match.json` is moved to `match.corrupt.json`; one written by another version is
  left in place and ignored.
- `Persistence/MatchHistoryStore` owns `history.json`, and nothing files into it automatically: a finished match
  is recorded when the operator presses **SAVE TO HISTORY**, which appears on the live view with the winner banner
  and goes again on RESET MATCH, so what belongs in the history is a decision rather than a side effect. The record
  is keyed on `GameState.MatchId`, which is what stops a second press — and a relaunch of the same won match, whose
  banner is still up — from filing a duplicate; the rule lives in `MatchArchive` against the file, so it holds
  across a restart. A rematch with the same names and score is a new match and is filed separately.
  `GameViewModel` looks the file up once per match, when its winner appears, which is how a match saved before the
  relaunch shows as saved instead of offering the button again.
  The reading side is `HistoryWindow`, opened from the History button beside Help: given the store rather than a
  copy of the archive, listing the matches newest first with `MatchArchive.Standings()` as a chip per name, and
  letting one row be deleted behind a confirmation — the row goes, the chips are recounted from what is left, and
  `MatchHistoryStore.TryDelete` rewrites the file. Nothing there creates a record. The window is separate on
  purpose — the live view and the setup screen gained nothing but the save control.
- `Diagnostics/TraceLogFile` appends every `Trace` line to `%AppData%\PoolScoreboard\controller.log`
  (attached first in `OnStartup`, disposed last in `OnExit`), so shutdown warnings can be read after the
  fact. At startup a log over 1 MiB is moved to `controller.old.log`, replacing the previous run's. Each
  line is opened, appended and closed — no handle is held — so the log stays readable while the app runs.
  It never throws: an unopenable log just silences the file.
- An exception that reaches the top of the app — `DispatcherUnhandledException`, `AppDomain.UnhandledException`,
  `TaskScheduler.UnobservedTaskException` — is written to that log with its type, message, stack and inner
  exceptions (`Diagnostics/UnhandledExceptionLog`), and left unhandled so the app still fails the same way.
  A `settings.json` or `roster.json` that cannot be read or written logs a "Could not read/write …" warning and
  falls back to defaults instead of failing silently. A file that cannot be read is moved to
  `settings.corrupt.json`/`roster.corrupt.json` (replacing an older rejected copy) before falling back, so the
  next save cannot overwrite the only copy of the operator's setup. A rejected roster's icon files move into
  `roster.corrupt.icons\` at the same time, keeping the artwork the preserved entries refer to.
- `Persistence/PlayerRosterStore` does the same for `roster.json` and one icon file per saved player.
  `GameViewModel` remembers both sides when a match starts and when the window closes, and each side's
  "Saved Player" dropdown fills name, team, Race-To and icon from a roster entry.
- Dark theme with operator-customizable accent color, consistent with the overlay's theme.

### PoolScoreboard.Overlay (ASP.NET Core, hosted in-process by Controller)

- `OverlayHost.Build` returns the host with every page and endpoint mapped but nothing started; `Start` is that
  plus the loopback address and `app.Start()`. The split is what lets `PoolScoreboard.Overlay.Tests` drive the real
  routes under `TestServer` without binding a port, and keeps one mapping list for the app and the tests.
- `/overlay/scoreboard` — browser-source page: scores, Race-To, shooter indicator, ball
  display, shot clock, operator colors. Read-only; polls or subscribes (SSE/WebSocket) for state. The shot clock
  is drawn only while the payload carries a deadline, reads red in its last five seconds, and ticks inside the
  page (`Date.parse` of the ISO instant, four times a second) so an OBS capture counts down between frames. The
  payload's `shotClockDisplay` picks which of the two drawings it uses: the numbers, or a line whose width is the
  fraction of the shot left (against `shotClockSeconds`, capped at a full line when an extension pushes the time
  past the length) and which takes its green/red from the same `low`/`expired` classes the numbers use, so the two
  can never disagree about when time is nearly up. An expired shot fills the whole track red rather than leaving it
  empty, because a drained line and a clock nobody started look identical. `shotClockArmed` draws a clock that is
  waiting at its full length without counting — the state a new rack leaves behind — dimmed so it cannot be
  mistaken for a running one.
- `/overlay/cueball` — separate browser-source page: a cue-ball graphic; clicking it places
  a red dot marking the spin/contact point. Independent of and separately positionable from
  the scoreboard overlay in the OBS scene.
- `/api/control/*` — local HTTP endpoints for Stream Deck buttons (add a point per side, undo
  the last point, shooter toggle, ball assignment, per-ball pocketed toggle, new rack, reset
  match, start/restart and hide the shot clock). Same `GameManager` instance as the UI.
- `Diagnostics/TraceLoggerProvider` forwards the host's own `ILogger` warnings and errors to `Trace`, which the
  Controller's log file records — without it they went to the Console/Debug providers and a WPF process has no
  console to write them to.
- Both pages subscribe with a plain `EventSource` and nothing else, so an outage of the host is survivable on the
  OBS side: the page keeps its last frame while the stream is down, `EventSource` retries on its own, and the
  reconnected stream replays the current snapshot, so it catches up within a few seconds of a Retry without a
  refresh in OBS. Measured on the scoreboard page in Chromium, not OBS's CEF; the cue-ball page shares the
  pattern but was not exercised.

## Stream Deck Integration

- No custom Elgato SDK plugin — Stream Deck buttons hit the local `/api/control/*` HTTP
  endpoints directly (built-in "Website"/HTTP-request-style action), which is simpler to
  build and maintain and needs no separate plugin install.
- `streamdeck/` documents the button layout and which endpoint each button calls, so the
  actual `.streamDeckProfile` can be built/exported from the Stream Deck app using that
  mapping.
- Because the control API is local-only, Stream Deck control works with no internet
  connection, same as the rest of the app.

## Visual Reference

The developer wants the scoreboard's styling to draw on the World Nine Ball Tour (WNT /
Matchroom Pool) broadcast scoreboard as an additional visual reference, alongside the
overlays.uno Billiards Scoreboard already noted in README.md. Two WNT screenshots have been
reviewed as of 2026-08-14; see NOTES.md ("WNT visual reference — style cues extracted" and
"WNT visual reference: second screenshot adds shooter-indicator and ball-tracker cues") for
detail. Concrete design tokens for the `/overlay/scoreboard` page in Phase 3:

- A single glossy "pill"-shaped bar, capsule-rounded ends, subtle top-lit gradient.
- Deep violet/indigo bar fill (`ColorTheme.HomeBackground`/`AwayBackground`), white bold
  sans-serif text (`ColorTheme.HomeText`/`AwayText`, editable independently per side).
- White rounded-rect score badges with dark, high-contrast numbers, sitting inside the bar
  next to each player's name.
- A slightly darker center segment holding "Race to N" in smaller white text, separating the
  two player halves without a hard divider line.
- End-cap badges are rectangular national flags (slightly rounded corners) on black rounded
  caps at each end — not circular, and not a generic team-sponsor mark.
- Current-shooter indicator: a small right-pointing triangle (▶) next to the center Race-To
  segment, on the side of whichever player is currently shooting — a pointer, not a glow/
  highlight across that player's whole half.
- Ball tracker: a separate white capsule-shaped bar, horizontally centered *below* the main
  score bar, holding small colored circular ball icons (standard ball coloring, white number
  centered) for balls still in play. Note: the WNT reference removes pocketed balls from this
  row entirely rather than greying them out — that does **not** override this project's own
  decision (see "Ball Display Detail" above) that pocketed balls render greyed out in place.
- `ColorTheme.Accent` maps naturally to the score-badge/center-segment treatment.

Flag end-caps are now backed by data: `Player.EndCapIcon` holds whichever logo or flag the
operator picks, served to the overlay from `/overlay/api/players/{id}/icon` rather than embedded
in the state payload. What is still unresolved is nationality as *data* — there is no country
field, so a flag is artwork the operator chose and nothing selects one from a player's name.

This only informs the Overlay's own pages — the Controller's WPF operator console keeps its
existing dark theme (CLAUDE.md's "consistent with the overlay's theme" is a soft aesthetic
alignment, not a pixel match).

## Known Simplifications & Future Work

- Persistence covers the operator profile (`settings.json`: colour theme, scoreboard style, element visibility,
  the shot clock's drawing, both players' setup fields) and the match in progress (`match.json`: scores, game type and Race-To mode, ball
  groups, pocketed balls, shooter), which is restored automatically on the next launch — there is no prompt, so
  RESET MATCH is how an operator discards a resumed match. A finished match is filed to `history.json` only when
  SAVE TO HISTORY is pressed, and the History window lists those newest first with each name's wins and deletes one
  at a time; there is no export, no filter and no bulk clear, the history is capped at 200 matches, and a record is
  per match rather than per player.
- The shot clock's length and its extension are set on Match Setup (40 and 30 seconds by default) and are not part
  of the saved profile, so a relaunch comes back to the defaults — the same way the game type and Race-To mode do.
  The clock itself is not part of `match.json`, so a resumed match shows no clock — a countdown from a previous
  session would be a lie — and there is no pause once it is running: it stops and hides with a reset, a won match or
  the clear route, while a new rack leaves it waiting at the full length for a manual start. The operator's screen says who has an extension left, and greys the button out
  once the player at the table has spent theirs, but the overlay carries nothing about extensions, so a viewer
  cannot see it either. A call with no clock running is still ignored rather than refused: the button stays live
  while the clock is waiting or hidden, and only a spent extension greys it. The clock is either numbers or the line, never both, and the line
  carries no numerals at all — which is the point of it across a room, but means an operator who wants the exact
  time on screen has to leave the display on `Time`.
- `controller.log` is capped by size only, never by age: at startup a log over 1 MiB becomes
  `controller.old.log`, so exactly one previous session survives and older ones are discarded.
- The roster only holds players who have actually been on screen for a started match (or present
  when the window closed). A "Delete" button beside each side's picker drops an entry (and its icon);
  renaming means reusing the name, which updates that entry rather than adding one. The list is capped
  at 100; the match history stores results, not per-player statistics.

## Testing Notes

- Core logic (`GameManager`, models) is UI-free and unit-testable — put tests under
  `PoolScoreboard.Core.Tests` (xUnit or NUnit).
- `PoolScoreboard.Controller.Tests` covers the Controller's file-backed persistence
  (`OperatorSettingsStore` and `PlayerRosterStore`) against a throwaway temp folder. It targets `net8.0-windows` because it
  references the WPF project; the tests themselves touch no UI. `GameViewModelTests` builds the view model through
  its internal wiring constructor with a roster store in a throwaway folder, no dispatcher and no window, so what
  the window shows at startup is assertable: a fresh launch stays on the screen's 9-ball default (a state at setup
  carries the 8-ball enum default, which is not a choice anyone made), and a resumed 8-ball match shows its groups,
  its 8-ball layout and the live view with the restored scores. Window layout, dialogs and live interaction are
  still checked by hand against a real `GameManager`. `OverlayBannerTests` drives the top bar through a fake
  `IOverlaySession`, so no port is bound: the reason the session reports is the banner text, Retry asks the session
  again and clears the banner when it agrees, and a `Stopped` raised on the test thread updates the banner on a real
  dispatcher thread — the hop is asserted by the thread that raised `PropertyChanged`, not assumed.
`StaleControllerTests` covers the leftover rule both ways — a windowless copy of this app is closed, one that still
shows a window is not, and neither is this process or any other program — plus the one-snapshot look itself (a real
window shown on an STA thread, a spawned process with none, and `null` for a PID that cannot be read) and the close,
including the recycled-PID guard. The window is shown without activation on the shared WPF test thread
(`WpfTestThread`, the thread `HelpWindowTests` uses too: WPF allows one `Application` per process, and a window
on a second STA thread stalled inside `Show()` on CI), and the test waits on the window Windows reports rather
than on `Show()` returning — with every call into that thread bounded, so a `Show()` that throws or wedges fails
the test instead of holding the run open. `MatchAutosaveTests` covers the autosave that used to live in `App`: a burst of
points becomes one snapshot, a reset deletes it instead of writing a blank one, the exit snapshot does not wait for
the debounce, changes after dispose are ignored, changes before `Restore()` are not watched (so applying the profile
cannot delete the match about to be resumed), a saved match is restored, a fresh install stays at setup, and an
unreadable file is kept aside without blocking startup. `OverlayHostSessionTests` also covers moving the port: a
failed start is reported on the old port, `SetPort` forgets that reason and the next start serves the new one, and a
serving host is stopped because a listening socket cannot be moved. `GameViewModelTests` covers the field itself: a
usable port rebinds and saves, and one below 1024 snaps back to the port in use. The base address the Help window lists
is computed from that port, so the change announces it too — the first binding to `OverlayBaseUrl` would otherwise keep
naming the old port until something else refreshed it. `OperatorSettingsStoreTests` pins the
other half of persistence — a chosen port survives the round trip through `settings.json`, and a file written before
the port was a setting (no `overlayPort` key) gives `OperatorProfile.DefaultOverlayPort`. `HelpWindowTests` builds
the real Help window at 51241 and reads its 13 URL lines back, asserting every one carries that port and none carries
51234, which is the CI-side twin of the live check's Help step. `OverlayPortSettingsTests` covers the two `App` port
steps with a session that records what it was asked for: the saved port reaches the session (and an unusable one
becomes the default), and a save records the port the session is on, not the one the profile still held. `NoHardCodedOverlayAddressTests`
guards the rule behind all of it: it scans the Controller's own sources — markup as raw text, C# only inside string
literals — and fails on a literal `51234` or on `127.0.0.1:` followed by a digit, so `$"http://127.0.0.1:{Port}/…"`
passes while an address with its port baked in cannot come back. Comments stay out of scope; several name both. `OverlayRouteTests` connects the six places a path is written
down — it reads the routes mapped in `PoolScoreboard.Overlay` and fails on any `/overlay` or `/api` path named in
`HelpWindow.xaml`, either page script or `OverlayHostSession`'s health probe that is not one of them, segment-wise,
so a route's `{...}` and the Help page's `N` match any one segment while everything around it must line up — plus a
second fact that fails if any of those sources stops naming a path, so the guard cannot go quiet. The Stream Deck
mapping is the sixth: `streamdeck/README.md` and `SETUP.md` spell the control endpoints again as full URLs, so
every `/api/control/…` path they name has to be a mapped route — `<N>` and `N` count as the ball-number
placeholder, and `/api/control/*` names the family rather than an endpoint and is skipped — with its own fact
that fails, naming the file, if either doc stops naming one — and the reverse, so a deleted table row cannot
slip through: every mapped `/api/control` route has to be named by one of the two files. Novel:
`OverlayLoopbackBindTests` holds the bind address still: no wildcard (`0.0.0.0`, `AnyIP`, `http://+`, `http://*`,
`[::]`) anywhere in the Overlay, and a string literal handing Kestrel a loopback address — literals rather than raw
text, because the class comment already says "127.0.0.1 only" and a comment must not count. All three guards read
sources through `RepositorySources` (the root walk, the `bin`/`obj` filter, the literal regex), so they scan the same
set. `NoExternalOverlayAssetTests` keeps the overlay offline, which CI had never checked at all: no absolute URL and
no scheme-less `//host` reference in any file under `PoolScoreboard.Overlay/Assets`. A web font or script from a CDN
would otherwise show up only as a half-styled page in OBS on the machine this is built for. The rule is the whole
scheme rather than a list of CDNs, because no asset needs one today; a data-URI SVG's `w3.org` namespace would be a
deliberate, documented exception rather than a silent allowance. `LiveCheckUiTextTests` keeps the live check's
contract with the window: it reads the literals the script hands UI Automation — `Invoke-WindowButton` labels,
`Wait-WindowText` patterns and their `|` alternatives, `NameProperty`/`AutomationIdProperty` lookups — and requires
each to still exist in the Controller. Where it looks matters: control names must *equal* the value of an attribute
that becomes a control name (`Content`, `Text`, `Title`, `Header`, `AutomationProperties.*`, `x:Name`), and
waited-for phrases must appear in a `Text` value or a C# string literal. A first draft that searched the sources for
the word "Retry" passed a mutation that renamed the button, because a `ToolTip` and
`Command="{Binding RetryOverlayCommand}"` still contained it. `PoolScoreboard.Overlay.Tests` is the HTTP level the
source guards cannot reach: 12 tests over the real endpoints, no socket. Every page and asset has to answer 200 with
its content type, the scoreboard and cue-ball payloads are checked against the pages themselves — every `state.…`
path `scoreboard.js`/`cueball.js` reads has to resolve in the JSON, so renaming `PlayerDto.Score` fails with
`home.score, away.score` — every `/api/control` route is checked against the payload the pages render — each score control moves only the side
it names (both sides arranged at one, so a no-op, a wrong side and a flipped sign all differ), undo removes the point
the scoreboard shows last, the shooter control flips `homeIsCurrentShooter` and back, each ball-group control assigns
its group and the complement (after switching to 8-ball, where groups exist), the per-ball toggle pockets and
unpockets, a ball number outside 1–15 is a 400, a new rack clears the tracker but keeps the score, and a reset clears
scores, groups and balls while keeping the chosen game type — the cue-ball contact round-trips through the
property names the page sends and reads, a player's icon comes back as its own bytes under the mime it was stored
with while an unknown player 404s, and each stream's first `data:` frame — the only picture an `EventSource` page ever
gets — carries the same paths that page reads (a payload that resolves on `/state` and not in the frame is still a
blank overlay), and a control call has to push a second frame on the stream that is already open, because that is the
only thing that updates OBS between page loads. Two things that test had to work around: a full
`pack://` URI is only parsed once WPF has registered the scheme, so the theme dictionary is loaded by its relative
`/Assembly;component/` form; and a window's bindings resolve only once its tree is loaded, so the test shows the
window at (-20000, -20000) rather than merely constructing it. `MatchStateStoreTests` pins what a save leaves behind: the
published file opens with no sharing, is exactly the serialized length, parses back with every ball, and leaves no
`.tmp` behind — the state a power cut has to find. The `Flush(flushToDisk: true)` call itself is not observable from
a test, so losing power is still an argument rather than a measurement, unlike a killed process, which was checked
live. `MatchArchiveTests` pins the archive call: a finished match is
  recorded with both sides' names, teams, own targets, scores and winner; a match still in progress or back at
  setup records nothing; the newest is first; and the list drops its oldest at the cap instead of growing.
  `MatchHistoryStoreTests` pins the file the way `MatchStateStoreTests` pins `match.json` — a fresh read gets every
  byte, no `.tmp` is left, earlier matches survive, a match in progress writes nothing, an unreadable file is kept
  aside as `history.corrupt.json`, a file from another version is left in place and ignored, and the cap survives
  the round trip — and that deleting one match keeps the rest and reaches the file. `GameViewModelTests` covers the
  explicit save: a win on its own writes nothing; the button is offered only with a winner; pressing it files the
  match once and a second press files nothing; a match already in the file reads as saved when the view model is
  rebuilt from a resumed state; and starting the next match clears the saved mark. `HistoryWindowTests` reads the
  window back off the screen: three records come out newest first with their scores and winners, the win chips
  read "Bob 2" then "Alice 1", an empty archive shows the placeholder instead of a blank list, and deleting a row
  removes it from the list, recounts the chips from what is left and is gone from the file afterwards — while
  declining the confirmation changes nothing. `MatchArchiveTests` also pins `Standings()` — the
  winner is credited rather than the loser, the most wins come first with names in order within a tie, one player
  across two spellings counts once under the newest spelling, and a nameless side credits nobody.
- Overlay/control-API endpoints should be tested against a `GameManager` test instance, not
  a live one.
- `.githooks/pre-push` mirrors CI (a parse of `tools/verify-live.ps1`, the Release build, then every test project)
  and is enabled per clone
  with `git config core.hooksPath .githooks`. `master` also has a required `build-and-test` check,
  which GitHub only enforces on merges — the hook is what covers direct pushes.
- `tools/verify-live.ps1` is what used to be hand-run in `%TEMP%` and vanished with it, so the four things a unit
  test cannot reach are now repeatable: `port-change` (the OVERLAY PORT field moves the overlay to 51241, the choice
  is saved and a relaunch comes back on it, the modal Help window opens and lists the overlay on 51241 and no longer
  on 51234 — `HelpWindowTests` pins the same text in CI without the dialog — and a foreign listener on the
  *configured* port is named in the banner with Retry recovering), `crash-resume` (score 2–1, kill the process with no `OnExit`, relaunch and
  check the overlay serves 2–1 and the file was not deleted at startup, then RESET deletes it), `banner-retry` (a
  foreign process holds 51234, the window names it and offers Retry, and freeing the port then pressing Retry brings
  the overlay up and clears the banner) and `leftover` (a Controller built from the pre-guard commit `1acddbd` keeps
  the port with a window — it must be left alone and named with the advice; hiding its window and pressing Retry
  then closes it and takes the port). Run `dotnet build` first, then
  `powershell -NoProfile -ExecutionPolicy Bypass -File tools\verify-live.ps1 [-Scenario <name>]`. It backs up and
  restores `%APPDATA%\PoolScoreboard`, stops only the processes it started, needs the port free to begin (`-Port`,
  51234, and `-SecondPort`, 7 above it, for `port-change`), removes its git worktree, and exits non-zero naming the
  assertion that failed. CI cannot run it — it needs the real window and real sockets — so the workflow parses it
  instead (`Parse the live-check script`), naming the file, line and message when it no longer parses; the pre-push
  hook does the same when PowerShell is on PATH and skips it with a note otherwise. Every scenario starts from no saved `settings.json` (the operator's own file is the backup
  restored on exit), so `port-change` has the default to move away from and the others find the app on the port they
  test — without that, `port-change` leaves 51241 saved and the next three all launch a Controller already moved.
  Opening the modal Help window from a script needs two things worth knowing: UIA's `Invoke` on the button that opens
  it does not run the command reliably, so the script raises the window the way the operator does — minimize and
  restore to take the foreground, focus the button, press space — and the dialog is a child of the Controller in the
  UIA tree as often as a sibling of it, so it is looked for in both places. `LiveCheckParametersTests` covers what the
  parse check cannot: the script's `-Port` default is `OperatorProfile.DefaultOverlayPort`, the port-change step moves
  by the offset its own help states and lands on a port `IsUsablePort` accepts, and the scenarios it runs are the ones
  `-Scenario` accepts and the ones this file names.

## Manual Checks

`UAT.md` lists the scenarios that need a person at the screen — window layout, what is filed and what is not,
what survives closing the app — with preconditions, steps and expected results. The match-history scenarios are
MH-1 to MH-14. Add a scenario there when a behaviour worth checking by hand is added, and record the outcome
when one is run.

## Versioning & Commit Policy

Version format while pre-1.0: **`0.<major>.<ui>`**

- **Major functionality updates** (new features, new workflows, significant logic changes)
  bump the middle number: `0.1` → `0.2` → `0.3` ...
- **UI-only updates** (layout, styling, cosmetic/no-behavior-change tweaks) bump the third
  number under the current major: `0.1.1` → `0.1.2` ... Resets when the major bumps.
- Trivial fixes (typos, comments, formatting) fold into the next functional/UI commit.

The version itself lives in `Directory.Build.props` (`<VersionPrefix>`), which stamps every
project; bump it there in the same commit as the change that earns it, and keep it in step with
the `v0.<major>.<ui>-beta` tags.

Commits are **not** auto-pushed in this repo — confirm with the developer before pushing,
same as any other repo, unless they explicitly grant standing authorization for this project.
