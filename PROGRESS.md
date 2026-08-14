# Progress

Tracks what's changed and what's next during development. Newest entries at
the top of each section.

## Current Status

**Scope reset (2026-08-13):** This project is being redirected away from its original
league-based design (APA/USAPL/BCA/TAP with skill-level/Fargo-rating-driven auto Race-To) to
a general-purpose scoreboard: operator-set Race-To (single or split), operator-assigned 8-ball
solids/stripes groups, a current-shooter indicator, a customizable color scheme, OBS overlay +
Stream Deck control, and a separate cue-ball spin-dot overlay — all fully offline. See NOTES.md
("2026-08-13 — Project scope reset") for why, and CLAUDE.md for the target domain model.

**Phases 0-3 are complete (2026-08-13).** The league system is gone, the domain model matches
CLAUDE.md, the Controller has a working match-setup + live-view UI, and the OBS overlay page is
live:

- `PoolScoreboard.Core`: `GameManager`, `GameState`, `Player`, `GameType`, `RaceToMode`,
  `BallGroup`, `ColorTheme` — no league/skill-level/Fargo-rating anywhere. `GameManager` wins
  are decided by comparing each player's score to their own `RaceToTarget`; it also exposes
  `AssignBallGroup`, `SetCurrentShooter`, `PocketBall`/`UnpocketBall`/`ResetBalls`, and
  `SetColorTheme`. It holds one persistent `GameState`/`GameManager` instance per app session
  (recreated only implicitly via `ResetGame`), which matters once Phase 3 hands the same
  instance to the Overlay host.
- `PoolScoreboard.Controller`: `GameViewModel` owns one persistent `GameManager` for the app's
  lifetime and mirrors its `GameStateChanged` events into bindable live-view state (score,
  current shooter, ball groups, pocketed balls). `PlayerViewModel` is now setup-only
  (team/player name, Race-To target). `MainWindow.xaml` has two screens toggled by
  `GameInitialized`: a match-setup screen (game type, Race-To mode + value(s), color theme
  hex fields with swatch previews) and a live view (score +/-, current-shooter toggle, 8-ball
  group-assignment buttons + shared center 8-ball, numeric 1-9/1-10 ball row for 9/10-ball,
  new-rack and reset-match controls). Balls render as colored circular buttons
  (`BallButtonStyle` in `Themes/Default.xaml`) that greyed out in place when pocketed —
  clicking toggles pocketed state through `GameManager.PocketBall`/`UnpocketBall`. Keyboard
  shortcuts (live view only): Q/P score home/away, A undoes the last point, Space toggles
  shooter, N starts a new rack.
- `PoolScoreboard.Overlay`: `OverlayHost.Start(gameManager)` builds and starts a Kestrel
  `WebApplication` bound to `127.0.0.1:51234` only, called from `App.OnStartup` with the same
  `GameManager` the Controller UI's `GameViewModel` is constructed with (`App.SharedGameManager`
  — no second copy of state). `ScoreboardEndpoints` serves `/overlay/scoreboard` (HTML/CSS/JS
  embedded as resources in the assembly, not physical `wwwroot` files, so single-file publish
  in Phase 6 won't need extra copy-output config) plus `/overlay/api/scoreboard/state` (one-shot
  JSON snapshot) and `/overlay/api/scoreboard/stream` (Server-Sent Events — pushes a JSON
  snapshot on every `GameManager.GameStateChanged`, using a per-connection `Channel<string>` so
  the WPF-thread event handler never writes directly to the response stream). The page applies
  the WNT-style design tokens from CLAUDE.md's "Visual Reference" (glossy violet pill, white
  score badges, darker center Race-To segment, circular end-caps) via CSS custom properties set
  from `GameState.ColorTheme` at runtime, plus a ball tracker row (pocketed balls greyed out,
  8-ball split by each player's assigned group) and a current-shooter glow on the active side —
  the shooter/ball-tracker treatment is a reasonable default per NOTES.md, not sourced from a
  screenshot. No control API yet — that's Phase 5. Verified end-to-end (build, `dotnet test`,
  live HTTP requests against a running instance, and a standalone harness confirming SSE pushes
  fire correctly on `InitializeGame`/`AddPoint`/`PocketBall`) since no screenshot tooling is
  available in this environment to eyeball it in an actual browser/OBS source — worth a manual
  visual pass by the developer.
- `dotnet build` is clean across all four projects (0 warnings, 0 errors); `dotnet test` is
  18/18. The Controller was launched and confirmed to start without a runtime binding/XAML
  crash — full visual verification of the live view wasn't done in this pass (no screenshot
  tooling available in this environment); worth a manual pass by the developer.
- No `streamdeck/` profile documentation yet.
- Ball colors are placeholder standard billiard-ball colors (solid fills, no true half-white
  stripe rendering) pending the WNT visual reference (see "Pending: WNT visual reference"
  below).

## Next Steps

### Phase 0: Scope reset — remove the league system, fix the broken Overlay project (DONE)

- [x] Delete `Enums/League.cs` and `Rules/RaceRules.cs` from Core.
- [x] Add `RaceToMode` (Single/Split) and `BallGroup` (Unassigned/Solids/Stripes) enums to Core.
- [x] Rework `Player`: drop `League`/`SkillLevel`; add `RaceToTarget` and `BallGroup`.
- [x] Rework `GameState`: add `RaceToMode`, `CurrentShooter` (rename from `CurrentBreak`),
      `ColorTheme`.
- [x] Rework `GameManager`: drop `RaceRules` dependency; win detection compares score to each
      player's own `RaceToTarget` directly. Add `AssignBallGroup(player, group)` (8-ball only,
      the other player's group auto-flips to the complementary one),
      `SetCurrentShooter(player)`, and `PocketBall`/`UnpocketBall`/`ResetBalls` for the
      pocketed-balls set (see Phase 2).
- [x] Rework `GameViewModel`/`PlayerViewModel`: drop league/game-type-driven skill fields;
      add Race-To mode toggle (single/split) with the corresponding input field(s), and 8-ball
      group-assignment buttons.
- [x] Fix `PoolScoreboard.Overlay.csproj`: restore a working ASP.NET Core project, but
      structured to be hosted **in-process** by the Controller (see CLAUDE.md architecture)
      rather than run as its own executable.
- [x] Confirm `dotnet build` is clean across all three projects before moving to Phase 1.

### Phase 1: Core Tests (DONE)

- [x] Create `PoolScoreboard.Core.Tests` (xUnit) covering:
  - [x] `GameManager` init, scoring, undo/reset, win detection under both Race-To modes
  - [x] Ball-group assignment (assigning one player auto-flips the other; 8-ball excluded)
  - [x] Current-shooter toggling

### Phase 2: Scoreboard UI (Controller) (DONE)

- [x] Match setup screen: game type, Race-To mode + value(s), color theme picker
  (background/accent/text).
- [x] Live view: score +/- per player, current-shooter toggle/indicator, ball display
  (8-ball group-assignment buttons + fixed center 8-ball; numeric 1-9/1-10 row for 9/10-ball),
  new-rack and reset-match controls.
- [x] Pocketed-ball tracking: clicking a ball toggles it pocketed/live; pocketed balls render
  **greyed out** in place (not removed from layout) in both the 8-ball and 9/10-ball displays.
  Independent of 8-ball group assignment (group = which player it's shown under; pocketed =
  is it still on the table).
- [x] Keyboard shortcuts for score +/-, shooter toggle, new rack.

### Pending: WNT visual reference

- [x] Get a screenshot from the developer of the WNT (World Nine Ball Tour / Matchroom Pool)
  score bar — received 2026-08-13 (see NOTES.md, "WNT visual reference — style cues
  extracted"); design tokens recorded in CLAUDE.md's "Visual Reference" section.
- [ ] Get further screenshots showing a current-shooter highlight and the ball-tracker
  graphic — not covered by the one screenshot so far. Not blocking Phase 3 (reasonable
  defaults will be used for those two elements in the interim) but worth another ask.
- [x] Apply the recorded design tokens (violet glossy pill bar, white score badges, center
  Race-To segment, circular end-caps) to the `/overlay/scoreboard` page — done as part of
  Phase 3.

### Phase 3: Overlay (OBS Integration) (DONE)

- [x] Host the Overlay's Kestrel server in-process from the Controller on startup, bound to
  `127.0.0.1` only, sharing the same `GameManager` instance as the UI.
- [x] `/overlay/scoreboard` page: scores, Race-To, shooter indicator, ball display, operator
  colors — no external CSS/JS/font references (must render offline).
- [x] Live updates via polling or SSE/WebSocket (avoid a hard requirement on JS libraries
  pulled from a CDN).

### Phase 4: Cue Ball Spin Overlay

- [ ] `/overlay/cueball` page: cue-ball graphic, click-to-place red contact-point dot.
- [ ] Independent of the scoreboard overlay — separate OBS browser source, separately sized
  and positioned.
- [ ] Persist/clear the dot per shot (decide: manual clear button vs. auto-clear on next
  score change — confirm with developer).

### Phase 5: Stream Deck Integration

- [ ] `/api/control/*` endpoints on the same in-process Kestrel server: score inc/dec,
  shooter toggle, ball-group assignment, new rack, reset match.
- [ ] `streamdeck/` folder: document the button layout and each button's target endpoint,
  so the actual Stream Deck profile can be built in the Stream Deck app from this mapping.

### Phase 6: Packaging

- [ ] `dotnet publish -p:PublishSingleFile=true -p:SelfContained=true` for a distributable
  build that doesn't require a separately installed .NET runtime.

## Known Gaps & Simplifications

- No persistence: match state is in-memory only.
- No player database: names/teams are typed in per match.
- Ball colors in the Controller's live view are placeholder standard billiard-ball colors,
  not yet informed by the WNT visual reference.
- No current-shooter-highlight or ball-tracker WNT screenshot yet, so those two elements on
  `/overlay/scoreboard` use reasonable defaults (accent-colored glow; grouped/greyed ball row)
  rather than a matched design — revisit if the developer supplies more WNT references.
- Keyboard shortcut keys (Q/P/A/Space/N) are a first pass, not confirmed with the developer.

## Change Log

### 2026-08-13 — Phase 3: OBS overlay

- Added `PoolScoreboard.Overlay.OverlayHost` (`OverlayHost.Start(gameManager, port)`): builds a
  minimal `WebApplication`, binds Kestrel to `127.0.0.1:51234` explicitly (never `0.0.0.0`), and
  starts it synchronously via the `IHost.Start()` extension so WPF's `App.OnStartup` doesn't need
  to go `async void`.
- Added `ScoreboardEndpoints.Map`, serving `/overlay/scoreboard`, `/overlay/scoreboard/style.css`,
  `/overlay/scoreboard/app.js` (HTML/CSS/JS read from embedded resources, not physical `wwwroot`
  files — avoids copy-output/single-file-publish complications later), `/overlay/api/scoreboard/state`
  (JSON snapshot), and `/overlay/api/scoreboard/stream` (SSE: subscribes to
  `GameManager.GameStateChanged`, relays each change through a per-connection `Channel<string>`
  so the event-handler thread never touches the HTTP response stream directly, unsubscribes on
  client disconnect via `HttpContext.RequestAborted`).
- Added `PoolScoreboard.Overlay.Models.ScoreboardStateDto`/`ScoreboardStateMapper`: a JSON wire
  contract separate from `Core.Models.GameState`, camelCased via `JsonSerializerDefaults.Web`.
- `App.xaml.cs` now owns `SharedGameManager` (one `GameManager` for the app's lifetime, created
  before `OnStartup` runs) and starts/stops the Overlay host in `OnStartup`/`OnExit`.
  `GameViewModel`'s constructor now takes a `GameManager` instead of `new`-ing its own, and
  `MainWindow` passes `((App)Application.Current).SharedGameManager` — the Controller UI and the
  Overlay page now read/write the exact same game state, per CLAUDE.md's "never a second copy of
  state" rule.
- Built `/overlay/scoreboard`'s look from the WNT design tokens recorded in CLAUDE.md/NOTES.md:
  glossy violet pill bar (gradient computed at runtime from `ColorTheme.Background` via a small
  JS shade() helper, so any operator-chosen color still gets a lit-top/darker-bottom gradient),
  white rounded score badges with dark digits, a darker center "RACE TO N" segment (or "N / M"
  in Split mode), black circular end-caps with an accent-colored dot placeholder, a ball tracker
  row (home group — 8 — away group for 8-ball; numeric 1-9/1-10 row otherwise) with pocketed
  balls greyed out, and a current-shooter glow on the active side — the shooter-highlight and
  ball-tracker look aren't from a screenshot (none supplied yet), so these are reasonable
  defaults, revisable once more WNT references arrive.
- `dotnet build` clean across all four projects; `dotnet test` 18/18 (no new tests needed — no
  Core logic changed). Verified live: launched the Controller, confirmed `GET
  /overlay/scoreboard` and its CSS/JS return 200, confirmed `127.0.0.1`-only binding, and used a
  standalone harness (Core + Overlay only, no WPF) to confirm the SSE stream actually pushes a
  fresh JSON snapshot on `InitializeGame`, `AddPoint`, and `PocketBall` — not just on connect.
  Full visual QA in an actual browser/OBS browser source is still owed (no screenshot tooling in
  this environment).

### 2026-08-13 — Phase 2: Scoreboard UI

- `GameManager` gained `SetColorTheme`; `GameViewModel` now creates one `GameManager` in its
  constructor and keeps it for the app's lifetime (previously a new instance was created on
  every match start, which would have broken the single-shared-instance assumption Phase 3
  needs for the Overlay host).
- `PlayerViewModel` trimmed to setup-only fields (team/player name, Race-To target); live
  state (score, ball group, current shooter, pocketed balls) now lives on `GameViewModel`,
  mirrored from `GameManager.GameStateChanged` — the UI no longer keeps a second, disconnected
  copy of game state.
- Added `BallItemViewModel` (number, color, pocketed) and a `BallButtonStyle` (circular,
  colored, greys out via a `DataTrigger` on `IsPocketed`) shared by the 8-ball group displays,
  the center 8-ball, and the 9/10-ball numeric row.
- `MainWindow.xaml` now has two screens toggled by `GameInitialized`: match setup (game type,
  Race-To mode, player fields, color theme hex fields with swatch previews, Start Match) and
  live view (score +/-, current-shooter toggle, ball-group buttons, ball display, new-rack /
  reset-match). Added `InverseBooleanToVisibilityConverter` and `HexColorToBrushConverter`.
- Added live-view-only keyboard shortcuts in `MainWindow.xaml.cs`: Q/P score home/away, A
  undoes the last point, Space toggles shooter, N starts a new rack (guarded against
  `TextBox` focus and inactive outside the live view).
- `dotnet build` clean across all four projects; `dotnet test` 18/18. Launched the Controller
  to confirm it starts without a runtime binding/XAML crash — full visual QA of the live view
  is still owed since no screenshot tooling was available in this pass.

### 2026-08-13 — Phase 1: Core unit tests

- Added `PoolScoreboard.Core.Tests` (xUnit), referenced from and added to the solution.
  17 tests cover `GameManager` initialization, scoring (single and split Race-To modes),
  win detection, undo/reset, current-shooter toggling (including the no-op after a game
  ends), 8-ball ball-group assignment (auto-flip to the complementary group, no-op outside
  8-ball), and pocketed-ball tracking. `dotnet test` passes 17/17; `dotnet build` is clean
  across all four projects.

### 2026-08-13 — Phase 0: league system removed, domain model reworked

- Deleted `Enums/League.cs` and `Rules/RaceRules.cs`. Added `Enums/BallGroup.cs` and
  `Models/ColorTheme.cs`. Reworked `Player` (`RaceToTarget`/`BallGroup` replace
  `League`/`SkillLevel`), `GameState` (`RaceToMode`, `CurrentShooter`, `ColorTheme`,
  `PocketedBalls`), and `GameManager` (win detection off each player's own `RaceToTarget`;
  added `AssignBallGroup`, `SetCurrentShooter`, `PocketBall`/`UnpocketBall`/`ResetBalls`).
- Reworked `GameViewModel`/`PlayerViewModel` and `MainWindow.xaml`/`.xaml.cs`: dropped
  league/skill-level/Fargo-rating UI, added Race-To mode toggle (single mode mirrors Home's
  target to Away), 8-ball ball-group buttons, and a current-shooter toggle in place of the old
  at-table toggle.
- Fixed `PoolScoreboard.Overlay.csproj`: `Microsoft.NET.Sdk.Web` with `OutputType=Library`
  (it has no `Main` — hosted in-process by the Controller) and a `ProjectReference` to Core.
- `dotnet build` is clean across all three projects (0 warnings, 0 errors).

### 2026-08-13 — Scope reset

- Discarded the league-based design (APA/USAPL/BCA/TAP, skill level, Fargo rating) in favor
  of a general-purpose, operator-controlled scoreboard. Rewrote CLAUDE.md, NOTES.md,
  README.md, and this file to reflect the new target design and the Phase 0 rework needed to
  get the existing code there. No source code changed yet — see Phase 0 above.

### v0.0 — 2026-07-12 (superseded)

- Original project setup: PoolScoreboard.sln with three projects (Core, Controller, Overlay).
- League-based Core game logic (`GameManager`, `RaceRules`, `Player`, `GameState`).
- Documentation (CLAUDE.md, NOTES.md, PROGRESS.md, README.md) — since superseded by the
  2026-08-13 reset above.
- (Not previously logged here, found via `git log`: manual Race-To entry replacing
  auto-calculation, and an MVVM Controller UI foundation — both still league-aware and in
  scope for Phase 0 rework.)
