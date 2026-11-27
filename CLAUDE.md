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
- `README.md` / `CLAUDE.md` / `NOTES.md` / `PROGRESS.md` — project docs

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
  overlay's contact point, or null), active/winner status.
- `ColorTheme` — operator-set background/accent/text colors, applied to the overlay pages.
- No league, skill level, or Fargo rating anywhere in this model.

## Ball Display Detail

- Every ball display (8-ball's grouped layout and the numeric 9/10-ball row) supports
  marking individual balls **pocketed**: a pocketed ball renders **greyed out** in place
  (not removed/collapsed from the layout), so the operator and viewers can see at a glance
  what's left on the table.
- `GameManager.PocketBall(ballNumber)` / `UnpocketBall(ballNumber)` toggle membership in
  `GameState.PocketedBalls`; `ResetBalls()` clears it for a new rack.
- This applies independently of `BallGroup` assignment in 8-ball — a ball's group (which
  player it's displayed under) and its pocketed state are separate flags.

## Architecture & Key Files

### PoolScoreboard.Core (Game Logic)

- `GameManager` — orchestrator for all game state and operations: `InitializeGame`,
  `AddPoint`/`SubtractPoint`, `UndoPoint`, `ToggleCurrentShooter`, `AssignBallGroup`, `ResetGame`.
  Raises `GameStateChanged` after every mutation.
- No database, no async I/O — pure, fast, unit-testable.
- `Persistence/OperatorProfile` (capture/apply of the saved profile), `Persistence/PlayerRoster`
  (saved-player identity — a name, ignoring case — ordering and the 100-entry cap),
  `Persistence/EndCapIconData` (data-URI ↔ icon bytes) and `Persistence/ResumableMatch` (the part of a match in
  progress worth surviving a crash: scores, game type and Race-To mode, ball groups, pocketed balls, shooter,
  with a file version) live here too, deliberately I/O-free so all four are unit-tested.
  `GameManager.RestoreMatch` puts one back atomically — floors scores at zero, drops ball numbers off the
  table, clears the undo history (the points it would undo are no longer on screen) and recomputes the winner
  from the scores rather than trusting the file.

### PoolScoreboard.Controller (WPF Desktop Application)

- MVVM: ViewModels for match setup, player setup, and the live game view.
- Match setup: game type, Race-To mode (single/split) and value(s), color theme picker.
- Live view: score +/- per player, current-shooter toggle, ball-group assignment buttons
  (8-ball only) or ball display (9/10-ball), new-rack/reset-match controls.
- On startup, starts the Overlay's Kestrel host in-process, passing it the same
  `GameManager` instance the UI is bound to. `OverlayHostRunner.Start` returns null when the host cannot bind
  (  `127.0.0.1:51234` already taken, usually a leftover Controller) and logs the reason, so startup continues
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
  closing it would end their window mid-match — the banner names it and the operator closes it when ready. Never
  this process, and never a program that is not this app. `IOverlaySession` is what the window depends on, so the
  banner and Retry can be tested without a socket.
- Only one Controller runs per desktop session (`SingleInstanceGuard`): a named mutex elects the first process,
  and a second launch sets a named event so the running window comes to the front, logs that it is exiting and
  shuts down before restoring or saving the profile — a duplicate must never write defaults over the running
  instance's `settings.json`/`roster.json`.
- `Persistence/OperatorSettingsStore` reads/writes `%AppData%\PoolScoreboard\settings.json` plus the
  end-cap icon files; `App` applies the saved profile before the window exists, then saves on a
  750 ms debounce and on exit.
- `Persistence/MatchStateStore` keeps the match in progress in `match.json`: `App` subscribes to
  `GameStateChanged` and re-arms a pool timer, so a burst of points costs one small write 500 ms later (the
  event arrives inside `GameManager`'s lock, often from an HTTP control thread, and the write never runs on the
  UI thread); a final snapshot is written in `OnExit`, and a state back at setup deletes the file so a reset
  match is not resumed. Restore happens at startup after the profile and before the window.
  `GameViewModel` therefore takes the game type and Race-To mode from state — a resumed 8-ball match shows its
  groups — and fills the live view from the restored snapshot, since no event fires until the operator touches
  something. An unreadable `match.json` is moved to `match.corrupt.json`; one written by another version is
  left in place and ignored.
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

- `/overlay/scoreboard` — browser-source page: scores, Race-To, shooter indicator, ball
  display, operator colors. Read-only; polls or subscribes (SSE/WebSocket) for state.
- `/overlay/cueball` — separate browser-source page: a cue-ball graphic; clicking it places
  a red dot marking the spin/contact point. Independent of and separately positionable from
  the scoreboard overlay in the OBS scene.
- `/api/control/*` — local HTTP endpoints for Stream Deck buttons (add a point per side, undo
  the last point, shooter toggle, ball assignment, per-ball pocketed toggle, new rack, reset
  match). Same `GameManager` instance as the UI.
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
  both players' setup fields) and the match in progress (`match.json`: scores, game type and Race-To mode, ball
  groups, pocketed balls, shooter), which is restored automatically on the next launch — there is no prompt, so
  RESET MATCH is how an operator discards a resumed match. There is no match history: a finished match is not
  kept anywhere.
- `controller.log` is capped by size only, never by age: at startup a log over 1 MiB becomes
  `controller.old.log`, so exactly one previous session survives and older ones are discarded.
- The roster only holds players who have actually been on screen for a started match (or present
  when the window closed). A "Delete" button beside each side's picker drops an entry (and its icon);
  renaming means reusing the name, which updates that entry rather than adding one. The list is capped
  at 100, and there is no match history: scores are never stored per player.

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
shows a window is not, and neither is this process or any other program — plus the window check itself (a real
window shown on an STA thread, a spawned process with none) and the close.
- Overlay/control-API endpoints should be tested against a `GameManager` test instance, not
  a live one.
- `.githooks/pre-push` mirrors CI (Release build, then every test project) and is enabled per clone
  with `git config core.hooksPath .githooks`. `master` also has a required `build-and-test` check,
  which GitHub only enforces on merges — the hook is what covers direct pushes.

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
