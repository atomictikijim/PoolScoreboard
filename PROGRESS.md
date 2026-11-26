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
  `SetPlayerDetails` (the setup screen's writes), `ToggleCurrentShooter`, `AssignBallGroup`,
  `PocketBall`/`UnpocketBall`/`ResetBalls`, `SetColorTheme`, and the per-element visibility
  setters. Every mutation runs under one internal lock that raises `GameStateChanged` with a
  snapshot, so deliveries arrive in mutation order. One `GameManager` lives per app session; its
  `GameState` is replaced only when a match starts or is reset, and both keep the players and the
  operator's settings. Phase 3 hands the same instance to the Overlay host.
- `PoolScoreboard.Controller`: `GameViewModel` owns one persistent `GameManager` for the app's
  lifetime and mirrors its `GameStateChanged` events into bindable live-view state (score,
  current shooter, ball groups, pocketed balls). `PlayerViewModel` is setup-only and holds no
  `Player` of its own: its team name, player name, Race-To and end-cap icon fields write straight
  into `GameManager` state, so the overlay previews them as they're typed. `MainWindow.xaml` has
  two screens toggled by `GameInitialized`: a match-setup screen (game type, Race-To mode +
  value(s), color theme hex fields with swatch previews) and a live view (score +/-, current-shooter
  toggle, 8-ball group-assignment buttons + shared center 8-ball, numeric 1-9/1-10 ball row for
  9/10-ball, new-rack and reset-match controls). Balls render as colored circular buttons
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
  screenshot. Verified end-to-end at the time (build, `dotnet test`, live HTTP requests against a
  running instance, and a standalone harness confirming SSE pushes fire correctly on
  `InitializeGame`/`AddPoint`/`PocketBall`); the pages have since been checked in a real browser
  and the Controller driven through its UI — see the 2026-11-26 entries below.
- `dotnet build` is clean across all four projects (0 warnings, 0 errors); `dotnet test` is 47/47.
  The Controller starts without a runtime binding/XAML crash, and both of its screens have since
  been driven through UI Automation and screenshotted (again, see the 2026-11-26 entries).
- `streamdeck/` documents the button-to-endpoint mapping (Phase 5) and the packaging procedure
  (Phase 6). There is still no exported `.streamDeckProfile` in the repo — by design, see
  `streamdeck/README.md`.
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
- [x] Get further screenshots showing a current-shooter highlight and the ball-tracker
  graphic — received 2026-08-14 (see NOTES.md, "WNT visual reference: second screenshot adds
  shooter-indicator and ball-tracker cues"); tokens recorded in CLAUDE.md.
- [x] Apply the recorded design tokens (violet glossy pill bar, white score badges, center
  Race-To segment, circular end-caps) to the `/overlay/scoreboard` page — done as part of
  Phase 3.
- [x] Revisit `/overlay/scoreboard`'s shooter-indicator (triangle pointer, not a glow) and
  ball-tracker (separate pill below the bar, not embedded) against the newly recorded tokens —
  done 2026-08-14 as part of "Configurable overlay styling + live show/hide" below: the triangle
  pointer is now a selectable mode (`ShooterIndicatorStyle.Triangle`/`Glow`/`Both`), not the only
  option, since the operator-configurability request (below) made the WNT look one of several
  presets rather than a forced replacement.

### Phase 3: Overlay (OBS Integration) (DONE)

- [x] Host the Overlay's Kestrel server in-process from the Controller on startup, bound to
  `127.0.0.1` only, sharing the same `GameManager` instance as the UI.
- [x] `/overlay/scoreboard` page: scores, Race-To, shooter indicator, ball display, operator
  colors — no external CSS/JS/font references (must render offline).
- [x] Live updates via polling or SSE/WebSocket (avoid a hard requirement on JS libraries
  pulled from a CDN).

### Bundled end-cap icon set (DONE)

- [x] Shipped a built-in icon set alongside the existing custom-file "Choose Icon..." picker: 249
  country/territory flags ([lipis/flag-icons](https://github.com/lipis/flag-icons), MIT) and 56 US
  state/territory/DC flags ([nibsbin/us-state-flags-svg](https://github.com/nibsbin/us-state-flags-svg))
  — see NOTES.md for full licensing detail and implementation decisions. Additive: the custom file
  picker and Clear button are unchanged.
- [x] New "Choose Flag..." button per player opens `FlagPickerWindow` — a modal thumbnail grid
  (SharpVectors' `SvgViewbox`), grouped STATES-then-COUNTRIES, alphabetical within each group.
  Picking a tile reuses the exact same `Player.EndCapIcon`/live-preview pipeline as the custom file
  picker (`data:image/svg+xml;base64,...`) — no Core/DTO/Overlay changes needed.
- [x] Added the `SharpVectors.Reloaded` NuGet package (WPF-native SVG rendering, build-time/offline
  dependency only) since WPF can't render SVG natively; extended
  `DataUriToImageSourceConverter` to branch on MIME type so the existing preview swatch keeps
  working for both picker-sourced SVG icons and file-picker-sourced raster icons.

### Phase 4: Cue Ball Spin Overlay (DONE)

- [x] `/overlay/cueball` page: cue-ball graphic (pure CSS radial-gradient sphere, no external
  image), click-to-place red contact-point dot.
- [x] Independent of the scoreboard overlay — its own route, own embedded HTML/CSS/JS, own
  SSE stream (`/overlay/api/cueball/stream`) — a separate OBS browser source, separately sized
  and positioned from `/overlay/scoreboard`.
- [x] Persist/clear the dot per shot: **both** auto-clear on the next score change and a manual
  "Clear" button on the page itself (developer's choice, see Change Log) — `AddPoint` clears
  `GameState.CueBallSpin` unconditionally before applying the score, and the page's own "Clear"
  button POSTs to `/overlay/api/cueball/clear` for an early reset (e.g. after a foul).

### Phase 5: Stream Deck Integration (DONE)

- [x] `/api/control/*` endpoints on the same in-process Kestrel server: score inc/dec,
  shooter toggle, ball-group assignment, new rack, reset match.
- [x] `streamdeck/` folder: document the button layout and each button's target endpoint,
  so the actual Stream Deck profile can be built in the Stream Deck app from this mapping.

### Phase 6: Packaging (DONE)

- [x] `dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true`
  for a distributable build that doesn't require a separately installed .NET runtime.

## Known Gaps & Simplifications

- No match persistence: scores, ball groups and pocketed balls are in-memory only. The operator
  profile — colour theme, scoreboard style, element visibility and both players' name/team/Race-To/
  end-cap icon — is saved to `%AppData%\PoolScoreboard\settings.json` and restored on launch.
- The roster only remembers players who were on screen for a started match (or still there when the
  window closed). "Delete" beside a picker drops an entry and its icon; renaming means reusing the name,
  and the list is capped at 100. No match history: no scores are stored against a player.
- Ball colors in the Controller's live view are placeholder standard billiard-ball colors,
  not yet informed by the WNT visual reference.
- Flag end-caps are artwork ("Choose Flag..." bundles country and US-state SVGs) stored as the
  player's `EndCapIcon`, now saved and restored with the player. There is still no nationality/flag
  property on `Player`, so nothing chooses a flag from a player's name.
- Settings are one flat profile in `%AppData%`: there is no per-machine/per-profile selection, and
  the icon files are replaced rather than versioned, so a cleared "Choose Icon" deletes the file.
- Keyboard shortcut keys (Q/P/A/Space/N) are a first pass, not confirmed with the developer.

## Change Log

### 2026-11-26 — Trace output goes to a log file

- `Diagnostics/TraceLogFile` is a `TraceListener` that appends to `%AppData%\PoolScoreboard\controller.log`,
  opening with a timestamped "Controller started" line so runs can be told apart. `App` attaches it first in
  `OnStartup` and disposes it last in `OnExit`, which is what puts the overlay host's give-up warnings
  somewhere readable after the process is gone. Writes are locked and flushed per line, since the last
  warning is raised from a background thread during shutdown.
- It never throws: an unopenable log (permissions, or a folder sitting where the file should be) leaves the
  listener attached but silent, and a failed write loses that line rather than the app. Logging is
  diagnostics — it must not be able to break startup or exit.
- Four tests (`TraceLogFileTests`) write real files in a temp folder: a `Trace.TraceWarning` reaches the
  file with its timestamped start line, a second session appends instead of truncating, disposing both
  stops listening and releases the handle (the file can be deleted), and an unopenable path stays quiet.
- Verified in the running Controller: `controller.log` is created on startup with the session header and is
  closed cleanly at exit. The warning path itself is covered by the unit tests, not triggered live — forcing
  sealed-host failure needs a hung host, which is exactly what the give-up path exists to avoid in production.
- Build 0 warnings; 135/135 tests (93 Core + 42 Controller).
- Known gap: the log is append-only with no rotation or size cap.

### 2026-11-26 — Deleting saved players, and a trace when the overlay host outlives its timeout

- `PlayerRoster.Forget(id)` returns whether an entry went, and each side's picker gained a **Delete**
  button (one `ForgetSavedPlayerCommand` taking the picked entry as its parameter) that drops the entry,
  rewrites `roster.json` and lets the store's existing pruning remove that player's icon file. Deleting
  does not clear the setup fields, so a mis-picked player is not wiped mid-prep; the entry only returns
  to the roster if a match starts with that name again.
- `OverlayHostRunner.StopAndDispose` now writes `Trace.TraceWarning` when the host misses its timeout,
  and when the stop throws (including the exception message), so a shutdown that gives up is no longer
  silent. Tests capture `Trace` output through a listener and assert both messages.
- `PlayerRosterStore.TrySave` (and both roster writes, at match start and at close) tolerates a locked or
  full disk, matching the settings store: the entry still leaves the picker for that session, a failed
  write traces a warning, and clicking Delete can no longer crash the app. The test blocks the write by
  turning `roster.json` into a directory and asserts `false` plus the warning instead of an exception.
- Restored `SavedPlayerOption.ToString()`: the ComboBox's item template reads the item's text from it, so
  without the override the picker listed `SavedPlayerOption { Id = …, … }` instead of the player's name.
  The previous commit removed it as dead code; the running app showed it was load-bearing.
- Verified in the running Controller: seeded two players (one with a PNG icon), selected the second in
  the HOME picker and pressed Delete — it disappeared from `roster.json` and from the picker; deleted the
  remaining player and their `roster-*.png` went too while the unrelated `home.png` stayed.
- Build 0 warnings; 130/130 tests (93 Core + 37 Controller).

### 2026-11-26 — The Controller now exits when its window closes

- Cause: `App.OnExit` stopped the in-process overlay host on the WPF UI thread with
  `StopAsync(2s).GetAwaiter().GetResult()`. ASP.NET Core's shutdown path awaits without
  `ConfigureAwait(false)`, so its continuations — including the one that would honour the two-second
  timeout — were posted to WPF's `SynchronizationContext` while the same thread sat blocked inside
  `OnExit`. The stop therefore never completed: the server had already stopped listening, which is why
  the port looked closed, but the process stayed alive with ~58 threads until killed. Instrumented
  `OnExit` marked "stopping host" and never reached the other side.
- Fix: `OverlayHostRunner` starts and stops the host on the thread pool, where there is no UI context to
  capture, and bounds the stop at the requested timeout plus a second — giving up rather than hanging
  shutdown, since the abandoned stop runs on a background thread that cannot keep the process alive.
- Tests: three in `PoolScoreboard.Controller.Tests`. A host whose shutdown captures the ambient context
  (with a stalled `SynchronizationContext` standing in for a blocked Dispatcher) stops successfully; a
  host that never stops makes the call return false within the bound; a throwing stop returns false.
  Reverting `StopAndDispose` to the old blocking shape makes the first test hang and take the test host
  down with it, so the regression is genuinely covered.
- Verified in the running Controller: closing the window exits the process about two seconds later, the
  overlay port is free, and `settings.json`/`roster.json` are written at close.
- Build 0 warnings; 123/123 tests (89 Core + 34 Controller).

### 2026-11-26 — Roster review: names can't duplicate, unused API removed

- `Remember` now removes *every* entry matching the name before inserting the updated one. Repeated
  remembers (START MATCH, then the window closing) were already idempotent — the match kept the entry
  and its id — but a roster file that already held the same name twice, hand-edited or written before
  the rule existed, would have kept the stale copy forever.
- Removed what nothing used: `PlayerRoster.Find`/`Forget`/`Search`, `PlayerRosterStore.ForgetIcon`
  (now private) and its `RootFolder` property, `SavedPlayerOption.ToString`, and the roster JSON's
  unread `version` field. Their tests went with them; one test was added for the duplicate collapse.
- Verified in the running Controller: typed both sides without starting a match and closed the
  window — `roster.json` appeared only at that point, with both players, so the `Closed` handler is
  what wrote it; a second remember (START MATCH on the restored roster) left the file identical in
  content, ids and order.
- Found while checking, and left alone as a separate issue: the process does not exit after its window
  closes. `OnExit` runs (both JSON files are written at close time) and the overlay port stops
  answering, but the process lingers with ~58 threads until killed. Pre-existing — the same thing was
  seen earlier in the session before the roster existed.
- Build 0 warnings; 120/120 tests (89 Core + 31 Controller).

### 2026-11-26 — A saved player roster instead of only the last two players

- `PlayerRoster` (Core, `Persistence/`) is the saved-player list: `Remember` adds or updates a player
  and moves them to the front, matching on name ignoring case and surrounding space so a returning
  player keeps one entry (and one icon) when their team or Race-To changes. Blank names are ignored,
  entries keep their id when updated, and the list is capped at 100. `Find`, `FindByName`, `Forget`
  and `Search` (name or team, case-insensitive, recency order) round it out — all pure and unit-tested.
- `PlayerRosterStore` (Controller) writes `roster.json` plus `icons/roster-{id}.{ext}` per player.
  Icons are read lazily, one at a time, so picking a player decodes only that player's artwork; entry
  ids are sanitised before use as file names, the mime is re-validated on load, and a forgotten player
  (or a hand-copied leftover file) loses its artwork on the next save.
- Match Setup gained a "Saved Player" dropdown per side. `GameViewModel.RememberPlayers` writes both
  sides (with their current icon) into the roster when a match starts and when the window closes;
  picking an entry fills name, team, Race-To and the remembered icon in a single push to state.
- Tests: 12 in Core for the roster rules and 15 in `PoolScoreboard.Controller.Tests` for the store
  (round trip, per-player icons, mime change, clearing, pruning, orphan files, missing/corrupt file,
  path-escape and non-image mime, cap, temp file). 121/121 overall, 0 warnings.
- Verified in the running Controller: typed both sides, started a match, saw `roster.json` list Bob
  then Alice; relaunched, both dropdowns listed "Bob - Red Rackers" and "Alice - Blue Cuers"; picking
  Bob filled his name/team, and picking Alice restored her name, team, Race-To 7 and a served icon
  matching the saved 16,877-byte PNG byte for byte.

### 2026-11-26 — Cover the settings store in CI, plus review fixes for the gate commit

- Added `PoolScoreboard.Controller.Tests` (`net8.0-windows`, references the WPF project, no UI in the
  tests) with 16 tests over `OperatorSettingsStore` against a throwaway temp folder: full profile and
  both icons round-tripping, icons written as files with no base64 in the JSON, SVG mime preserved,
  an unchanged icon not rewritten, a mime change replacing the old file, clearing removing it,
  unusable data treated as no icon, no `.tmp` left behind, missing/corrupt/empty settings, a missing
  icon file, a hand-edited `../escape.png` path and a `text/html` mime both ignored, overwriting, and
  `ApplyTo` writing the loaded profile into a real `GameManager`.
- The CI test step and the pre-push hook now run `dotnet test PoolScoreboard.sln`, so the new project
  is gated in both places. `InternalsVisibleTo` is what lets the tests reach the internal store.
- Review of `9339fc4` caught two smaller things: `HasSameBytes` swallowed only `IOException`, so a
  file locked by another process threw `UnauthorizedAccessException` out of the save instead of being
  treated as "different, rewrite"; and `EndCapIconData.TryParse` accepted a mime with stray spaces,
  which would have produced a data URI the browser rejects. Both fixed, the latter with a test.
- Build 0 warnings; 92/92 tests (76 Core + 16 Controller).

### 2026-11-26 — Block a red build at push time, and harden icon persistence on review

- GitHub cannot reject a *direct push* on a check that runs after that push, so the required
  `build-and-test` check only gates merges. `.githooks/pre-push` now runs the same Release build and
  Core tests as CI before anything leaves the machine; enabled per clone with
  `git config core.hooksPath .githooks`, bypass with `git push --no-verify`. Verified all three ways:
  green run exits 0, `git hook run pre-push` finds it, and a deliberately broken `Core` file exits 1
  with `BUILD FAILED` before tests run.
- Review found `WriteIcon` deleted the previously saved icon file *before* validating the new data
  URI, so one unparseable icon would destroy a good one. It now writes the new bytes first and prunes
  stale files afterwards, and skips the write when the bytes are unchanged.
- `ReadIcon` used the JSON's file name verbatim; it now goes through `Path.GetFileName` and
  re-validates the mime as `image/*`, so a hand-edited settings file cannot point the loader outside
  the icons folder or have a non-image type echoed back into a data URI. Confirmed in the running app:
  a tampered `"../escape.png"` and a `text/html` icon both load as no icon while the rest of the
  profile still restores.
- The data-URI parsing moved from the Controller to `Core/Persistence/EndCapIconData`, where it is
  I/O-free and covered by 19 new test cases (round trips, missing marker, malformed base64, non-image
  mime, extension mapping). Build 0 warnings, 76/76 tests.
- Re-verified the whole save/load path afterwards: seeded profile with a PNG icon → app served the
  original 16,877 bytes, and a graceful exit re-saved the JSON still pointing at `home.png`.

### 2026-11-26 — Remember the operator's look and the last two players between runs

- `OperatorProfile` (Core, `Persistence/`) is the saveable slice of a state snapshot: `ColorTheme`,
  `ScoreboardStyle`, element visibility and both players' name/team/Race-To. `Capture` reads it from
  a snapshot, `ApplyTo` writes it back through `GameManager`'s mutation methods so the overlay sees
  restored values by the same path as operator edits. Scores, ball groups and pocketed balls stay
  per-match and are deliberately not saved.
- `OperatorSettingsStore` (Controller) writes `%AppData%\PoolScoreboard\settings.json` and keeps
  end-cap icons as files under `icons/`. Icons stay out of the JSON on purpose: a logo can be ~1.5 MB
  of base64 while the settings file is rewritten whenever a colour or a name changes. Writes go to a
  temp file and are moved into place, and an unreadable/unparseable file is ignored rather than
  blocking startup.
- Saves are debounced 750 ms (`App.RequestProfileSave`) and flushed on exit, so dragging a slider or
  typing a name produces one write, not one per keystroke.
- `App` restores the profile into `SharedGameManager` before any window exists; `GameViewModel` and
  `PlayerViewModel` then seed their fields from that state with their change handlers suppressed,
  since those values came out of state and pushing them back would be a burst of no-op mutations.
  Game type and Race-To mode remain per-match choices and are not saved.
- Verified against the running Controller, not just unit tests: set names/teams/colours and toggled
  Score Bar off from the setup screen and confirmed `settings.json` matched; relaunched and got back
  both names and teams, Race-To 7/5, all six colours, roundness 40 / scale 130 / glossy off / Both
  shooter indicator, `scoreBarVisible: false`, and a served end-cap icon URL whose bytes are the
  original 16,877-byte PNG. Toggling visibility again rewrote the JSON and kept the icon. Build 0
  warnings, 57/57 tests (3 new).

### 2026-11-26 — Normalise names once, in Core, instead of in every consumer

- Names are now trimmed in `SetPlayerDetails` — the single path operator-typed names take into
  state — so a spaces-only name is stored as `""`. Before this, three surfaces each decided for
  themselves what blank meant (overlay pills, winner banner, Controller winner line) and two of
  them got it wrong: both bugs found in the previous entries were the same missing rule.
- Consumers keep only their display fallback, which is presentation rather than normalisation:
  the overlay's `displayText` lost its `.trim()`, its team-name lines lost their trim, and the
  Controller's check became `IsNullOrEmpty` instead of `IsNullOrWhiteSpace`.
- The rule is pinned by a Core test (`SetPlayerDetails_TrimsNamesAndStoresWhitespaceOnlyAsEmpty`),
  which is the only place in the solution with a harness — the JS and the ViewModel have none, so
  a rule living there was untestable by construction.
- Deliberately unchanged: `Player.Name` itself is not trimmed, so a future writer that sets it
  directly would bypass the rule; and the HOME/AWAY fallback strings stay in the UI, because state
  should record what the operator typed, not a display placeholder.
- Verified live: three spaces typed into the home Player Name field now arrive in state as `""`,
  and the overlay shows "HOME"/"AWAY" pills with "HOME WINS!". Build clean, 53/53 tests.

### 2026-11-26 — Whitespace-only names fall back like empty ones on the overlay

- The name pills used `state.home.name || 'HOME'`, which treats a name of only spaces as present, so
  the pill rendered blank; the winner label went through the same check, so a race-to win by a
  spaces-only name read " WINS!" instead of a side. Both now share one `displayText(value,
  fallback)` helper that trims before deciding, giving HOME/AWAY pills and "HOME WINS!" /
  "AWAY WINS!" — the same fallback the Controller applies with `IsNullOrWhiteSpace`.
- Team names are trimmed as well, so a spaces-only team name renders nothing instead of blank
  layout space above the score.
- Verified live with the home player's name set to three spaces: state carried `'   '` in both
  `home.name` and `winnerName`, and the overlay showed "HOME"/"AWAY" pills with "HOME WINS!".
  (The Core does not trim names, so this state really is reachable from the setup screen.)

### 2026-11-26 — Controller winner line falls back to the side, never a blank name

- `GameViewModel.WinnerAnnouncement` was `$"{state.Winner.Name} wins!"`, so with the name fields
  left blank the live view read " wins!". It now mirrors the overlay's fallback: an empty or
  whitespace-only name renders the winner's side ("Home wins!" / "Away wins!"), chosen with
  `ReferenceEquals(winner, state.Player1)` against the snapshot's own player instances.
- Also corrected the comment above `GameInitialized`, which still said a finished match sets
  `IsGameActive` false — since the origin/master merge a win only sets the winner.
- Verified in the running Controller with both names blank: "Home wins!" for a home win,
  "Away wins!" after the scores were moved to the other side, and no line at all once the winning
  point was subtracted again.
- Build clean, 52/52 tests. The view model has no automated coverage — this check was live, not
  unit-tested.

### 2026-11-26 — Winner banner shows for players left unnamed

- The overlay gated the banner on `if (state.winnerName)`, but the mapper sends `""` — not null —
  for a winning player with no name. A race-to win with both name fields left empty therefore set
  the winner in state and painted nothing, which reads as the banner being broken. Presence is now
  decided by null-ness (`state.winnerName != null`; the DTO only sends null when there is no
  winner at all) and the label falls back to HOME/AWAY, mirroring the name pills beside it.
- Verified against a running Controller with both players unnamed: a home win shows "HOME WINS!",
  an away win shows "AWAY WINS!" with the away tint, and subtracting the winning point hides the
  banner again (`sb-hidden`) — the clearing behaviour is unchanged, only the empty-name case is.
- No C# change. The script is an embedded resource, so this needs a rebuild, not an asset copy,
  and it has no unit test — the check was done in the browser against the live app.

### 2026-11-26 — Merge origin/master: editable after a win, per-side minus, per-side text

- Merged `58cc003` ("Keep match editable after a win; add score-subtract and per-side text color")
  into the local 17-commit series instead of pushing over it. Six files conflicted; both sides'
  intent was kept, and their `SubtractPoint`/`UpdateWinner` model now runs inside the single lock
  and raises its snapshot inside it, exactly like every other mutation — none of the thread-safety
  work was given up to take their feature.
- Behaviour now: reaching a Race-To target only sets `Winner`, recomputed after every score change,
  and the match stays active — so +/−, undo, shooter toggle and ball buttons all keep working after
  a win, and a mistaken final point can be corrected without a Reset. `EndGame` and its
  `IsGameActive = false` are gone; adopted deliberately over the previous "a win ends the match"
  rule, and the help window, both Stream Deck docs and the tests now say the win only shows the
  banner.
- `UndoPoint` gained a floor at zero. It could not go negative before only because it was the one
  way to lower a score; now that a per-side minus can remove a point that is still on the undo
  history, a later undo would have driven the score to −1. Pinned by a test.
- `ColorTheme.Text` split into `HomeText`/`AwayText` (their change), threaded through
  `ColorTheme.Copy()`, the DTO/mapper and the overlay CSS; tests assert both sides separately.
- One of their doc lines did not survive review: "Reset Match is destructive, clears the loaded
  players, theme and style" is the stale claim this branch had already corrected — `ResetGame`
  keeps players, theme, style and visibility, and the merged docs say so.
- Tests 47 → 52 (their four cases adapted to `NewMatch`/snapshots, plus one for the undo floor),
  all passing; build clean, 0 warnings.

### 2026-11-26 — Remove GameState.StartTime

- `StartTime` was stamped with `DateTime.UtcNow` whenever `InitializeGame` built a new state, and
  carried through `Clone()`, but nothing read it: the overlay DTO has no start time, neither
  Controller screen shows one, and no test asserted on it. No match-duration display is planned, so
  it is gone rather than left as a field that reads like a feature.
- If a duration or shot clock ever lands, it needs more than the field back: a DTO property and a
  render spot on the overlay page, a decision on what a pause or a re-rack does to it, and the fact
  that `ResetGame` already returns to setup (so the clock would restart on the next
  `InitializeGame`, not continue across a "reset").
- Build clean, 47/47 tests — no test referenced the field.

### 2026-11-26 — Remove the unsynchronised state accessor and the unused state id

- `GameManager.GetCurrentGameState()` returned the live `GameState` with no lock. Its last three
  callers (the icon route and the two SSE connect frames) moved to `GetStateSnapshot()` during the
  review, leaving it test-only — and it was the exact footgun that produced those unlocked reads, so
  it is gone rather than documented. The tests now read `GetStateSnapshot()` too: the identity
  assertions they depend on still hold, because `Clone()` remaps `CurrentShooter` and `Winner` onto
  its own player copies, and `Assert.NotSame(snapshot, raised)` still proves the event payload is a
  separate object.
- `GameState.Id` (a `Guid` string regenerated per state) had no reader anywhere — not in the DTO,
  the SSE payload, the pages or the tests; only `Player.Id` is actually used, by the end-cap icon
  URL. Removed, along with its copy line in `Clone()`.
- Net effect: every path to state is now either a lock-taking mutation or `GetStateSnapshot()`; there
  is no way to hold the live object from outside the class.
- Tests: 39 call sites switched, 47/47 still pass, build clean.
- Reviewing that switch turned up two assertions that had quietly gone vacuous, since a snapshot
  always hands out fresh copies: `Assert.NotSame(snapshot, raised)` in the payload-isolation test
  and `Assert.NotSame(theme, stored)` in the colour-theme test. Both could only have failed for a
  different reason than the one they were written for. They are replaced by value checks that still
  bite — a later point and a later pocket must not reach the captured payload, and mutating the
  caller's theme must not reach state — and the single-use `CreatePlayer` test helper is inlined.
- `GameState.StartTime` was left in place at the time of this entry as a candidate for the same
  treatment as `Id`; it was removed in the entry above once no match-duration display turned out to
  be planned.

### 2026-11-26 — README/CLAUDE.md drift: overlay URL, control API, GameState shape

- README's Quick Start pointed OBS at `http://localhost:5000/...`, which is neither the port nor
  the address the app uses: the server binds `127.0.0.1:51234` (`OverlayHost.Port`), and that is
  what the Controller prints. Both browser-source examples now use the real URL.
- README and CLAUDE.md both described the control API as "increment/decrement score". There is no
  decrement route — it is `/api/control/score/{home,away}/add` plus `/api/control/score/undo`,
  and undo now reverses the most recently scored point whichever side scored it, including one
  that ended the match. Both now list add/undo, and the per-ball pocketed toggle that was missing
  from the summary.
- CLAUDE.md's `GameState` shape was missing `ScoreboardStyle`, `Visibility` and `CueBallSpin`,
  which have been part of the model since the styling, show/hide and cue-ball work.
- Docs only; no code changed.

### 2026-11-26 — Docs catch-up: reset behaviour, overlay toggles, current status

- "Reset Match" stopped being destructive to the operator's setup when `ResetGame` began carrying
  the players, styling and chosen game/Race-To over, but three places still said otherwise:
  `streamdeck/README.md` and both Reset Match notes in `HelpWindow.xaml` (Live View and Stream
  Deck tabs). They now say it ends the match and clears scores, undo history, pocketed balls and
  the winner while keeping player details, end-cap icons, colours, style, show/hide settings and
  the game/Race-To choices.
- The in-app help described the OVERLAY row only under Live View, after it became available on
  Match Setup too. It is now described on both screens, with a note that the choices carry
  through START MATCH and RESET MATCH, and the Live View bullet refers back to it.
- `PROGRESS.md`'s Current Status block was stale in several ways: it listed the deleted
  `SetCurrentShooter`, said state was "recreated only implicitly via `ResetGame`", described
  `PlayerViewModel` as owning players and omitted its end-cap icon fields, still said the control
  API and `streamdeck/` docs were missing, and quoted 18/18 tests. It now matches the code — the
  locked, snapshot-raising manager, `SetPlayerDetails`/`ToggleCurrentShooter`, both exits keeping
  the players and settings, and 47/47.
- Known Gaps: the flag bullet claimed flag end-caps were not implemented, but "Choose Flag..."
  with bundled country and US-state SVGs shipped on 2026-08-14. It now records what is actually
  missing: no nationality/flag field on `Player`, so a flag is re-picked each match.
- Documentation and help text only; no behaviour change. Build clean, 0 warnings; 47/47 tests.

### 2026-11-26 — One embedded-asset reader for both overlay page endpoints

- `ScoreboardEndpoints` and `CueBallEndpoints` each had their own private `ReadEmbeddedAsset`, the
  same six lines apart from the type used to reach the assembly. Moved to `EmbeddedAsset.Read`,
  which both now call for their HTML/CSS/JS routes; the two private copies are gone, and with them
  `ScoreboardEndpoints`'s `using System.Reflection;`, which only existed for that method.
- Checked on the running app: all six page routes (`/overlay/scoreboard`, its `style.css` and
  `app.js`, `/overlay/cueball`, its `style.css` and `app.js`) return 200 with the expected content
  types and non-empty bodies. Build clean, 0 warnings; 47/47 tests, which do not cover the Overlay.

### 2026-11-26 — One SSE implementation for both overlay streams

- `ScoreboardEndpoints` and `CueBallEndpoints` each carried a copy of the same ~30-line stream: set
  `no-cache`/`text/event-stream`, open a per-connection channel so the raising thread never writes to
  the response, replay the current snapshot on connect, forward every `GameStateChanged`, swallow the
  `OperationCanceledException` from a closed OBS source, then unsubscribe. Only the serializer
  differed — each page maps the same state differently — so that is now the only thing the endpoints
  pass in: `GameStateEventStream.WriteAsync(context, gameManager, serialize)`.
- The two properties worth keeping are preserved in the helper: the connect frame comes from
  `GetStateSnapshot()` rather than the live instance, and the channel keeps the writer (inside
  `GameManager`'s lock) off the response stream.
- Checked on a running app, not just compiled: `/overlay/api/scoreboard/stream` sent its connect
  frame plus one frame per mutation (`POST /overlay/api/cueball/contact`), and
  `/overlay/api/cueball/stream` sent three across contact and clear — `{"x":null,"y":null}` →
  `{"x":0.2,"y":0.9}` → `{"x":null,"y":null}`. A shooter toggle with no match running correctly
  produced no frame (the manager early-returns, so nothing is raised). Build clean; Core tests
  unchanged at 47/47.
- Still duplicated, left alone: `ReadEmbeddedAsset` in both endpoint classes.

### 2026-11-26 — Drop the player-carrying InitializeGame overload

- `InitializeGame(Player, Player, GameType, RaceToMode)` had no production caller once START MATCH
  began starting from the players already in state, so it only existed to let the tests inject their
  own — a second entry point whose players had to be built outside the lock, next to the
  `SetPlayerDetails` path the app actually uses. Both it and the private `InitializeGameLocked`
  helper are gone; `InitializeGame(gameType, raceToMode)` does the work directly.
- The tests now build matches the way the setup screen does: a `NewMatch(...)` helper writes both
  players through `SetPlayerDetails`, then calls `InitializeGame`. That also means every
  `InitializeGame` test exercises the adoption of the state players rather than a bypass of it.
- Two assertions changed meaning as a result: `Assert.Same(player1, state.Player1)` in the setup test
  and `Assert.Same(player1, state.Winner)` in the split-mode test. The first is now covered by the
  names/shooter assertions (plus `SetPlayerDetails_ValuesSurviveStartingTheMatchFromTheStatePlayers`),
  the second compares `state.Player1` to `state.Winner` instead of to a locally held player.
- Build clean, 0 warnings; 47/47 tests, same names and same coverage otherwise.

### 2026-11-26 — Reset keeps the chosen game type and Race-To mode

- `ResetGame` builds a fresh state, and `GameType`/`RaceToMode` were going back to the enum defaults
  (`EightBall`/`Single`) while the setup screen kept showing whatever was selected. `ResetMatch`
  therefore had to re-push a preview with `SetMatchPreview`, and a reset from
  `/api/control/match/reset` — which has no screen behind it to re-push anything — left the overlay
  previewing 8-ball until the operator started a new match.
- Reset now carries both alongside the players and the styling, so "keep what the operator
  configured, clear what was played" holds for every field, and `ResetMatch` is one call to
  `ResetGame()`. The review entry above still says `ResetMatch` keeps `ApplyMatchPreview()`; that was
  true when written and is superseded here.
- Test: `ResetGame_KeepsTheChosenGameTypeAndRaceToMode` starts a NineBall/Split match, scores,
  resets, and asserts the inactive state still reports NineBall/Split (the old defaults were
  `EightBall`/`Single`, so the assertion would have failed before this change). 46 → 47 tests.
- Not done, deliberately: an early return in `SetPlayerDetails` when all four fields are unchanged.
  No caller can produce that call any more — the per-keystroke preview push is gone (see the review
  entry) and the view model's settable properties skip equal values — so the comparison would be
  untested dead code in the state lock.

### 2026-11-26 — OVERLAY show/hide toggles also on the match-setup screen

- The three toggles (Score Bar, Ball Tracker, Winner Banner) only existed on the live view, so the
  operator couldn't set the show up hidden and then start: the moment the match began the only
  controls that could hide anything were on the screen that had just replaced the setup one. That
  also made the earlier "starting a match keeps the operator's visibility" fix unreachable from a
  fresh launch — there was no way to be hidden while still on the setup screen.
- The panel is now one `OverlayVisibilityTogglesTemplate` in `MainWindow.xaml`, instantiated by a
  `ContentControl` on both screens and bound to the same three view-model booleans, so the two
  copies can't drift apart. On the setup screen it sits bottom-left of the `START MATCH` row, one
  click away from starting.
- Verified on the running app: the setup screen now exposes `Score Bar`, `Ball Tracker` and
  `Winner Banner`; clicking Score Bar and Ball Tracker with no match running set
  `scoreBarVisible: false, ballTrackerVisible: false`; `START MATCH` kept both false, and both
  copies of the panel render them in the off style. The overlay page loaded in Chromium afterwards
  has `#scorePill` and `#ballTracker` at `display: none`. Build clean, 0 warnings; 46/46 tests (no
  test covers XAML).

### 2026-11-26 — Review pass: reads back under the lock, one styling rule for both exits

- Three overlay endpoints read `GetCurrentGameState()` — the live instance, no lock — from Kestrel
  threads: the end-cap icon route (picking a player, then decoding their icon) and the initial
  frame of both SSE streams (serialising the live `PocketedBalls` set). All three now take
  `GetStateSnapshot()`, which is what the doc comment on `GetCurrentGameState()` already said to
  do for anything that copies, serialises or runs off the UI thread. Left as-is: the Core tests,
  which read single fields on one thread.
- Starting a match and resetting one now follow the same rule about the operator's look.
  `InitializeGame` builds a fresh state like `ResetGame` does, and both move the colour theme,
  scoreboard style and show/hide settings onto it as copies (one shared
  `CarryOperatorStyling(from, to)`). That fixes a real gap: pressing START MATCH after hiding the
  score bar silently brought it back, because the view model only re-applied the theme and style,
  never the visibility.
- Because state now carries the styling, `StartMatch`'s theme/style re-push and `ResetMatch`'s
  theme/style re-push, score/group/winner/pocketed-ball re-zeroing and `RefreshBallLayout()` are
  gone — `ResetGame` raises a snapshot synchronously on the UI thread, so `ApplyGameState` has
  already done exactly that by the time `ResetMatch` continues. `ResetMatch` keeps only
  `ApplyMatchPreview()`, which is still needed because a reset state goes back to the default
  8-ball while the setup screen may be showing 9-ball.
- Dropped the match-preview push from both `PlayerViewModel.PropertyChanged` handlers. Player
  fields already reach state (and the overlay) through `SetPlayerDetails`; the extra
  `SetMatchPreview` only re-wrote an unchanged game type and Race-To mode, doubling the SSE
  traffic for every keystroke. The away handler had nothing left, so it and its subscription are
  gone; the home one now only mirrors Race-To in single mode.
- `SetColorTheme` now copies the incoming theme the way `SetScoreboardStyle` already did, so a
  caller that keeps its reference can't keep mutating state off the lock and without an event.
- Tests (48 total): added `InitializeGame_KeepsTheOperatorsColorThemeStyleAndVisibility` next to
  the reset one; the rest of the suite was unchanged and still passes.
- `SetCurrentShooter(Player)` and its two tests are gone (48 → 46 tests). Production only ever
  called `ToggleCurrentShooter`; the setter was a test-only way to write the shooter from a
  `Player` the caller had to read outside the lock first — the same read-then-act shape that
  ball-group assignment already dropped. CLAUDE.md's list of `GameManager` operations and the
  control API's comment about the atomic toggle now name `ToggleCurrentShooter`.

### 2026-11-26 — Setup-screen player fields now write through GameManager

- `PlayerViewModel` no longer owns a `Player`. It holds the manager plus which side it edits, and
  every name/team/Race-To/icon edit calls the new
  `GameManager.SetPlayerDetails(isHome, name, teamName, raceToTarget, endCapIcon)` — so those writes
  take the same lock as scoring, ball groups and styling, and a snapshot being serialized for an
  overlay can no longer catch a player mid-edit (most visibly an icon version bumped while the
  artwork bytes were still the old ones).
- The players in state *are* the setup players now: `InitializeGame(gameType, raceToMode)` starts
  the match with the `Player`s already in state, and `SetMatchPreview(gameType, raceToMode)` takes
  no players either. The four-argument `InitializeGame` stays for callers that bring their own
  players (the tests).
- `SetPlayerDetails` assigns `EndCapIcon` only when the artwork actually changed, so typing a name
  doesn't bump `EndCapIconVersion` and make every overlay re-download an unchanged icon.
- `ResetGame` keeps what the operator set up rather than played: the two players, the colour theme,
  the scoreboard style and the show/hide settings. Only match state is cleared — scores, undo
  history, pocketed balls, winner, and the players' ball groups. The players matter because a reset
  from `/api/control/match/reset` has no setup screen behind it to re-type anything (that HTTP path
  is what surfaced the problem); the styling matters because a reset ends a match, it doesn't
  restyle the show. The carried objects are copies, so a later styling edit can't rewrite an
  already-delivered snapshot.
- Tests (47 total): setup fields land in `GetStateSnapshot()` including the icon version; they
  survive `InitializeGame` from state; the icon version only moves when the artwork changes;
  snapshots stay detached from later edits; a reset keeps the players but clears their groups; a
  reset keeps the theme, style and visibility while clearing the match; and a concurrent
  writers/readers case asserts no snapshot ever pairs one player's name with the other artwork.
- Verified by hand on the real app: typing name/team/Race-To in Match Setup appeared in
  `/overlay/api/scoreboard/state`, survived an HTTP reset, and carried into START MATCH. Choosing
  `overlay-endcap-a.png` through `Choose Icon...` produced
  `/overlay/api/players/{id}/icon?v=1` (200, `image/png`, 16 877 bytes, byte-identical to the file);
  renaming kept `v=1`; an HTTP reset kept `v=1`; picking `overlay-endcap-b.png` bumped to `v=2`
  (16 894 bytes, byte-identical). No exceptions in the app log. Build clean, 0 warnings, 47/47
  tests.

### 2026-11-26 — Verification correction: group-assignment routes exercised under EightBall

- The earlier 1070-request stress ran a **NineBall** match, so `/api/control/balls/{side}/{group}`
  hit `AssignBallGroup`'s `GameType != EightBall` early return. The routes answered 200 but never
  mutated anything, so the side-based refactor's real mutation path was untested under load (the
  Core xUnit cases were the only coverage). No code change — the gap was in the verification.
- Re-ran against a live EightBall match (setup screen's Game combo switched to EightBall before
  START MATCH; state confirmed `"gameType":"EightBall"`, `"isGameActive":true`):
  - **Functional:** `home/solids` → home `Solids`, away `Stripes`; then `away/solids` → home
    `Stripes`, away `Solids`. The complement flip works through the HTTP routes, on the real app.
  - **Under load:** 1330 requests, all 200 — 680 group-assignment, 240 ball-toggle, 120 score,
    120 shooter-toggle, 120 state reads, 50 match resets — including a phase with resets
    interleaved with assignments. The final state after the run still held `Solids`/`Stripes` (not
    `Unassigned`), so the last assignment wrote rather than early-returning, and the app log had no
    exceptions.
- Build and unit tests stand unchanged from the entry below (clean build, 41/41).

### 2026-11-26 — Thread-safety follow-up: in-lock delivery, side-based group assignment

- **Snapshot ordering fixed.** Raising `GameStateChanged` after leaving the lock let a later mutation
  notify before an earlier one (thread A snapshots, thread B snapshots, B raises first), so the UI or
  an overlay could apply an older snapshot last and show stale scores until the next event. The event
  is now raised *inside* the mutation lock, which makes delivery order match mutation order without
  sequence numbers; subscribers just must not block (the current ones marshal to a dispatcher or
  write into an SSE channel). Covered by
  `ConcurrentPoints_RaiseSnapshotsInMutationOrder`: 200 parallel points must arrive as 1..200, so any
  backwards step fails the test.
- **Last read-then-act race in the control API closed.** `/api/control/balls/{side}/{group}` used to
  read `GetCurrentGameState()`, pick the player by side, then call `AssignBallGroup(player, group)` —
  a reset in between meant acting on a player that had already been replaced (silent no-op).
  `AssignBallGroup(bool isHome, BallGroup)` now resolves the side inside the lock, the endpoints call
  it directly, the private `AssignGroup` helper and the player-based overload are gone.
- **Simplifications in the committed change:** every mutator raises `_currentGame.Clone()` inline
  instead of the `GameState snapshot; … snapshot = …; } Raise(snapshot);` dance; `GameState.Clone`
  inlines its player-reference remapping instead of a helper method; `GetCurrentGameState()` no longer
  takes the lock for what is only a reference return (it's documented as the live view, and
  production code now goes through `GetStateSnapshot()`); and `GameViewModel` lost its
  `_homePlayerModel`/`_awayPlayerModel` fields plus its own `AssignGroup` wrapper now that shooter
  toggling and group assignment are atomic, side-based calls.
- Verified: `dotnet build` clean, `dotnet test` 41/41, and a 1070-request concurrent stress across
  ball toggles, shooter, score, reset, group assignment and state reads returned zero non-200s.

### 2026-11-26 — Fixed: HTTP control API 500s from cross-thread UI updates

**Root cause.** The Stream Deck endpoints run on Kestrel threads and call straight into the shared
`GameManager`, which raises `GameStateChanged` synchronously on the calling thread. `GameViewModel`
handled that event on the same thread and raised `PropertyChanged`, and `MainWindow`'s handler for
`HomeIsCurrentShooter` writes `CurrentShooterToggleButton.Content` — a `DependencyObject` — so WPF's
thread-affinity check threw `InvalidOperationException: The calling thread cannot access this object
because a different thread owns it`, from `MainWindow.UpdateCurrentShooterButton`, up through
`GameViewModel.OnGameStateChanged` and `GameManager.RaiseGameStateChanged`, and back out of the
endpoint as HTTP 500. Worse than a failed request: the throw aborted `OnGameStateChanged` partway,
so the live view could be left half-updated (score changed, ball layout not).

**Reproduced** by hammering `/api/control/*` and `/overlay/api/*` concurrently: 6 of 1480 requests
failed — `shooter/toggle` ×4, `balls/{n}/toggle` ×1, `match/reset` ×1 — matching the one-off 500
noted during the end-cap verification. The exception text came from a throwaway request-logging
middleware (removed again once captured).

**Fix, in three layers.**
- `GameManager` serializes every mutation on a private lock (one instance is driven by the WPF UI,
  keyboard shortcuts and HTTP at once) and `GameStateChanged` now carries a `GameState.Clone()`
  snapshot taken under that lock instead of the live instance. Subscribers can hold and read it on
  any thread, which also removes the latent "Collection was modified" race where an SSE handler
  enumerated `PocketedBalls` while another request mutated it.
- `ScoreboardEndpoints`/`CueBallEndpoints` state routes map `GetStateSnapshot()`, not the live object.
- `GameViewModel.OnGameStateChanged` marshals to `Application.Current.Dispatcher` when the event
  arrives off the UI thread (`ApplyGameState` holds the former body), so bound properties and
  `MainWindow`'s dependency-object writes happen where they're allowed.
- Two read-then-write races closed at the source: new `GameManager.ToggleCurrentShooter()` and
  `ToggleBall(int)` perform the toggle atomically under the lock, and `ControlEndpoints` call them
  instead of reading state and then calling `SetCurrentShooter`/`PocketBall`.

**Verified** after the fix, same hammering with zero non-200 responses: 1480 mixed requests, 860
with a match active, and 1000 shooter-heavy requests with no resets (the path that used to throw) —
3340 requests, 0 failures. Tests added: `GameStateChanged_CarriesASnapshotThatLaterMutationsCannotChange`
and `ConcurrentMutations_AreSerializedAndEveryEventCarriesAConsistentSnapshot` (400 parallel mutations
with an enumerating subscriber). `dotnet build` clean; `dotnet test` 40/40.

**Not changed:** `Player` field writes from the match-setup screen still bypass the lock (atomic
reference/value writes only — a subscriber can see a stale name/icon for one event, but nothing
throws), and UI updates now take one dispatcher hop, so a remote change lands a frame later than
before.

### 2026-11-26 — End-cap icons moved out of the SSE state payload

- `ScoreboardStateDto.PlayerDto` no longer carries the base64 data URI (`EndCapIcon`); it carries
  `EndCapIconUrl`, a relative reference to the new `GET /overlay/api/players/{id}/icon` endpoint
  (or `null` when that player has no icon). The icon bytes are decoded from the player's data URI
  and served with their declared MIME type by a small `EndCapIconDataUri` helper; unknown player
  ids and players without an icon both answer `404`.
- `Player.EndCapIcon` gained a custom setter that bumps a new `EndCapIconVersion`, and the DTO
  appends it as a `?v=` query. That's cache-busting only — the browser can cache the image across
  score updates, and picking new artwork gives the page a new URL so a stale logo can't stick. An
  old `?v=` URL still serves the current bytes, so the query is never load-bearing.
- `scoreboard.js` reads `endCapIconUrl` and only reassigns `img.src` when that reference changes,
  so a score click no longer re-requests an unchanged image.
- **Measured** with a throwaway harness (Core + Overlay, real Kestrel on a scratch port, not
  committed): with a 400,174-character inline data URI, one `/overlay/api/scoreboard/state`
  snapshot — the same serialization the SSE stream pushes on every operator action — is now
  **776 bytes**, down from ~400 KB, and contains no `data:image` substring. The icon endpoint
  returned `200 image/svg+xml` with bytes identical to the original (300,110), `404` for an
  unknown player id, and `404` for a player with no icon. Changing the artwork moved the URL
  `?v=1` → `?v=2` while the old URL still returned the new bytes. Payload size was unchanged
  (776 bytes) after scoring a point.
- Trimmed after a review pass: the setter no longer carries an equality guard (the Controller
  only assigns from `PlayerViewModel`, which raises change notifications solely on a real change,
  so the branch was unreachable), the endpoint looks the player up directly instead of allocating
  a LINQ array, the decoder no longer treats an empty payload as a separate case, and
  `scoreboard.js` compares the `src` attribute to the URL directly instead of via a temp variable.
- Core test added: `EndCapIconVersion_BumpsWheneverIconIsAssigned`. `dotnet build` clean
  (0 warnings, 0 errors); `dotnet test` 38/38.
- Verified end to end in the real Controller and a browser (2026-11-26), replacing the earlier
  "not verified visually" caveat: a player's end cap was set through the app's own "Choose
  Icon..." file dialog, after which the state payload carried
  `/overlay/api/players/{playerId}/icon?v=1` (and no data URI). The endpoint answered
  `200 image/png` with bytes identical to the picked file (`cmp` clean, 910 bytes), and the
  already-open `/overlay/scoreboard` page rendered the logo at that end cap — `src` set to that
  URL, natural 128×128, rendered 28×28, the accent-colored `.cap-dot` hidden — with the browser
  network log showing only the icon request and no page reload. Picking different artwork moved
  the URL to `?v=2`; the page fetched it over the existing SSE connection without reloading and
  the rendered pixels changed (canvas centre pixel (20,40,110) blue → (150,20,30) red), so the
  logo updates live rather than sticking on the cached one. Unknown player id still answered
  `404`. Verification captures: `overlay-endcap-a.png` / `overlay-endcap-b.png` in the repo root
  (screenshots only, not committed assets).
- Observed while driving that check, unrelated to this change: `GET /api/control/match/reset`
  returned `500` once, then `200` on three consecutive retries, with nothing in the app's console
  log. Not reproduced, so not investigated further. The plausible shape is a cross-thread race —
  the Stream Deck endpoints mutate `GameManager` from a Kestrel thread while
  `GameViewModel.OnGameStateChanged` runs synchronously on that thread and touches WPF-bound
  state. Worth a look if it recurs.

### 2026-11-26 — Undo reverses the most recent point

- `GameManager.UndoPoint()` used to decrement whichever player was *ahead*, which was wrong
  whenever the trailing player had scored last (2–0 → away scores → undo gave 1–1 instead of
  2–0) and did nothing at all at a tie. It now records which player scored each point in a
  private `List<bool> _pointHistory` and pops the most recent entry, so undo always reverses the
  action the operator actually took. The history is internal to `GameManager` — not on
  `GameState` or the overlay DTO, so the SSE payload is unchanged — and is cleared by
  `InitializeGame` and `ResetGame`.
- Undo now also reverses the point that *ended* the match, clearing `Winner` and re-enabling
  `IsGameActive`. The old `if (!IsGameActive) return;` guard meant the `Winner = null;
  IsGameActive = true;` lines already sitting inside `UndoPoint` were unreachable; recovering
  from a mis-clicked winning point no longer requires a full Reset Match (which also drops the
  players and styling).
- Tests: replaced `UndoPoint_DecrementsLeadingPlayersScore` with
  `UndoPoint_ReversesTheMostRecentlyScoredPoint_NotTheLeader`, and added coverage for walking
  back points in reverse order, undoing at a tie, undoing the winning point (match resumes), and
  undo after `ResetGame` (a reset match can't be resurrected).
- Docs updated to match: the endpoint table in `streamdeck/README.md`, the Step 5 test pass in
  `streamdeck/SETUP.md`, and the in-app Help window's "Score + / −" bullet.
- `dotnet build` clean (0 warnings, 0 errors); `dotnet test` 37/37 (was 33/33 — five new undo
  cases replace the one old leader-decrement test). The Controller/overlay behavior itself was
  not exercised by hand this pass; the change is confined to `GameManager.UndoPoint` and the
  docs, and `GameViewModel`/`ControlEndpoints` both just call through to it.

### 2026-11-26 — Removed unused `GameViewModel.MatchTitle`

- Deleted the `[ObservableProperty] private string? matchTitle;` field from `GameViewModel`. It
  was never bound in `MainWindow.xaml`, never copied into `Player`/`GameState`, and never included
  in `ScoreboardStateDto`, so it was dead state that suggested a per-match title feature existed
  when nothing displayed one. Removing it rather than wiring it through — no doc, mockup, or
  changelog entry ever called for a match title on the overlay. No version bump (trivial removal,
  no behavior change).

### 2026-08-15 — Single-file self-contained publish (Phase 6)

- Added `<RuntimeIdentifiers>win-x64</RuntimeIdentifiers>` to `PoolScoreboard.Controller.csproj`
  plus a `Condition="'$(SelfContained)' == 'true'"` property group
  (`PublishSingleFile`/`IncludeNativeLibrariesForSelfExtract`/`EnableCompressionInSingleFile`) so
  these only kick in for a real publish, not `dotnet build`/`dotnet run` — confirmed a plain
  `dotnet build` still produces the normal multi-file Debug output afterward.
  `IncludeNativeLibrariesForSelfExtract` matters specifically for WPF: a few native dependencies
  (e.g. `PenIMC`, `wpfgfx`) can't be merged into the single file, so without it they'd still land
  next to the exe instead of self-extracting from it at runtime.
- Verified end-to-end: `dotnet publish -c Release -r win-x64 --self-contained true
  -p:PublishSingleFile=true` produces one ~84MB `PoolScoreboard.Controller.exe` (plus `.pdb`
  symbol files, which aren't needed for distribution) with no other runtime files. Launched the
  published exe directly (not `dotnet run`), confirmed the process starts and stays up, and
  confirmed the in-process overlay/control Kestrel server comes up correctly on the packaged
  build too — `GET /overlay/scoreboard` returned 200 and
  `GET /overlay/api/scoreboard/state` returned a valid `GameState` JSON snapshot — before
  tearing the test process and output down.
- Also added `You Chalkin To Me.png` (developer-supplied mascot art, background removed via a
  connected-component flood fill so only the small interior details like teeth/eye-highlight
  stayed opaque) as `Assets\Icon\AppIcon.ico`, wired via `ApplicationIcon`, ahead of this phase
  so the packaged exe carries a real icon rather than the default WinExe placeholder.

### 2026-08-14 — In-app Help modal

- Added `HelpWindow.xaml`/`.xaml.cs`: a modal (opened via `ShowDialog`) with a left-hand nav
  (Overview / Using the Controller / OBS Studio Setup / Stream Deck Setup) and a scrollable
  content panel on the right, styled to match the app's existing dark theme (reuses
  `BackgroundBrush`/`TextPrimaryBrush`/`TextSecondaryBrush`/`AccentBrush`/`ActiveButtonStyle`/
  `InactiveButtonStyle` from `Themes/Default.xaml` — no new global resources). Content is
  self-contained (not just links to the repo's markdown docs) since a packaged/distributed build
  won't necessarily ship the source repo — it condenses CLAUDE.md's feature set, every Match
  Setup/Live View control, the OBS Browser Source + Interact-mode workflow, and the full Stream
  Deck endpoint table from `streamdeck/README.md`/`SETUP.md` into one place.
- `GameViewModel` gained a `ShowHelpCommand` (`new HelpWindow { Owner = ... }.ShowDialog()`).
  `MainWindow.xaml` was restructured slightly: the root `Grid` now has a persistent top row (an
  "Help" button, top-right, visible on both the Match Setup and Live View screens) above the
  existing two-screen content, rather than adding it to just one screen — the outer content that
  used to be the root Grid's direct children moved one level deeper into row 1.
- `dotnet build` clean, `dotnet test` 33/33 (no Core/Overlay logic changed — a new WPF window and
  one command). Verified live in two ways: a real screenshot of the Controller confirming the
  Help button's placement doesn't disturb the existing Match Setup layout, and a throwaway WPF
  render harness (a small `net8.0-windows` console app referencing the Controller project) that
  constructs `HelpWindow` directly, merges `Themes/Default.xaml` into a minimal `Application` so
  `StaticResource` lookups resolve, and renders each of the four nav panels to PNG via
  `RenderTargetBitmap` — switching panels by raising each nav button's `Click` routed event
  in-process (not OS-level input simulation) rather than driving the mouse, consistent with
  earlier feedback in this session about not simulating clicks on the app under test. All four
  panels confirmed to render correctly: text wraps, headings/body/code styling reads clearly, the
  active nav button highlights correctly, and the content `ScrollViewer` scrolls independently of
  the fixed nav column.

### 2026-08-14 — Stream Deck control for the ball display

- Added `GET /api/control/balls/{number}/toggle` (number 1-15) to `ControlEndpoints` — toggles
  that ball's pocketed state, mirroring the exact toggle semantics of clicking a ball in the
  Controller's live view (pocket it if live, un-pocket it if already marked). Returns `400` for
  numbers outside 1-15, since this is an external-facing API boundary unlike `GameManager`'s
  internal `PocketBall`/`UnpocketBall` (which don't validate, since the Controller only ever
  calls them with real ball numbers from its own view models).
- Updated `streamdeck/README.md` and `streamdeck/SETUP.md` with the new endpoint: a worked
  example of wiring up per-ball buttons, guidance on only wiring the balls actually in play for
  the current game type (1-9 for 9-ball, 1-10 for 10-ball, 1-7/9-15 plus 8 for 8-ball), and a
  note that 15 individual buttons may be more than fits on smaller Stream Deck models — suggesting
  a dedicated folder/page or skipping them in favor of clicking balls directly in the Controller.
- `dotnet build` clean, `dotnet test` 33/33 (no Core changes — thin routing wrapper over
  already-tested `PocketBall`/`UnpocketBall`). Verified live with a throwaway console harness
  (same pattern as the rest of Phase 5): toggled ball 9 on then off, toggled balls 1 and 15 on,
  and confirmed `GameManager.GetCurrentGameState().PocketedBalls` reflected each change correctly,
  plus confirmed ball numbers 0 and 16 are rejected with `400`.

### 2026-08-14 — Stream Deck setup walkthrough

- Added `streamdeck/SETUP.md`: a step-by-step guide to actually wiring up the Phase 5
  `/api/control/*` endpoints in the Stream Deck app (creating a profile, adding a "Website"
  action per button, the background-fetch setting so buttons don't pop open browser windows,
  a full button-by-button test pass, and troubleshooting). `streamdeck/README.md` stays the
  reference table of endpoints/effects and now links to `SETUP.md` at the top. Documentation
  only — no code changed.

- New `PoolScoreboard.Overlay.Endpoints.ControlEndpoints` maps ten `/api/control/*` routes onto
  the shared `GameManager` instance: score add (home/away), undo, shooter toggle, 8-ball group
  assignment (home/away × solids/stripes), new rack, and reset match. All are `GET` routes, not
  `POST` — per CLAUDE.md's "no custom Elgato SDK plugin" design, Stream Deck buttons are meant to
  hit these via the built-in "Website" action in background mode, which has no way to configure a
  request body or verb. Registered in `OverlayHost.Start` alongside the existing
  `ScoreboardEndpoints`/`CueBallEndpoints` calls.
- Added `streamdeck/README.md`: a table mapping a suggested button layout to each endpoint and
  its effect, plus notes on safe no-op behavior pre-match, what "Reset Match" actually does (full
  reset back to the Match Setup screen, not a mid-rack undo), and why the cue-ball "Clear" button
  is deliberately left off the table (meant to be clicked directly on the OBS browser source via
  Interact mode).
- **Found and fixed a real desync bug** while wiring this up: `GameViewModel.GameInitialized`
  (which toggles the Controller between the Match Setup and Live View screens) was previously
  only ever set by the local `StartMatch`/`ResetMatch` button-click commands, never derived from
  `GameManager` state. Since the whole point of `/api/control/match/reset` is to let an operator
  reset the match from a physical Stream Deck button without touching the WPF window, hitting it
  would have reset `GameManager`'s state correctly but left the Controller's own screen stuck on
  the (now-stale) Live View. Fixed by deriving `GameInitialized` in `OnGameStateChanged` instead:
  `state.IsGameActive || state.Winner != null` (the `Winner != null` half keeps the live view
  showing the winner banner after a race is won, until the operator resets or starts a new rack —
  matching existing behavior). Removed the now-redundant explicit `GameInitialized = true/false`
  assignments from `StartMatch`/`ResetMatch`, since the derived value already covers both.
- `dotnet build` clean across all four projects; `dotnet test` 33/33 (no Core logic changed —
  `ControlEndpoints` are thin routing wrappers over already-tested `GameManager` methods).
  Verified live end-to-end with a throwaway console harness (Core + Overlay only, referencing
  both projects directly, no WPF) that starts a real match via `GameManager.InitializeGame`,
  starts `OverlayHost` on a scratch port, and hits every `/api/control/*` endpoint via `HttpClient`
  while asserting on `GameManager.GetCurrentGameState()` after each call — confirmed score
  add/undo, shooter toggle, ball-group auto-flip, new-rack pocketed-ball clearing, and match reset
  (`IsGameActive` flips to `false`, players reset to blank) all work correctly. Also confirmed via
  a full `dotnet clean && dotnet build` + Controller relaunch that the app still starts cleanly on
  the Match Setup screen with correct defaults after these changes. Actually wiring a physical
  Stream Deck device to these URLs is still owed by the developer — no such hardware is available
  in this environment.

### 2026-08-14 — Confirmed in real OBS Studio: both overlay pages verified as browser sources

- The developer added `/overlay/scoreboard` and `/overlay/cueball` as actual OBS Studio browser
  sources (not just curl/screenshot checks from this environment) and confirmed both render
  correctly. This closes out the "visual QA in an actual browser/OBS browser source is still
  owed by the developer" caveat noted on every overlay changelog entry back through Phase 3 —
  no code change, just closing the loop on outstanding manual verification.

### 2026-08-14 — Cue ball overlay: training cross + larger contact dot

- Added a `.cross-pattern` element (CSS `::before`/`::after` lines) through the center of the
  cue-ball graphic, mimicking the cross printed on real spin-training cue balls, per developer
  request. Sized after a few rounds of live feedback: short lines spanning only the center ~22%
  of the ball (`inset` at 39%/61%) so it reads as a small reference cross rather than dividing
  the ball into quadrants.
- Enlarged `.contact-dot` from 20px to 34px for better visibility, again per live feedback.
- CSS/HTML-only change, no Core/Overlay-API logic touched; `dotnet test` unaffected (33/33).
  Verified live across three iterations: launched the Controller, opened `/overlay/cueball` in a
  browser, and refreshed after each CSS tweak per the developer's visual feedback.

### 2026-08-14 — Phase 4: Cue Ball Spin Overlay

- Added `PoolScoreboard.Core.Models.CueBallSpin` (`X`/`Y`, each a 0.0-1.0 fraction relative to
  the cue-ball graphic) as a nullable `GameState.CueBallSpin` — `null` means no contact point is
  currently placed. `GameManager` gained `SetCueBallSpin(x, y)` (clamps both into `[0, 1]`) and
  `ClearCueBallSpin()`, both following the existing "live, mid-match" mutation lifecycle
  (`RaiseGameStateChanged()` after every change, same as `PocketBall`/`SetCurrentShooter`).
- Per the developer's choice between manual-clear-only, auto-clear-on-score-only, or both: went
  with **both** — `AddPoint` now clears `CueBallSpin` unconditionally as its first action (before
  the score/win-check logic), and the overlay page carries its own "Clear" button for an early
  manual reset (e.g. after a foul, before the next point is scored).
- New `PoolScoreboard.Overlay.Endpoints.CueBallEndpoints` mirrors `ScoreboardEndpoints`'s
  pattern exactly: serves `/overlay/cueball` (HTML), `/overlay/cueball/style.css`,
  `/overlay/cueball/app.js` (all embedded resources), `GET /overlay/api/cueball/state` (one-shot
  JSON snapshot via new `CueBallStateDto`/`CueBallStateMapper`), and
  `GET /overlay/api/cueball/stream` (SSE, same per-connection `Channel<string>` relay off
  `GameManager.GameStateChanged`). Also adds two write endpoints specific to this page —
  `POST /overlay/api/cueball/contact` (body `{x, y}`, binds to a `CueBallContactRequest` record)
  and `POST /overlay/api/cueball/clear` — since the cue-ball page is the first overlay page that
  needs to *write* state rather than just read it; these aren't considered part of Phase 5's
  Stream Deck `/api/control/*` surface, which is for external button-triggered actions rather
  than a page's own click handler.
- The page itself (`Assets/CueBall/cueball.html/.css/.js`) renders the cue ball as a pure-CSS
  radial-gradient sphere (no bundled image needed, keeping with the offline/no-external-asset
  rule) with a red `.contact-dot` positioned via `left`/`top` percentages from the SSE-pushed
  `x`/`y` fractions. Clicking the ball computes the click position relative to the ball's
  bounding circle and clamps it to the circle's radius (so a corner-of-the-bounding-box click
  can't place the dot visually outside the sphere) before `POST`ing to `/overlay/api/cueball/contact`.
  Independent of the scoreboard page's SSE connection and DOM entirely — a separate OBS browser
  source pointed at `/overlay/cueball`, sized/positioned on its own.
- Registered `CueBallEndpoints.Map(app, gameManager)` in `OverlayHost.Start` alongside the
  existing `ScoreboardEndpoints.Map` call; added `Assets\CueBall\*` to the `Overlay.csproj`'s
  `EmbeddedResource` items (mirroring the existing `Assets\Scoreboard\*` entry).
- `dotnet build` clean across all four projects; `dotnet test` 33/33 (4 new tests: setting/
  clamping `CueBallSpin`, clearing it directly, and confirming `AddPoint` clears it). Verified
  live: launched the Controller and confirmed `/overlay/cueball` + its CSS/JS return 200, and
  exercised the full read/write/clear cycle via `curl` against `/overlay/api/cueball/state`,
  `/contact`, and `/clear` — state round-trips correctly through all three. Visual QA of the
  cue-ball graphic and dot placement in an actual browser/OBS browser source (including using
  OBS's "Interact" mode to click the source, since browser sources aren't normally
  mouse-interactive in a live scene) is still owed by the developer — no browser-rendering
  tooling is available in this environment.

### 2026-08-14 — Fix: ComboBoxes not displaying selected item text

- Root cause of the pre-existing bug noted in the previous entry: the four style/game-type
  `ComboBox`es had their items declared as plain `<system:String>` literals (e.g. `"EightBall"`)
  while bound via `SelectedValue` to enum-typed `GameViewModel` properties (`GameType`,
  `RaceToMode`, `EndCapStyle`, `ShooterIndicatorStyle`). WPF's `Selector` matches `SelectedValue`
  against each item's `SelectedValuePath`-resolved value using `Equals`; with `SelectedValuePath`
  unset, that resolved value is the item itself, so an enum instance was never `.Equals()` to a
  `string`, and the match silently failed — `SelectedItem`/`SelectionBoxItem` never got set, so
  the selection box always rendered blank even though the underlying property held the correct
  value (the reverse direction worked because selecting a string item let WPF's default
  `EnumConverter` parse it back into the enum when writing to the bound property).
- Fixed by changing each `ComboBox`'s child items from `<system:String>` literals to
  `<x:Static Member="enums:GameType.EightBall" />`-style references (added an `enums:` xmlns for
  `PoolScoreboard.Core.Enums` in `MainWindow.xaml`), so the items are actual enum instances of the
  same type as the bound property — `SelectedValue` matching now compares enum-to-enum and
  succeeds, and the default `ContentPresenter` still displays the enum's `ToString()` (e.g.
  "NineBall"), so the visible text is unchanged from before.
- `dotnet build` clean, `dotnet test` 29/29 (no logic changed — XAML-only fix). Verified live via
  a screenshot of the Controller's Match Setup screen: all four combo boxes (Game, Race-To Mode,
  End-Cap Style, Shooter Indicator) now display their selected text ("NineBall", "Single", "Dot",
  "Glow") instead of a blank selection box.

### 2026-08-14 — Bundled flag icon set (country + US state flags) for end-cap icons

- Downloaded and embedded 249 country/territory flag SVGs from
  [lipis/flag-icons](https://github.com/lipis/flag-icons) (MIT) — the `iso:true` subset of its
  `country.json`, renamed from ISO codes to display names (e.g. `us.svg` → `United States of
  America.svg`) — and 56 US state/territory/DC flag SVGs from
  [nibsbin/us-state-flags-svg](https://github.com/nibsbin/us-state-flags-svg) (no explicit repo
  license, but its README states the artwork is pulled from Wikipedia's public-domain holdings and
  frames the repo as "a public resource for artists" — flagged transparently in NOTES.md since it's
  a softer legal basis than flag-icons' explicit MIT license, proceeding since the developer named
  this source directly). Both sets live under `PoolScoreboard.Controller/Assets/Flags/{Countries,States}/`
  as WPF `Resource` build items (`pack://application:,,,/...`-addressable), not `EmbeddedResource`s
  — the idiomatic WPF way to bundle images, versus the manifest-stream approach used for the
  Overlay's HTML/CSS/JS text assets.
- Generated `PoolScoreboard.Controller/Assets/Flags/FlagCatalog.cs`: a static, hardcoded
  `FlagIconEntry` list (`Label`, `Group`, `PackUri`) per flag, sorted alphabetically within each of
  the two groups — no runtime resource-enumeration needed, so ordering and completeness aren't
  dependent on how the build packs resources.
- Added the `SharpVectors.Reloaded` NuGet package — WPF can't render SVG natively. It's a
  build-time/offline dependency only (same category as `CommunityToolkit.Mvvm`, not a CDN/runtime
  fetch), verified via a throwaway console probe that `FileSvgReader.Read(stream)` →
  `new DrawingImage(drawing)` correctly converts real downloaded flag files (including Nepal's
  non-rectangular flag) before wiring it into the app.
- New `FlagPickerWindow` (+ `.xaml.cs`): a modal thumbnail grid using SharpVectors'
  `SvgViewbox` bound directly to each entry's pack URI, grouped "STATES" (alphabetical) then
  "COUNTRIES" (alphabetical) per the developer's spec, each tile a borderless button with the flag
  image + name label. Selecting a tile sets `DialogResult = true` and exposes the chosen
  `FlagIconEntry`.
- `GameViewModel` gained `PickHomeFlagCommand`/`PickAwayFlagCommand`: opens the picker, and on
  selection reads the chosen flag's SVG bytes via `Application.GetResourceStream(entry.PackUri)`,
  base64-encodes them, and sets `player.EndCapIconDataUri = "data:image/svg+xml;base64,..."` —
  reusing the exact same property and live-preview pipeline the custom-file "Choose Icon..." picker
  already uses, so no Core, DTO, or Overlay changes were needed at all. A new "Choose Flag..."
  button sits alongside the existing "Choose Icon..."/"Clear" pair in each player's End-Cap Icon
  row in `MainWindow.xaml`.
- Extended `DataUriToImageSourceConverter` to branch on the data URI's declared MIME type:
  `image/svg+xml` → decode + `FileSvgReader` → `DrawingImage`; anything else (the existing raster
  file-picker path) keeps today's `BitmapImage` behavior. This one converter still backs the
  existing circular preview swatch for both icon sources.
- `dotnet build` clean, `dotnet test` 29/29 (unaffected — no Core/Overlay logic changed). Verified
  live: launched the Controller and confirmed it starts cleanly with the new package and ~305
  embedded assets (no missing-resource or XAML-parse errors), confirmed via a standalone probe that
  SharpVectors correctly converts real flag files at runtime. Full interactive verification of the
  picker grid itself (opening it, confirming STATES-then-COUNTRIES ordering and thumbnail
  rendering visually) is owed by the developer — this pass avoided driving the picker's file/window
  dialogs via UI automation per earlier feedback in this session about not wanting the mouse/
  keyboard driven directly.

### 2026-08-14 — Per-side backgrounds, always-black center, cap-dot roundness, real ball rendering fix

- `ColorTheme.Background` split further into `HomeBackground`/`AwayBackground` (mirroring the
  earlier `Accent` → `HomeAccent`/`AwayAccent` split) so each side's segment of the bar can be a
  distinct color, or the same color if the operator sets both equal. `.side-home`/`.side-away` in
  `scoreboard.css` now each carry their own glossy/flat gradient (`--bg-home*`/`--bg-away*` CSS
  vars); the outer `.pill` no longer paints a shared background since its children fully tile it.
- The center "Race To" segment (`.center`) is now hardcoded `background: #000000` — not driven by
  `ColorTheme` at all, per explicit instruction that it should always be black regardless of theme.
- Fixed `.cap-dot`'s roundness (`border-radius`) to scale with `--radius-scale` like every other
  rounded element (`calc(50% * var(--radius-scale))`) — previously it stayed a fixed circle even
  when the corner-roundness slider was turned down to a squared-off look.
- **Found and fixed a real rendering bug** while verifying the above (via a screenshot — the first
  in this project since none of the earlier UI work had ever been visually confirmed): the
  Controller's ball buttons showed correct *colors* but no digit and no stripe banding at all, for
  every ball. Root cause was two-fold: (1) `Text="{TemplateBinding Content}"` doesn't reliably
  convert a boxed `int` to a string in this context; (2) a `ControlTemplate.Triggers` `DataTrigger`
  toggling the stripe-vs-solid `Visibility` wasn't taking effect. Both were replaced with direct
  `{Binding Number}` / `{Binding IsStripe}` bindings (using the already-proven-working
  `{Binding Color}` Style-setter pattern) feeding `BoolToVisibilityConverter`/
  `InverseBooleanToVisibilityConverter`. Fixing this then surfaced a second bug: those two
  converters are declared in `App.xaml`, which *merges in* `Themes/Default.xaml` — but
  `StaticResource` lookups from *within* a merged dictionary's own Styles can't see the dictionary
  that merged them in, only their own keys and dictionaries they themselves merge. Fixed by
  declaring both converters locally in `Default.xaml` too.
- Widened the Color Theme label column (90px → 120px) so "Home Background"/"Away Background"
  don't truncate.
- `dotnet build` clean, `dotnet test` 29/29 (`SetColorTheme` test updated for
  `HomeBackground`/`AwayBackground`). Verified live with actual screenshots this time (via a
  PowerShell UI-Automation + `Graphics.CopyFromScreen` harness, since no dedicated screenshot tool
  is available in this environment): confirmed ball numbers and real stripe banding render
  correctly in both the Controller and the overlay ball tracker, confirmed the center segment
  renders black, and confirmed setting Home/Away Background to different hex values live-updates
  the overlay with two visibly distinct side colors before any match is (re)started.
- **Known pre-existing issue noticed, not fixed (out of scope for this pass):** none of the four
  `ComboBox`es bound via `SelectedValue` to an enum property (`Game`, `Race-To Mode`, and the two
  new Scoreboard Style dropdowns) visibly display their selected item's text — the dropdown arrow
  shows but the selection box itself renders blank, even though the underlying property value is
  set correctly. Likely a `SelectedValue`/string-item type-mismatch in `StandardComboBoxStyle`
  predating this session's changes (Game/Race-To Mode use the exact same pattern and are also
  affected). Flagging for a future pass since it wasn't part of what was reported this round.

### 2026-08-14 — Live match-setup preview, team names, visibility hierarchy, per-side colors + picker

- Added `GameManager.SetMatchPreview(player1, player2, gameType, raceToMode)` — updates
  `GameState.Player1`/`Player2`/`GameType`/`RaceToMode` without touching scores/`IsGameActive`
  (guarded to no-op while a game is active). `GameViewModel` calls it from every relevant setup
  field's `partial void On<Property>Changed` hook (`SelectedGameType`, `SelectedRaceToMode`, and
  now both `HomePlayer`/`AwayPlayer` `PropertyChanged` — previously only `HomePlayer` was
  subscribed) plus once in the constructor and after `ResetMatch()`, so player names, team names,
  game type, and Race-To all preview live on the overlay before "Start Match" is clicked, the same
  way `ScoreboardStyle` already did.
- The overlay now displays each player's team name (already present in the DTO, just never
  rendered): `scoreboard.html`/`.css`/`.js` gained a `.name-block` wrapping a `.team-name` line
  above `.name`, hidden automatically via `:empty` when a player has no team name set.
- Fixed the ball tracker and winner banner to depend on the score bar's visibility: hiding the
  score bar now hides both, regardless of their own individual toggle, since showing them without
  the bar they're anchored to doesn't make sense. Plain AND logic in `scoreboard.js`'s `render()`.
- `ColorTheme.Accent` split into `HomeAccent`/`AwayAccent` so each side's shooter-glow, shooter
  triangle, and end-cap dot can carry a different color (or the same, if both are set equal —
  the default). The winner banner now tints itself with the winning side's accent (`WinnerIsHome`
  added to the DTO/mapper). `GameManager.SetColorTheme`'s existing lifecycle is unchanged, but
  `GameViewModel` now applies it live the same way as `ScoreboardStyle` (previously it only
  applied at "Start Match", an oversight from the earlier styling work).
- Color entry got a picker: swatch squares next to each hex `TextBox` (Background, Home Accent,
  Away Accent, Text) are now `Button`s wired to a new `PickColorCommand` that opens
  `System.Windows.Forms.ColorDialog` and writes the chosen color back as hex (the hex `TextBox`
  is still there for direct entry/paste). Referencing WinForms via `UseWindowsForms=true` turned
  out to inject implicit global usings (`System.Windows.Forms`, `System.Drawing`) that collide
  with WPF's own `Application`/`Brush`/`Color`/`TextBox`/etc. across the whole project — resolved
  by referencing `Microsoft.WindowsDesktop.App.WindowsForms` directly via `<FrameworkReference>`
  instead, which pulls in `ColorDialog` without the colliding implicit usings.
- `dotnet build` clean (0 warnings), `dotnet test` 29/29 (2 new `SetMatchPreview` tests; existing
  `SetColorTheme` test updated for `HomeAccent`/`AwayAccent`). Verified live: launched the
  Controller and confirmed the pre-match `/overlay/api/scoreboard/state` snapshot already reflects
  the ViewModel's default game type/race-to (proving the preview path fires from construction, not
  just on edits) and that the served HTML/CSS/JS contain the team-name markup, the score-bar
  dependency logic, and the home/away accent split. Manually operating the new color-picker dialog
  and confirming the visual result in a browser is still owed by the developer (no GUI-automation
  tooling available in this environment).

### 2026-08-14 — Fixes: shooter-pointer direction, realistic ball rendering, live style preview

- Fixed the shooter-indicator triangle's direction: `shooter-pointer-home`/`shooter-pointer-away`
  had their `border-left-color`/`border-right-color` swapped, so each pointer's tip pointed *into*
  the center Race-To segment instead of out toward the shooting player's name/score. Corrected in
  `scoreboard.css`.
- Balls (both the Overlay's ball tracker and the Controller's ball buttons) now render like real
  pool balls instead of flat colored circles with plain text: a white number badge in the center,
  and for 9-15 a white ball body with a colored stripe band instead of a solid fill. Overlay:
  `ballElement()` in `scoreboard.js` builds a `.solid`/`.stripe` div with a nested `.ball-badge`
  span, using a `--ball-color` custom property; `scoreboard.css` draws the stripe via a
  `background-image`/`background-size` band clipped to the circle. Controller: `BallItemViewModel`
  gained an `IsStripe` (`Number > 8`) property; `BallButtonStyle` in `Themes/Default.xaml` now
  clips a `Grid` to a circle and layers a stripe-band `Rectangle` (shown only when `IsStripe`) or a
  solid `Ellipse`, plus a white badge `Ellipse` and the number `TextBlock` on top.
- `ScoreboardStyle` changes (corner roundness, scale, glossy/flat, end-cap, shooter-indicator mode)
  now apply immediately as the operator adjusts them in Match Setup, instead of waiting for
  "Start Match". `GameViewModel` gained `partial void On<Property>Changed` hooks for each style
  property that call a new `ApplyScoreboardStyle()` helper, which `StartMatch()` and `ResetMatch()`
  now call too (so a match reset re-applies the operator's chosen style instead of leaving the
  overlay on `GameManager`'s freshly-reset defaults until the next match start).
- `dotnet build` clean, `dotnet test` 27/27 (no test changes needed — these were rendering/wiring
  fixes, not new game-logic behavior). Verified live: launched the Controller, confirmed
  `/overlay/api/scoreboard/state`'s `style` field reflects defaults before any match is started
  (proving the live-apply path is reachable pre-match), and confirmed the served CSS/JS contain
  the corrected pointer directions and the new ball-rendering markup. Visually confirming the
  triangle direction and ball appearance in an actual browser is still owed by the developer (no
  screenshot/browser tooling available in this environment).

### 2026-08-14 — Configurable overlay styling + live show/hide animation

- Added `PoolScoreboard.Core.Models.ScoreboardStyle` (`CornerRoundness` 0-100, `OverallScale`
  50-200, `GlossyFinish`, `EndCapStyle`, `ShooterIndicatorStyle`) and `ScoreboardVisibility`
  (`ScoreBarVisible`/`BallTrackerVisible`/`WinnerBannerVisible`), plus the new `EndCapStyle`
  (`Dot`/`Hidden`) and `ShooterIndicatorStyle` (`Triangle`/`Glow`/`Both`) enums. Both live on
  `GameState` with defaults that reproduce the overlay's original hardcoded look exactly, so
  nothing changes visually until an operator touches the new controls.
- `GameManager` gained `SetScoreboardStyle` (clamps `CornerRoundness`/`OverallScale` into range)
  and `SetScoreBarVisible`/`SetBallTrackerVisible`/`SetWinnerBannerVisible` — the style setter
  follows `SetColorTheme`'s "set once at match setup" lifecycle, the three visibility setters
  follow `PocketBall`/`SetCurrentShooter`'s "live, mid-match" lifecycle.
- `/overlay/scoreboard` now reads these through new `ScoreboardStyleDto`/`ScoreboardVisibilityDto`
  fields on `ScoreboardStateDto`. `scoreboard.css`/`.js` apply corner roundness and overall scale
  as CSS-variable multipliers/transforms (`--radius-scale`, `--ui-scale`) rather than fixed
  pixels, add a glossy/flat toggle and a hideable end-cap, add a new triangle shooter-pointer
  (from the 2026-08-14 WNT screenshot) as a selectable alternative to the existing accent-glow —
  see PROGRESS.md's "Pending: WNT visual reference" above — and introduce a generic
  `.sb-hideable`/`.sb-hidden` slide+fade mechanism (inspired by overlays.uno's Billiards
  Scoreboard "scorebug" transitions) applied to the score bar, ball tracker, and winner banner
  independently. The winner banner's own win-detection visibility now combines with the new
  operator toggle (`winnerName present AND operator hasn't hidden it`) instead of the old
  bespoke CSS-only fade, which was removed to avoid two conflicting transitions on one element.
  A `hasRenderedOnce` flag in `scoreboard.js` snaps all three elements to their correct state
  instantly (no animation) on the very first SSE message of a connection — needed because the
  server resends the full snapshot on every reconnect (e.g. an OBS scene switch mid-match), not
  just true first page load.
- `GameViewModel`/`MainWindow.xaml`: a new "SCOREBOARD STYLE" section (roundness/scale sliders,
  glossy-finish checkbox, end-cap and shooter-indicator combo boxes) sits below the existing
  "COLOR THEME" section in match setup, applied via `SetScoreboardStyle` in `StartMatch()`
  alongside the existing `SetColorTheme` call. Three new live-view toggle buttons ("Score Bar" /
  "Ball Tracker" / "Winner Banner") call the three new `GameManager` visibility setters; a new
  `BoolToButtonStyleConverter` picks `ActiveButtonStyle`/`InactiveButtonStyle` to show each
  toggle's on/off state, reusing the existing button styles rather than adding new ones.
- `dotnet build` clean across all four projects; `dotnet test` 27/27 (9 new tests covering style
  defaults, `SetScoreboardStyle` value updates and range-clamping, and the three visibility
  setters). Verified live: launched the Controller and confirmed
  `/overlay/api/scoreboard/state` includes the new `style`/`visibility` JSON fields with correct
  defaults, and that `/overlay/scoreboard`'s HTML/CSS/JS serve the new markup/classes/functions
  (`sb-hideable`, `shooterPointerHome`/`Away`, `--radius-scale`/`--ui-scale`, `flat-finish`,
  `caps-hidden`, `shooter-glow`, `applyStyle`, `setElementVisible`, `hasRenderedOnce`). Full
  visual QA of the animations/sliders in an actual browser is still owed by the developer (no
  screenshot/browser-automation tooling available in this environment).

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
