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

### Ranked follow-ups (2026-11-27) — new ground, most valuable first

Recorded when the overlay-guard note below ran out of items. Each was chosen for what it saves a streamed match,
not for symmetry with work already done.

1. **Match history and a per-player record.** The Known Gaps entry says a finished match is kept nowhere, which is
   the difference between a live display and an app that accumulates value: who won last time, a session's win
   count, a season record. Core already has the pieces (`ResumableMatch` for the snapshot shape, `DurableFile` for
   the write, the roster for identity). *Done 2026-11-27: the record model, the Core archive call and
   `history.json` are wired through an explicit SAVE TO HISTORY, so a finished match is filed once and only when
   the operator says so, and `HistoryWindow` — the History button beside Help — lists them newest first with each
   name's wins and deletes one behind a confirmation.*
2. **Undo that covers everything, not just points.** `GameManager` keeps one stack — who scored each point — so
   `UndoPoint` reverses a point and nothing else. A wrong shooter toggle, ball or group assignment has to be
   corrected by hand mid-stream, which is the most frequent real failure. Bounded command history, not new UI.
3. **Global hotkeys.** Shortcuts are live-view-only, so they need the Controller focused; a real stream has OBS
   focused while the operator scores. `RegisterHotKey` for the same five actions, enabled only during a match.
4. **A shot clock / match timer on the overlay.** The WNT reference the developer chose has a clock and
   `GameState` has no time in it at all. Additive (one field, one overlay element, one control route, one key) and
   it feeds #1 with match duration. Needs a decision: per shot with a reset, or continuous. *Done 2026-11-27, as a
   per-shot countdown: `GameState.ShotClockEndsAt` is a deadline, the overlay ticks it down itself, `T` and
   `/api/control/shotclock/*` start/restart or hide it, a point restarts a running clock, and a new rack or a
   reset hides it. Extended 2026-11-28: both lengths are Match Setup fields, each player can call one extension per
   rack, a ball leaving the table or a shooter toggle restarts the clock like a point does, and a won match hides
   it instead of restarting it behind the banner.*
5. **Tag-triggered release workflow.** Phase 6 packaging is a `dotnet publish` typed by hand, and CLAUDE.md asks
   versions to keep in step with the `v0.<major>.<ui>-beta` tags — a rule nothing checks. A `push: tags: v*` job
   that publishes the single-file win-x64 build and asserts `VersionPrefix` matches the tag. Zero runtime risk.
6. **A test for the startup wiring.** Every extracted piece is tested, but the *order* — profile before window,
   autosave after profile, port before anything binds — is proven only by `verify-live.ps1`, which CI cannot run.
   That ordering has already produced one bug (the profile event deleting the match being restored).
7. **True striped ball rendering.** Ball colors are the documented placeholder; solids and stripes are solid fills,
   so a viewer cannot tell a 3 from an 11. Pure CSS/SVG in `Assets/Scoreboard`, which the asset guard keeps honest.
8. **An opt-in "overlay is stale" cue.** Both pages deliberately keep their last frame when the host dies, which is
   right for a blip and wrong for a dead host: the scene shows confident, frozen numbers and says nothing. After a
   few seconds of a closed stream, mark the page. The one overlay behaviour with no test at all today.
9. **Named setup presets.** Settings are one flat profile in `%AppData%`; an operator with two venues or two overlay
   styles retypes colours and the port each time. A named profile file plus a picker, composing with #1.

Considered and deliberately not queued: a phone/tablet control page (the loopback-only rule forbids it — a real
scope change, not a follow-up); an exported `.streamDeckProfile` (absent by design); re-deriving the pages' JS keys
as a text guard (superseded by the HTTP payload tests); a headless CI end-to-end run (the live check needs a real
window and real sockets).

### Parked 2026-11-27 — the overlay CI guards: where to pick this up

Done and on `master` (CI green at 109 Core + 29 Overlay + 141 Controller, 0 warnings):

- Source guards in `Controller.Tests`, all sharing `RepositorySources`: `NoHardCodedOverlayAddressTests` (no literal
  `51234`, no `127.0.0.1:` + digit in the Controller's markup or C# literals), `OverlayRouteTests` (every
  `/overlay`/`/api` path named in Help, either page script or the health probe is a mapped route, and those four
  sources still name one; so is every `/api/control` path in the two `streamdeck/` markdown files),
  `OverlayLoopbackBindTests` (no wildcard bind, and a *literal* loopback address),
  `NoExternalOverlayAssetTests` (no absolute or protocol-relative URL under `Overlay/Assets`), `LiveCheckUiTextTests`
  (the AutomationId, control names and banner text `verify-live.ps1` drives still exist — matched by equality against
  the attributes that become control names, never a tooltip or a comment).
- `PoolScoreboard.Overlay.Tests` (net8.0, `Microsoft.AspNetCore.TestHost`) — the HTTP level: every page and asset with
  its content type, payloads checked against the `state.…` paths the embedded pages read, each stream's first `data:`
  frame, a control call pushing a second frame, the icon's 200 bytes and its 404, and all 13 `/api/control` routes'
  effects read back through the state payload.
- `OverlayHost.Build` (host mapped, not started) exists so those tests can use `TestServer`; `Start` is the same list
  plus the loopback address. CI parses `tools/verify-live.ps1` (syntax only) before building.

Re-verified since: on 2026-11-27 `tools/verify-live.ps1` with no `-Scenario` passed all four scenarios — 59
assertions, exit 0 — against the build from `073668b`, so the outstanding full live-check run is closed (see the
Change Log entry of the same date). What follows is what is still open.

Not open from this batch:

- The old "JS keys vs DTO without a server" idea is redundant — the HTTP payload tests do it against the real
  serializer, so don't rebuild it as a text comparison.
- **Script parameter parity** was the last item and is done (2026-11-27): `LiveCheckParametersTests` holds the
  script's `-Port` default to `OperatorProfile.DefaultOverlayPort`, its port-change offset to the offset its own help
  states and to `IsUsablePort`, and the scenario names the script runs to the ones it accepts and the ones CLAUDE.md
  names. It stays catalogued rather than re-adding doc drift: what it catches is a number or a name that moved in one
  place only.

Two things worth remembering from this batch: a mutation script must match the source's real indentation (a failed
anchor silently tests unmutated code), and a presence-only text guard passes on a `ToolTip` or a
`Command="{Binding …}"` name — both were caught only by running the mutation and reading the failure.

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

- Persistence: the operator profile — colour theme, scoreboard style, element visibility and both players'
  name/team/Race-To/end-cap icon — is saved to `%AppData%\PoolScoreboard\settings.json`, and the match in
  progress (scores, game type and Race-To mode, ball groups, pocketed balls, shooter) is saved to `match.json`
  after each change and restored on the next launch, so a crash or a forced restart costs nothing. Neither is a
  record of play: that is `history.json`, described below.
- The roster only remembers players who were on screen for a started match (or still there when the
  window closed). "Delete" beside a picker drops an entry and its icon; renaming means reusing the name,
  and the list is capped at 100. A finished match is filed to `history.json` only when the operator presses
  SAVE TO HISTORY (record model, archive call and save path), and the History window lists them newest first with
  each name's wins and deletes one behind a confirmation, but there is no export, no filter, no bulk clear, and no
  per-player detail beyond the win count.
- The shot clock's two lengths are Match Setup choices that are not saved with the profile, so a relaunch returns
  to 40/30 (the game type and Race-To mode behave the same way); the clock itself is never written to
  `match.json`, so a resumed match starts with no clock, and each side's extension is a per-rack flag with nothing
  on screen showing who has used theirs.
- Ball colors in the Controller's live view are placeholder standard billiard-ball colors,
  not yet informed by the WNT visual reference.
- Flag end-caps are artwork ("Choose Flag..." bundles country and US-state SVGs) stored as the
  player's `EndCapIcon`, now saved and restored with the player. There is still no nationality/flag
  property on `Player`, so nothing chooses a flag from a player's name.
- Settings are one flat profile in `%AppData%`: there is no per-machine/per-profile selection, and
  the icon files are replaced rather than versioned, so a cleared "Choose Icon" deletes the file.
- Keyboard shortcut keys (Q/P/A/Space/N) are a first pass, not confirmed with the developer.

## Change Log

### 2026-11-28 — A new rack resets the clock without starting it

- `ResetBalls` used to clear the clock, so a fresh rack showed nothing at all. It now clears the deadline and sets
  `GameState.ShotClockArmed`: the clock sits on screen at its full length, dimmed and not counting, and the operator
  starts it after the break with `T`, the extension button's neighbour on the deck, or the start route. Nothing else
  starts it — a potted ball and a shooter toggle leave it waiting.
- `StartShotClock` takes the clock out of that state, `ClearShotClock` hides a waiting clock as well as a running
  one (its guard now covers both, so the hide route is not a no-op after a new rack), and a won match and the money
  ball leave neither a deadline nor an armed flag.
- The overlay learned one more state: `shotClockArmed` in the payload draws the full length with no countdown, no
  colour change and 55% opacity, so a waiting clock cannot be read as one already running. The line is full, the
  numbers read the shot's length.
- Mutation-proved, then reverted, seven ways: the armed flag dropped from `ResetBalls` (the clock vanished instead of
  waiting), starting leaving it armed, a reset carrying it onto the setup screen, the clear route ignoring it, a won
  match keeping it, the money ball keeping it, and the payload dropping `shotClockArmed`. The last two mutating
  cleanly meant the first drafts of two tests were wrong — they started the clock before the reset and the win, so
  the flag was already false and the mutation proved nothing. Both were tightened until the mutation failed.
- 0.35.0 -> 0.36.0. 147 Core + 34 Overlay + 170 Controller, 0 warnings. Not checked by hand: the waiting clock has
  not been seen in the window or in OBS.

### 2026-11-28 — The money ball stops the clock

- Potting the ball the rack is counting towards ends the game, so the shot clock now stops with it instead of
  restarting over the end of it. `GameState.MoneyBallNumber` derives that ball from the game type — 8 in 8-ball, 9 in
  9-ball, 10 in 10-ball — so the ball row and the clock read one rule rather than two.
- Both ball routes go through one `GameManager.BallLeftTheTable`, so the Controller's ball buttons and the Stream
  Deck's per-ball toggle cannot disagree: any other ball still restarts a running clock for the next shot, the money
  ball clears it (a null deadline, which is how the overlay hides the clock), and bringing a ball back still leaves
  the clock alone.
- "Stop" is the clock's existing stop: the countdown disappears, exactly as it does for a new rack, a reset or a won
  match. There is no paused state to add, and a frozen number on the overlay would claim a shot is still live.
- Mutation-proved, then reverted: the money-ball branch removed from `BallLeftTheTable` (the clock restarted over the
  end of the rack). Covered from both sides — the Core theory over the three game types, and the Overlay's route test
  reading the payload the page draws from, in the fixture's 9-ball match.
- 0.34.1 -> 0.35.0. 145 Core + 33 Overlay + 170 Controller, 0 warnings. Not checked by hand: no money ball has been
  potted in the running window or in OBS.

### 2026-11-28 — An expired line still says the shot ran out

- The line's fill shrinks to nothing at 0:00, so an expired shot left an empty grey track — indistinguishable from a
  clock that was never started. `.shot-clock.expired` now colours the track itself red, matching the numbers, which
  read a red `0:00`: the shot ran out, and the overlay keeps saying so until the next clock.
- Checked in the browser at 0:00 with the classes the page really applies. No test covers it: the rule is one CSS
  selector on classes the page already toggles, which the HTTP tests keep honest at the payload level but cannot
  see painted.
- 0.34.0 -> 0.34.1. 141 Core + 32 Overlay + 170 Controller still green, 0 warnings.

### 2026-11-28 — The countdown can be a line instead of numbers

- `GameState` gained `ShotClockDisplay` (`Time` or `Line`), set through `GameManager.SetShotClockDisplay` and carried
  by `CarryOperatorStyling` into a reset and the next match — it describes the show rather than the match, so unlike
  the two lengths it is allowed mid-match. The profile captures and applies it, so the choice survives a relaunch
  like the theme and the show/hide settings.
- The Overlay draws both: `scoreboard.js` toggles `line-display` from the payload's new `shotClockDisplay`, renders
  the line's width as the fraction of the shot left against `shotClockSeconds`, and takes its green/red from the
  same `low`/`expired` classes the numbers use — so the line turns red at exactly five seconds, not at a second
  threshold that could drift from the numbers'. An extension pushes the remaining time past the length, so the
  fraction is capped at a full line rather than overflowing the track.
- The operator swaps it with **Shot Clock: Time / Line** at the end of the shared OVERLAY row, which is on both
  screens and persists the profile when pressed. The button names the drawing currently on screen; the Help page's
  OVERLAY bullet explains the swap.
- No new route, so the Stream Deck docs and the route guard are untouched by this one — the display is a look
  setting, not a button an operator would press mid-shot from a deck.
- Mutation-proved, then reverted: the mapper dropping the field (the payload path the page reads), `Clone` dropping
  it, `CarryOperatorStyling` dropping it, and the profile's capture dropping it. Each failed the fact covering it.
  The five-second colour threshold itself lives in CSS/JS and is not unit-tested: it reuses the classes the numbers
  already had, which is the reason it cannot drift.
- 0.33.0 -> 0.34.0. Full suite 141 Core + 32 Overlay + 170 Controller, 0 warnings. Not checked by hand: neither
  drawing has been seen in OBS, and the line's proportions are the page's own arithmetic rather than a measurement.

### 2026-11-28 — The clock's length is the operator's, and each player gets an extension

- Both lengths moved to Match Setup. `ShotClockSeconds` (40) and the new `ShotClockExtensionSeconds` (30) are still
  defaults on `GameState`; `SetShotClockLengths` takes them from the screen, clamps rather than refuses (5-300 s, so a
  slipped digit still leaves a usable clock), and a started match, a reset and a resumed match all carry them, so
  START MATCH cannot quietly fall back to forty. The fields are lost-focus bindings that show back what Core
  accepted, so the screen never claims a length the clock is not using.
- The extension is the player's, called by the operator: `ExtendShotClock` adds the extension time to the clock that
  is already running for the current shooter — more time on the shot in progress, not a fresh shot. One per player
  per rack, held by two flags on the state, and a new rack hands both of them one back. A call with no clock running
  is ignored rather than spent. Reached by `/api/control/shotclock/extension`, `E` on the live view, and a CALL
  EXTENSION button beside the shooter toggle.
- The restart rules widened to what the developer asked for: a ball leaving the table (either ball route) and a
  Home/Away shooter toggle now restart a running clock the way a point does. Putting a ball back does not — that is
  the operator correcting a mistake, not a shot — and marking a ball already marked pocketed is not one either.
- A point that wins the match now clears the clock instead of restarting it, and both `StartShotClock` and
  `ExtendShotClock` are ignored while a winner is up: a countdown behind the winner banner was timing nothing.
- The route guard failed first, naming `/api/control/shotclock/extension` as a route no button could be built for,
  until both Stream Deck docs gained their row — the guard doing exactly its job. The Help page gained the endpoint
  and the `E` shortcut, so its pinned URL count moved 15 -> 16.
- Mutation-proved, then reverted, eight ways: the winning point restarting instead of clearing, the per-rack
  used-flag not consulted, the shooter toggle leaving the clock counting, pocketing leaving it counting, putting a
  ball back restarting it, the match not carrying the setup lengths, the win guard dropped from `StartShotClock`, and
  the extension computed from now instead of added to the deadline. Each failed the fact that covers it.
- 0.32.0 -> 0.33.0. Full suite 139 Core + 31 Overlay + 169 Controller, 0 warnings, and the extension route is read
  back through the payload the page renders. Not checked by hand: neither length, nor the button, has been used in
  the running window or seen in OBS.

### 2026-11-27 — A per-shot countdown on the overlay

- Decided, since it was open: **per shot, with a restart**, not a continuous match clock. `GameState` gained
  `ShotClockEndsAt` (a deadline, or null for no clock) and `ShotClockSeconds` (40, fixed), and `GameManager` gained
  `StartShotClock`/`ClearShotClock`. The `now` comes from a clock injected into the constructor, so the deadline
  arithmetic is asserted exactly rather than slept through.
- The deadline is the design: one frame when the clock starts, and the page counts down on its own — forty frames a
  shot would be a lot of traffic for a number a browser can subtract. `scoreboard.js` re-reads the instant on every
  state frame and ticks four times a second, reads red under five seconds, and hides the element when the payload
  carries null rather than sitting at 0:00.
- A running clock restarts on every point, because a point ends one shot and begins the next; a clock nobody started
  is never started by a point. A new rack and a reset hide it — a countdown belongs to a game still being played —
  and a repeated clear raises nothing, so a Stream Deck button held down cannot flood the pages.
- The operator starts it with `T` (live view) or `/api/control/shotclock/start`, and hides it with
  `/api/control/shotclock/clear`. Both routes are in the Help window and both Stream Deck docs, which the route
  guard enforces: it failed first with "no button could be built for them: /api/control/shotclock/start,
  /api/control/shotclock/clear" until the docs gained their rows.
- Mutation-proved, then reverted: the setup guard dropped (a clock started at setup), the point restart removed
  (the restarted deadline stopped moving), the new-rack clear removed, and the deadline written as `now` instead
  of `now + ShotClockSeconds`. Each failed the fact that covers it.
- 0.31.1 -> 0.32.0. Full suite 131 Core + 30 Overlay + 168 Controller, 0 warnings. Not checked by hand: the clock
  has not been seen in OBS or in the running window; the payload and the page's reads are what the tests pin.

### 2026-11-27 — The live view stops claiming a save that was deleted

- The save control re-read the file once per match, when its winner appeared, so a record deleted from the History
  window left "Saved to history" on a banner whose match was no longer in the file. The reading moved into
  `GameViewModel.RefreshSavedMatch(state, reread)`: the state path still looks once per match, and `ShowHistory`
  calls it again after the dialog closes, because that is the only moment the answer can change behind a banner
  nobody can see yet.
- Mutation-proved, then reverted: the force re-read ignored (`reread` dropped from the guard), and the lookup
  pinned to true. The first failed `DeletingTheSavedMatch_ClearsTheSavedMark`, the second failed the resumed-match
  fact.
- 0.31.0 -> 0.31.1. Full suite 126 Core + 29 Overlay + 167 Controller, 0 warnings. Not covered by a test: that
  `ShowHistory` makes that call — the dialog is modal, so the refresh is tested through the method it shares.
- Deletion is still final in the other direction: nothing files a match on its own, so the deleted record cannot
  come back until the operator presses the button again or relaunches with the match still won.

### 2026-11-27 — A match is saved when the operator says so, and a saved match can be deleted

- Saving is now a decision. `MatchHistoryRecorder` is gone: nothing files a match on its own. The live view gained
  **SAVE TO HISTORY** beside "Saved to history", shown with the winner banner, enabled only while that match is not
  yet in the file, and gone again after RESET MATCH. `GameViewModel.SaveMatch` archives through the same
  `MatchHistoryStore.TryArchive` the recorder used, so the id rule still stops a second press, a relaunch of the
  same won match and the corrected winning point from making a duplicate.
- Deletion is per match, not a wipe. `MatchArchive.Forget` and `MatchStanding` recounting come from the remaining
  records; `MatchHistoryStore.TryDelete` rewrites the file through the same durable temporary-file move.
  `HistoryWindow` now takes the store rather than a loaded archive, shows a Delete button per row behind a
  confirmation, and reloads list and chips from what is left — so the screen cannot drift from the file. It is
  still read-and-delete only.
- A match saved before a relaunch shows as saved: the view model looks the archive up once per match, when the
  winner appears, instead of remembering the save in memory only.
- Mutation-proved, then reverted: `Forget` not matching the id, `Contains` not matching it, the archive's dedupe
  neutered (twice — Core and view-model facts), `TryDelete` forgetting to write, the resumed-match lookup pinned to
  false, the button left enabled after saving, the reset leaving the saved mark, and the delete confirmation
  ignored. Each failed the fact that covers it.
- 0.30.0 -> 0.31.0. Full suite 126 Core + 29 Overlay + 165 Controller, 0 warnings. Not checked by hand: the save
  button and the Delete column were never seen in the running app; the window tests show it off the visible desktop
  and read it back.

### 2026-11-27 — The match-history checks are written down, and a stale gap is corrected

- `UAT.md` now holds the match-history scenarios (MH-1 to MH-12) with preconditions, steps and expected results,
  written the way they were run by hand when the window landed: the empty state, a first record, newest-first
  order, one record per match after later changes and across a relaunch, a rematch, a corrected winning point, one
  player across spellings, a split race with an unnamed side, read-only and modal behaviour, a damaged file, and
  that the setup and live-view screens did not move. It also says how to reset the history between runs.
- Corrected the Known Gaps bullet that still said scores, ball groups and pocketed balls were in-memory only: they
  have been in `match.json` since the crash-resume work, and the bullet now says so, keeping `history.json` as the
  separate record of play. The stale wording survived several passes because nothing reads Known Gaps; it is the
  sort of claim worth checking whenever it is touched.
- Docs only; no code changed. No version bump.

### 2026-11-27 — A read-only window for the recorded matches

- The history can be read. `HistoryWindow` lists the archive newest first — when, both names, the score and who
  won — with `MatchArchive.Standings()` above it as one chip per name and its win count. It is a window of its
  own, opened from a History button beside Help, so the live view and the setup screen are untouched: no screen
  toggle, no XAML moved, and nothing new in the view model beyond the command that opens it.
- Standings are computed in Core, where they can be tested: the winner is credited rather than the side that
  lost, the most wins come first with names alphabetical within a tie, one player across two spellings counts
  once under the newest spelling, and a nameless side credits nobody. Names are compared ignoring case and
  surrounding space the way the roster compares them; a record carries no roster id, so deleting a saved player
  cannot take their wins with it.
- Formatting lives in the window, not in the record: local time, and a dash for a side the operator never named.
- Mutation-proved, then reverted: crediting the loser, reversing the wins order, comparing names case-sensitively,
  showing the list oldest first, and pinning the empty-history placeholder to hidden — each failed the fact that
  covers it.
- 0.29.0 -> 0.30.0. Full suite 123 Core + 29 Overlay + 161 Controller, 0 warnings. Not checked by hand: the window
  was never opened in the running app; the tests show it off the visible desktop and read it back.

### 2026-11-27 — A finished match is filed once, through the history store

- The history is wired in. `MatchHistoryRecorder` (Controller) starts in `OnStartup` immediately after
  `MatchAutosave.Restore()` and subscribes to `GameStateChanged`, so a match played in the window, on the keyboard
  or through the Stream Deck is filed by the same path. It touches `MatchHistoryStore.TryArchive` only when the
  state carries a winner. No XAML, layout or view-model change.
- Filed once, without bookkeeping in the caller: `GameState.MatchId` is assigned by `InitializeGame` and carried
  into `ResumableMatch`, so `MatchArchive` can tell a repeated event for a match it already holds — the winner stays
  in state while the banner is up, so a ball toggle, a rack reset and a relaunch restoring the same match all
  arrive with it — from a rematch that ends with the same names and score. The check reads the file, so it survives
  a restart; the recorder also remembers the last id it filed so the common case costs no file read.
- A state with no id — hand-made, or from a file older than the field — is still filed: a possible duplicate
  record beats a silently missing win.
- Mutation-proved, then reverted: the archive's id check neutered, and the same-match and relaunch facts failed;
  `MatchRecord` no longer copying the id, and dedupe never applied (four facts failed, the rematch one included);
  `RestoreMatch` assigning a fresh id, and `Restore_KeepsTheMatchIdSoTheSameMatchIsNotFiledTwice` failed;
  `InitializeGame` leaving the id empty, and both id facts failed with "Strings are equal".
- 0.28.0 -> 0.29.0. Full suite 119 Core + 29 Overlay + 159 Controller, 0 warnings. Still open: nothing reads the
  history back — no screen shows it, and a record is per match rather than per player. Also fixed here: the
  previous entry had repeated the Change Log heading, leaving two of them.

### 2026-11-27 — A finished match becomes a record, in Core and in a crash-safe file

- First slice of the first ranked follow-up, and the gap the Known Gaps section has carried the longest.
  `MatchRecord` (Core) is one finished match as it was played — both sides' names, teams and own Race-To targets,
  the final scores, game type, mode, when the winning point landed and who won — and `MatchArchive` (Core) files it
  newest first under a 200-record cap. Both are I/O-free and unit-tested, the way `ResumableMatch` and
  `PlayerRoster` are.
- What counts as finished is the state's own winner, not a second comparison against the Race-To targets:
  `GameManager` recomputes the winner after every score change, so a corrected score takes it away again and a match
  in progress has none. That is also why the archive call returns null instead of writing a blank entry.
- `MatchHistoryStore` (Controller) keeps it as `history.json` beside `settings.json`, written through `DurableFile`
  and read-modified-written under one lock, so a crash or a power cut leaves the previous history rather than a
  truncated one. An unreadable file is moved to `history.corrupt.json`; one written by another version is left in
  place and ignored, the rule `match.json` already follows.
- Mutation-proved, then reverted: dropping the winner condition recorded an unfinished match ("Value is not null");
  loosening the cap kept 201 ("Values differ"); appending instead of inserting lost the newest-first order (two
  facts failed); deleting an unreadable file instead of keeping it failed the recovery test; and reading a file
  with an unknown version failed with "Collection was not empty".
- Not wired yet, as asked: nothing calls `TryArchive`, so the app does not record matches and no UI or `App` code
  changed. New functionality rather than tests only, so 0.27.0 -> 0.28.0.

### 2026-11-27 — The live check's ports and scenario names are held to the app

- The parked note's last open item. `tools/verify-live.ps1` is the second place the ports are written down — it
  launches the Controller on a port of its own and moves the overlay to a second one — and CI only proves it parses,
  so a default that drifted from `OperatorProfile.DefaultOverlayPort` would be found at the next manual run, if at
  all. `LiveCheckParametersTests` reads the script instead of repeating it: the `-Port` default has to be
  `DefaultOverlayPort` (otherwise every scenario launches on a port the app never binds), the port-change step's
  `$Port + 7` has to agree with the offset its own help states and land on a port `IsUsablePort` accepts, and the
  scenarios it runs have to be the ones `-Scenario` accepts and the ones CLAUDE.md names.
- Every value is extracted, and every extraction asserts it matched exactly once: a guard that quietly read an empty
  set would pass for the wrong reason, which is how the first draft of `LiveCheckUiTextTests` was fooled.
- Mutation-proved, then reverted: `-Port = 51235` failed naming both ports; `$Port + 8` failed with "moves 8 above
  -Port, but its help says 7"; renaming `leftover` to `leftovers` in the run list only failed with "never run:
  leftover; not accepted: leftovers"; removing one scenario's backticks from CLAUDE.md failed with "CLAUDE.md never
  names them: banner-retry"; and a duplicated `[int]$Port` line failed the extraction with "no longer states its
  -Port default once, but 2 time(s)".
- Tests and docs only: no version bump.

### 2026-11-27 — Ten green re-runs of the window test's job, on the commit that fixed it

- Two passes after `9c2dcaf` did not answer the question the flake raised: at the rate it was failing before (~2
  failures in 6 runs, about 1 in 3), two passes are the expected outcome 44% of the time whether or not anything was
  fixed. So the job was re-run until that commit had ten attempts behind it.
- Ten runs of run 46072310460's `build-and-test` job on `9c2dcaf` (`attempt=10`) — the original push and nine
  `gh run rerun`s: **10 passed, 0 failed**. Every attempt is a fresh execution: the job rebuilds and runs all three
  test projects, and the last attempt reports `Total tests: 141`.
- Ten consecutive passes are about 1.7% likely if the old ~1/3 rate still held, so the flake is very unlikely to be
  there at its old rate. It is not proof it is gone: ten runs put a rough 26% upper bound on any remaining per-run
  failure rate (95% confidence), a rarer failure would still slip through, and every attempt shares one runner image
  and one commit rather than an independent environment.
- Kept as the check on a recurrence: in the passing attempt this assertion took **14 ms** (`HelpWindowTests`, on the
  same shared thread, 4 s) against the 10-19 s the old flake spent failing, so a repeat is still legible from the
  duration column, and the bounded waits name their stage — `did not run its work within 15s` for a thread that never
  ran, versus a window that was shown and not reported.
- Docs only; no code changed. No version bump.

### 2026-11-27 — The window test shares the WPF thread that already shows windows

- CI on the trim commit (`28a280a`) failed the same test again, and the diagnostic added an hour earlier named the
  stage: `Windows reported no window 15s after the window thread started (Show() returned: no, thread state:
  Background)` — `Window.Show()` itself never returned on that agent — while the next push of identical test code
  passed. The earlier change had only made the flake legible; the test still created the fragile thing.
- The difference from `HelpWindowTests`, whose window has never stalled there, was the thread. That test shows its
  window on a thread that has an `Application` and the app's theme merged; this one created a fresh STA thread with
  neither, in a process that already had an `Application`. WPF allows one `Application` per process, so
  `WpfTestThread` is now the single UI thread both tests use — extracted from `HelpWindowTests` rather than duplicated —
  and the window test shows its window there. `TestWindow` (its own thread, dispatcher reference, start handshake and
  disposal) is gone.
- Calls into that thread are bounded (`InvokeWithin`): a `Show()` that wedges fails the assertion with
  `the WPF test thread did not run its work within 15s` instead of holding the test run open, and one that throws
  surfaces as itself (`System.InvalidOperationException : mutation: Show() failed`), not wrapped in an
  `AggregateException`. The assertion still polls `StaleController.Observe(ProcessId)` and says what it saw.
- Tried and rejected on the way: showing the window in a child process (`notepad`), which on this machine starts as the
  packaged app whose `MainWindowHandle` stays 0, so it is not a dependable subject for "a process with a window".
- Mutation-proved, then reverted: a throwing `Show()` and a `Show()` sleeping 40 s both failed as described, the second
  in about 17 s rather than 40. Full suite 109 Core + 29 Overlay + 141 Controller, 0 warnings. Tests only: no version
  bump.

### 2026-11-27 — The window test waits on the window, not on Show()

- `StaleControllerTests.Observe_SeesTheWindowAProcessShows` failed once on CI with "the test window never appeared"
  after 10 s, then passed on a re-run of the same commit — 122 ms in the passing run, so the deadline was not being
  grazed by a slow machine. The helper finished one handshake only after `Window.Show()` returned, and from the test
  thread three different things looked identical: the thread never started, `Show()` threw, or `Show()` stalled.
  `Show()` is the fragile half — it creates the process's first window (theme, HWND) and, with the default
  `ShowActivated`, asks the session for the foreground, which a CI agent's input queue can withhold indefinitely.
- The wait is now on what the test asserts. `TestWindow.Start` only starts the thread — starting it is not the
  subject — and the window is shown with `ShowActivated = false`, the way `HelpWindowTests` already shows its own;
  the test polls `StaleController.Observe(ProcessId)` until Windows reports the window. A `Show()` that throws is
  carried back through a `TaskCompletionSource` and rethrown with its own stack; a `Show()` that has not returned
  by the deadline fails naming the stage (`Show() returned: yes|no`, thread state). The 15 s
  budget is the old 10 s + 5 s, not a longer one.
- The handshake signal is no longer a `ManualResetEventSlim` in a `using`: a wait that timed out used to dispose it
  while the UI thread might still `Set()` it, turning a failed test into an `ObjectDisposedException` on that thread.
  Cleanup ends the dispatcher and joins the thread with a bound, so a stuck `Show()` cannot hang the run after the
  assertion has already failed.
- Mutation-proved, then reverted: a `Show()` that throws failed at once with
  `System.InvalidOperationException : mutation: Show() failed`; a `Show()` that sleeps past the deadline failed with
  `Windows reported no window 15s after the window thread started (Show() returned: no, thread state: Background,
  WaitSleepJoin)`. Both restored, then 8 consecutive runs of the test and the full suite passed.
- Found while proving it: `ShowInTaskbar = false`, which `HelpWindowTests` uses, gives the window an owner, and
  `Process.MainWindowHandle` only considers a process's unowned visible windows — the first version of this test could
  never have passed with it, so the test window stays in the task bar.
- Reviewed once more after pushing, and cut what the test never reads: the start handshake (its own
  `TaskCompletionSource`, the dispatcher and window fields, the "did not start" throw), the dead-thread branch, and the
  queued `Close` with its bounded wait and `AggregateException` catch. `Dispatcher.FromThread` finds the dispatcher at
  cleanup time, a thread that exits takes its window with it, and the timeout message already carries the thread state
  — so the helper is now the thread plus one task that completes when `Show()` has returned or thrown. Re-proved after
  the cut: the throwing `Show()` and the stalled one still fail with their own messages, 8 consecutive passes and
  109/29/141.
- Full suite 109 Core + 29 Overlay + 141 Controller, 0 warnings. Tests only: no version bump.

### 2026-11-27 — The overlay's base address announces the port it follows

- `GameViewModel.OverlayBaseUrl` is computed from `OverlayPort`, and the `[ObservableProperty]` generator raises only
  for the port, so the address never announced its own change. Nothing bound to it yet — `ShowHelp()` reads it fresh
  at click time, which is why this was invisible — but the first XAML binding that did would have kept naming the old
  port until something unrelated refreshed it.
- The fix is one `OnPropertyChanged(nameof(OverlayBaseUrl))` at the top of `OnOverlayPortChanged`, ahead of the early
  return for a view model with no session: that shape, which the tests build, has an address that moves too.
- The test was written first and failed on the unfixed code — `Assert.Contains() Failure: Item not found in
  collection` — then passed with the line in place. It moves the port through a fake session and asserts the address
  was announced and reads `http://127.0.0.1:51240`, so it cannot pass on the notification the port already raised.
- Full suite 109 Core + 29 Overlay + 141 Controller, 0 warnings. A notification nothing binds to yet, so no visible
  change: no version bump.

### 2026-11-27 — Every mapped control route has to stay in the Stream Deck docs

- The guard only went one way: a path named in `streamdeck/README.md` or `SETUP.md` had to be a mapped route. Deleting
  a table row — or a whole table — therefore passed, because the check had one less path to look at. The reverse fact
  now holds every mapped `/api/control` route to the two docs between them: README carries all thirteen, SETUP the
  eleven it walks through, and a route neither names fails naming its own path.
- Mutation-proved, then reverted: deleting README's `Home -1` row (`/api/control/score/home/subtract`, which SETUP
  does not list) failed with `these mapped control routes are in neither Stream Deck doc, so no button could be built
  for them: /api/control/score/home/subtract`, while the path→route fact and the coverage fact both stayed green.
  Restoring the row returned 5/5.
- Full suite 109 Core + 29 Overlay + 140 Controller, 0 warnings. Tests only: no version bump.

### 2026-11-27 — The Stream Deck mapping is held to the mapped routes

- The two files the operator follows — `streamdeck/README.md`'s endpoint table and `SETUP.md`'s walkthrough — spell
  every control endpoint again, as full URLs. They were the one place `OverlayRouteTests` did not read, so renaming a
  `/api/control` route left a dead button with CI green. The guard now reads both and fails on any `/api/control/…`
  path there that is not a mapped route.
- Two shapes those docs use needed handling: the ball number is `<N>` in `SETUP.md` and `N` in `README.md`, so `<…>`
  joins a route's `{…}` and the Help page's capital `N` as a placeholder for one segment; and `/api/control/*` names
  the family rather than an endpoint, so a wildcard tail is skipped. Everything else still has to line up, which is
  where a typo shows.
- A second fact covers the guard itself: if either file stops naming a control path, it fails naming that file
  instead of passing on nothing.
- Mutation-proved, then reverted. Renaming `rack/new` → `rack/fresh` in `ControlEndpoints.cs` failed with
  `streamdeck/README.md names /api/control/rack/new; streamdeck/SETUP.md names /api/control/rack/new` (and took the
  existing `/overlay`-path fact with it, as it should). Turning `README.md`'s `api/control` into `api%control` failed
  only the coverage fact — `… stopped covering them: streamdeck/README.md` — while the route fact still passed.
  Reverting both restored 4/4.
- Full suite 109 Core + 29 Overlay + 139 Controller, 0 warnings. Tests only: no version bump.

### 2026-11-27 — The full live check runs green on the guarded build

- `tools/verify-live.ps1` with no `-Scenario`, against the build from `073668b`: all four scenarios passed, 59
  assertions, no `FAIL` line, exit 0. This is the run the parked note had left outstanding — the previous full run
  predated the `OverlayHost.Build` split and the whole HTTP-test batch, so nothing had driven the real window and
  sockets since.
- `port-change` (24): the field moves the overlay to 51241 with no restart, 51234 stops answering,
  `settings.json` records it, a relaunch returns on 51241, Help lists the overlay on 51241 and shows nothing on
  51234, and a foreign listener on the *configured* port is named in the banner with Retry recovering.
  `crash-resume` (12): 2-1 survives a `Stop-Process` kill with no `OnExit` and the file is not deleted at startup,
  then RESET deletes it. `banner-retry` (11) still names the foreign holder and recovers on Retry. `leftover` (12)
  still leaves the windowed copy from pre-guard `1acddbd` alone and names its PID, then closes it and takes the
  port once its window is hidden.
- The two checks that mattered most here still bite: the Help window is read on a non-default port and asserts no
  stale `51234` (the one place a hard-coded overlay URL could hide), and the `leftover` scenario's worktree build of
  `1acddbd` succeeded, so the single-instance guard is checked against a genuinely older binary, not a mock.
- The run cleaned up after itself — `git status` clean, its temporary worktree gone, no listener on 51234, and the
  operator's `%APPDATA%\PoolScoreboard` restored with their own `settings.json`. Checks only: no version bump.

### 2026-11-27 — Every Stream Deck route's effect is read back through the payload

- The HTTP tests checked `score/home/add` and nothing else, so eleven control routes could have been no-ops (or moved
  the wrong side) with CI green and only a Stream Deck button to notice. All thirteen are now covered, each asserted
  through `/overlay/api/scoreboard/state` — the payload the pages render, not the manager's internals.
- Score controls are one theory over home/away × add/subtract with *both* sides arranged at one, so a no-op, the
  wrong side and a flipped sign are each distinguishable. Undo is arranged H, H, A so it has to remove the away point
  (the last), not just any point. The shooter control is asserted to flip and flip back. Ball groups are a theory over
  the four routes, after switching to 8-ball, asserting the named group *and* the complement on the other side. The
  ball toggle, the 400 for a ball number outside 1–15, new rack (clears the tracker, keeps the score) and reset
  (scores, groups and balls cleared, chosen game type kept) each have their own fact.
- Mutation-proved, then reverted: swapping the two subtract routes' sides failed both subtract cases
  (`Expected: 1 / Actual: 0` and the reverse), and making `shooter/toggle` a no-op failed with
  `Expected: False, Actual: True`. Reverting restored 29/29.
- Full suite 109 Core + 29 Overlay + 137 Controller, 0 warnings. Tests only: no version bump.

### 2026-11-27 — The new stream and icon assertions are mutation-proved

- Broke the scoreboard stream's first frame (`Serialize` returning `"{}"`): the frame test failed naming all 23 paths
  the page reads (`raceToMode, home.raceToTarget, … visibility.winnerBannerVisible`). Broke the icon route
  (`Results.Bytes(Array.Empty<byte>(), …)`): `Assert.Equal() Failure: Collections differ` with `Expected: [137, 80, 78,
  71, 13, ···]  Actual: []`. Both reverted.
- New `AControlCallPushesTheNextStateOnTheOpenStream`: the stream was only tested at connect, but a page that never
  receives the second frame shows the score from connect time until it reloads. The test reads the first frame, calls
  `/api/control/score/home/add`, reads the next frame off the same body and asserts the score moved by one.
- Reading consecutive frames needed a small `EventFrames` reader: it keeps the bytes that arrived past the frame it
  returned (one read can span two frames), and scans bytes rather than text so a character is never decoded across a
  read boundary.
- Mutation-proved too: making the stream stop after its snapshot failed that test immediately rather than waiting out
  its 10 s timeout, because the response ends and the second read sees EOF.
- Full suite 109 Core + 15 Overlay + 137 Controller, 0 warnings. Tests only: no version bump.

### 2026-11-27 — The stream's first frame and the icon's 200 are tested too

- Two gaps the HTTP tests left. The streams were only checked for status and content type, which is the wrong half:
  an `EventSource` page never requests `/overlay/api/scoreboard/state`, so what it renders comes from the first
  `data:` frame. Both streams now have their first frame read off the response, parsed, and held to the same `state.…`
  paths their pages read — the assertion walks the frame's own JSON, so a frame that is empty, malformed, or carrying
  the other page's payload fails.
- The icon route only had its 404 tested. `APlayerIconIsServedAsItsOwnBytes` now sets a real data URI through
  `SetPlayerDetails` and asserts 200, the stored mime type, and the exact bytes back.
- Reading a frame needs the body, which `HttpClient.Timeout` does not cover once headers have arrived with
  `ResponseHeadersRead` — so both stream cases carry their own 10 s `CancellationTokenSource` and still cannot hang a
  runner.
- Full suite 109 Core + 14 Overlay + 137 Controller, 0 warnings. Tests only: no version bump.

### 2026-11-27 — The overlay's routes and payload get an HTTP-level test

- New `PoolScoreboard.Overlay.Tests` (net8.0, `Microsoft.AspNetCore.TestHost`): the guards in `Controller.Tests`
  read source text and prove the strings agree, which cannot tell a working route from a registered one. These 12
  tests drive the real endpoints over HTTP with no socket bound — nothing to leave behind on a runner.
- `OverlayHost.Build` is the host with every page and endpoint mapped but nothing started; `Start` is that plus the
  loopback address and `app.Start()`. One mapping list serves the app and the tests, and the split is what makes
  `UseTestServer` possible without touching production wiring.
- Covered: all six pages/assets answer 200 with their content type; the scoreboard and cue-ball payloads carry every
  `state.…` path their pages read (the paths are extracted from the embedded assets the server actually serves, so
  renaming a DTO property fails with the path the page wanted); `home/add` moves the score the page renders; the
  cue-ball contact round-trips through the request and payload names the page uses; an unknown player 404s; the SSE
  route opens as `text/event-stream`.
- Mutation-proved, then reverted: `PlayerDto.Score` → `Points` failed with
  `the payload no longer carries them: home.score, away.score` (and took the canvas/control tests with it), and
  turning `/overlay/cueball` into `MapPost` failed with `Expected: OK, Actual: MethodNotAllowed`. Reverting restored
  12/12.
- Full suite 109 Core + 12 Overlay + 137 Controller, 0 warnings. Tests plus a no-behaviour-change split of
  `OverlayHost`: no version bump.

### 2026-11-27 — The live check's grip on the window's text is now a CI test

- `LiveCheckUiTextTests` (2 facts) covers what the parse check cannot: that the script is valid PowerShell says
  nothing about the text it looks for. It reads the literals the script hands UI Automation —
  `Invoke-WindowButton` labels (`Retry`, `START MATCH`), `Wait-WindowText` patterns and their `|` alternatives
  (`overlay is off`, `could not start`), `NameProperty` lookups (`Retry`, `Help`, `PoolScoreboard Help`) and
  `AutomationIdProperty` lookups (`OverlayPortBox`) — and fails if the Controller stops showing any of them. The
  literals come from the script, not from a list in the test, so the guard follows it; each extraction asserts it
  found something, so a rewrite of the script's helpers fails loudly instead of guarding an empty set.
- The first draft was too loose, and the mutation run showed it: it searched the Controller's sources for each word,
  so renaming the Retry button still passed — `Retry` survived in a `ToolTip` and in
  `Command="{Binding RetryOverlayCommand}"`, neither of which is a control name, and `overlay is off` survived in the
  same tooltip. Now control names must *equal* the value of an attribute that becomes one (`Content`, `Text`,
  `Title`, `Header`, `AutomationProperties.*`, `x:Name`), and waited-for phrases must appear in a `Text` value or a
  C# string literal — never in a comment or a tooltip.
- Mutation-proved, then reverted: renaming `Content="Retry"` to "Try Again", rewording the banner to "The overlay is
  not running:", and pointing the script at `'PortFieldBox'` produced
  `button label 'Retry'; element name 'Retry'; automation id 'PortFieldBox'` and `overlay is off`. Reverting restored
  2/2.
- Full suite 109 Core + 137 Controller, 0 warnings. Tests only, no behaviour change: no version bump.

### 2026-11-27 — The overlay's assets are held to "works with no network"

- `NoExternalOverlayAssetTests` (2 facts) covers the one rule in CLAUDE.md that CI never looked at: the overlay must
  render with no network, and nothing was checking the six files under `PoolScoreboard.Overlay/Assets`. An absolute
  URL anywhere in them now fails, and so does a scheme-less `//host/…` reference in an attribute, `url(…)`,
  `@import`, `fetch(` or `EventSource(` — that form has no scheme but still resolves off this machine.
- The rule is the whole scheme rather than a list of CDN hosts: no asset needs one today, so a host list would only
  be a thing to keep up to date. If a data-URI SVG ever needs `xmlns="http://www.w3.org/2000/svg"`, that namespace is
  an identifier no browser fetches and belongs in the test as a precise, documented exception.
- Mutation-proved, then reverted: a `fonts.googleapis.com` stylesheet in `scoreboard.html` failed naming the URL, and
  an `@import url(//cdn.example.com/reset.css)` in `cueball.css` failed naming the reference. Reverting restored 2/2.
- Full suite 109 Core + 135 Controller, 0 warnings. Tests only, no behaviour change: no version bump.

### 2026-11-27 — The five copies of an overlay path are now checked against each other

- `OverlayRouteTests` (2 facts). `Endpoints/*.cs` chooses the 26 routes; `HelpWindow.xaml` (13 URLs),
  `scoreboard.js`, `cueball.js` and `OverlayHostSession`'s health probe each spell paths again, and nothing connected
  them — a rename left CI green while the probe reported "overlay stopped serving" every 3 s, Help advertised 404s
  and the OBS pages retried forever. The guard reads the mapped routes and fails on any used path that is not one,
  comparing segment-wise: a route's `{number:int}`/`{id}` and the Help page's `N` placeholder match a single segment,
  everything around them must still line up. A second fact fails if one of those four sources stops naming a path at
  all, so the check cannot pass for the wrong reason.
- `OverlayLoopbackBindTests` (2 facts). The Overlay's `UseUrls("http://127.0.0.1:…")` is the only thing keeping this
  app off the network, and no live check could catch a change — they all connect from this machine. So: no wildcard
  (`0.0.0.0`, `AnyIP`, `http://+`, `http://*`, `https://*`, `[::]`) anywhere in the Overlay's sources, and a *string
  literal* handing Kestrel a loopback address. Literals rather than raw text matters: the class comment already says
  "127.0.0.1 only", and a comment must not be able to stand in for the address actually asked for.
- Both scan through a new shared `RepositorySources` (repository root walk, `bin`/`obj` filter, string-literal
  regex); `NoHardCodedOverlayAddressTests` was moved onto it with no change in what it checks.
- Mutation-proved, each reverted: renaming `/overlay/api/scoreboard/state` to `…/snapshot` failed the route guard
  with `OverlayHostSession.cs uses /overlay/api/scoreboard/state`; widening the bind to `0.0.0.0` failed both bind
  facts; a `/overlay/cue-ball` typo in `HelpWindow.xaml` failed the route guard naming the markup. Reverting restored
  6/6.
- Full suite 109 Core + 133 Controller, 0 warnings. Tests only, no behaviour change: no version bump.

### 2026-11-27 — Two guards: the overlay address can't be hard-coded, and the live check can't rot unnoticed

- `NoHardCodedOverlayAddressTests` (2 tests) fails if the Controller's own sources hard-code the overlay address or
  the default port. Markup is checked as raw text; C# only inside string literals, so `$"http://127.0.0.1:{Port}/…"`
  (the Help base URL and every banner message) passes while an address with a port baked in (`127.0.0.1:` followed
  by a digit) or a literal `51234` fails. Comments stay out of scope — they name both, and they are documentation
  rather than behaviour. Proven by mutation: a `$"http://127.0.0.1:51234"` in `GameViewModel` and a `Title="… 51234"`
  in `MainWindow.xaml` each failed their test, and reverting them restored 2/2.
- CI now parses `tools/verify-live.ps1` (`Parse the live-check script`, `shell: pwsh`). The script drives the real
  window and sockets, so it can never run on a runner; a syntax error would otherwise sit until the next manual run.
  On failure the step annotates the file, line and message and exits non-zero. The exact workflow block was run
  locally both ways: `tools/verify-live.ps1 parses.` (exit 0) on the real script, and
  `::error file=tools/verify-live.ps1,line=2::Missing ')' in function parameter list.` (exit 1) on a deliberately
  broken copy. The pre-push hook runs the same check when PowerShell is on PATH and says so when it is not, keeping
  the hook's "mirrors CI" promise true.
- Writing the step turned up a real bug in its first draft: `foreach ($error in $errors)` collides with PowerShell's
  read-only `$Error` automatic variable, so the loop threw instead of reporting. Renamed to `$problem` and re-tested
  before committing.
- Full suite 109 Core + 129 Controller, 0 warnings. Tests and CI wiring only, no behaviour change: no version bump.

### 2026-11-27 — The App port wiring gets tests, and App gets two lines shorter

- The last part of the port work that only the live check covered was in `App`: applying a saved port before
  anything binds, and writing the port in use when the profile is saved. Both now live in
  `Persistence/OverlayPortSettings`, extracted the way `MatchAutosave` was. `ApplySavedPort(profile)` normalizes and
  hands the port to the session; `RecordPortInUse(profile)` stamps the session's port onto a profile about to be
  written. `App` keeps what only it can know — the ordering, one call before `OverlaySession.Start` and one inside
  `SaveOperatorProfile` — and nothing else.
- `OverlayPortSettingsTests` drives both directions with a session that records what it was asked for and binds
  nothing: the saved port reaches the session, a theory covers the range (0, 80, 1023, 65536 → the default; 1024,
  51241, 65535 kept as given), a save records the port the session is on rather than the stale one in the profile,
  and the choice survives apply-then-save. Dropping the normalization from `ApplySavedPort` fails four of the ten, so
  they are not decorative.
- Verified live after the refactor, since this is the wiring a save and a relaunch go through:
  `verify-live.ps1 -Scenario port-change` still passes all 25 assertions — the app comes up on the port the file
  records, saves the one it served on, and Help lists it. Full suite: 109 Core + 127 Controller.
- Refactor plus tests, no behaviour change: no version bump.

### 2026-11-27 — The Help page and the saved port get CI tests

- Two things the live checks had been covering by hand are now asserted on every push. `HelpWindowTests` builds the
  real `HelpWindow` at 51241, reads its 13 URL lines back through the logical tree, and asserts every one carries
  that port and none carries 51234 — so a re-hard-coded address fails in CI, not only when someone remembers to run
  `verify-live.ps1 -Scenario port-change` on a non-default port. `OperatorSettingsStoreTests` round-trips 51241
  through `settings.json` and pins that a file written before the port was a setting (no `overlayPort` key) gives
  `OperatorProfile.DefaultOverlayPort`; the round-trip test it already had never set the port, so the contract was
  asserted nowhere.
- The window test needed two things that are easy to get wrong. A full `pack://` URI in a static initializer throws
  `UriFormatException` because the scheme is only registered once WPF has been touched, so the app's theme is loaded
  by its relative `/Assembly;component/` form. And a window's bindings do not resolve off a constructed-but-unshown
  window — the tree has to be loaded — so the test shows it at (-20000, -20000) with `ShowActivated` off, which gives
  it a handle without taking the desktop.
- Checked that the new test has teeth rather than trusting a green run: hard-coding 51234 back into one Help URL
  line makes it fail (12 URL lines instead of 13), then the line was restored.
- Tests, not product behaviour: no version bump.

### 2026-11-27 — The full live run catches one scenario leaking into the next

- Running `tools/verify-live.ps1` with no `-Scenario` failed three of the four: `port-change` leaves
  `overlayPort: 51241` in `settings.json`, and `crash-resume`, `banner-retry` and `leftover` all assume a Controller
  that opens on 51234 — so each of them launched a Controller already moved to another port and tested the wrong
  thing. The run is only useful as a whole if the scenarios cannot see each other's leftovers.
- Each scenario now starts from no saved `settings.json`: the runner deletes it before every one (the operator's own
  file is the backup taken at the start and restored on exit). That also removes the reset `port-change` carried,
  which only protected itself, and it makes the other three robust to an operator who has saved a port of their own.
- Verified live: the full run with no `-Scenario` now passes all four back to back — 25 assertions in `port-change`
  (`Help shows Scoreboard: http://127.0.0.1:51241/overlay/scoreboard` and `.../cueball`), then `crash-resume`
  answering on 127.0.0.1 and resuming 2-1, `banner-retry`, and `leftover`.
- Developer tooling only, so no version bump.

### 2026-11-27 — The port change becomes a live check, and Help is read on screen

- The port work above had only been proven by hand, in scripts that died with `%TEMP%`. `tools/verify-live.ps1
  -Scenario port-change` now repeats it: starting from no saved settings (so the field has the default to move away
  from), it moves the port from the OVERLAY PORT field to 51241 and asserts the overlay follows without a restart,
  that 51234 stops answering and that `settings.json` records the choice; it kills and relaunches and asserts the
  saved port comes back; it opens Help and asserts the page lists `http://127.0.0.1:51241/overlay/scoreboard` and
  nothing on 51234; then it holds 51241 with a foreign listener and asserts the banner names the *configured* port
  (not the default), offers Retry, and that Retry serves 51241 once the port is free.
- That closes the one gap the port change left: the Help page's URLs had never been read on screen. Doing it needed
  two things the script now carries. UIA's `Invoke` on the button that opens the modal Help window does not run its
  command reliably, so the window is raised the way the operator raises it — minimize and restore to take the
  foreground, focus the button, press space. And the dialog is an automation child of the Controller as often as a
  sibling of it, so it is looked for in both places; searching only the desktop children is what made every earlier
  attempt look like "the window never opened" while Win32 could see it.
- The foreign listener is a shared helper now (`Start-ForeignHolder`, also used by `banner-retry`), and the port
  helpers take the port they watch, so one scenario can work on two. `crash-resume`, `banner-retry` and `leftover`
  were re-run after the refactor.
- Reviewed with fresh eyes after the first green run and cut what the run had made unnecessary: the UIA `Invoke`
  retry behind the keyboard path (which proves reliable once the window has the foreground), the `AppActivate`
  belt-and-braces, two banner assertions that duplicate `banner-retry`'s (the advice rule and that Retry is
  offered — pressing it already proves it), and the magic sleeps, which are now `WaitForExit`.
- Developer tooling only, so no version bump.

### 2026-11-27 — The overlay port is the operator's, not a constant

- 51234 was hard-coded: another program holding it left the overlay and the Stream Deck API dead for the whole
  session, with a banner that could only name the holder. The port is now a setting — an **OVERLAY PORT** field in
  the window's top bar (1024–65535) and a value in `settings.json`.
- `OperatorProfile.OverlayPort` owns the default (51234), `IsUsablePort` and `NormalizePort`; it lives in the profile
  because it is the operator's choice for this machine, not part of a match. `App` hands it to the session before
  anything binds and writes what the session is actually using on every save. A settings file without the value, or
  with one that cannot be bound, falls back to 51234 rather than failing to start.
- `IOverlaySession` gained `Port` and `SetPort`: changing the port stops a running host (a listening socket cannot be
  moved) and clears the failure reason, because the old reason names a port the app no longer uses. The window's
  `OverlayPort` property rebinds and saves on change; a value outside the range snaps back to the port in use instead
  of leaving the field advertising something that was never tried. `OverlayHostSession`'s probe, banner texts and
  leftover-controller lookups all follow the current port.
- The Help window's URLs are built from the port now (`HelpWindow(overlayBaseUrl)` and one `OverlayBaseUrl` binding
  with a `StringFormat` per line), so it cannot list an address the app is not serving on. README and the
  `streamdeck/` setup guide say 51234 is the default and to substitute the configured port.
- Tests (new): `OperatorProfileTests` covers the default, the accepted range and the fallback; `OverlayHostSessionTests`
  covers a failed start re-reported on the newly set port, `SetPort` clearing the old reason, and a serving host being
  stopped by the move; `GameViewModelTests` covers the field rebinding and saving, and an unusable port snapping back.
- Verified live: changed the port to 51241 in the running Controller, the banner cleared, the overlay answered on
  51241 and no longer on 51234, `settings.json` recorded 51241, and a relaunch came up on it. Pointing a second
  Controller at a port a foreign listener held showed that port in the banner.
- Version 0.26.0 → 0.27.0.

### 2026-11-27 — The live checks become a script instead of temp-folder history

- The three things no unit test reaches — a mid-match crash resuming, the overlay banner and its Retry, and the
  leftover-Controller port rule — had only ever been proven with throwaway scripts in `%TEMP%` (`uia-click.ps1`,
  `hide-window.ps1`, `win-mode-retry.ps1`, `pool-block.ps1` ...), which nobody else could run and which died with the
  folder. They are now one script, `tools/verify-live.ps1`, with a scenario per check and a `-Scenario` switch.
- `crash-resume`: starts a match, scores 2–1 over the control API, waits out the debounce, asserts the snapshot holds
  those scores, kills the process with no `OnExit` (that is the crash), relaunches and asserts the overlay serves 2–1
  again *and* that the resume did not delete the file it read, then asserts RESET deletes it.
- `banner-retry`: holds 51234 with a `TcpListener` in another process, launches the Controller, asserts the window says
  the overlay is off, names the port, offers Retry and does *not* give the close-that-window advice meant for this
  app; frees the port, presses Retry and asserts the overlay serves and the banner is gone.
- `leftover`: builds a Controller from `1acddbd` (before the single-instance guard) in a temp git worktree and runs it.
  The current build must refuse to close it while it has a window — asserting the process is still alive and the
  banner carries "Close that Controller window, then choose Retry" — then hiding its window (handle 0) and pressing
  Retry must close it and take the port. Both halves of the rule in one run.
- Safety, because this drives the real app over the operator's own `%APPDATA%`: the profile folder is copied first and
  restored afterwards, only processes the script started are stopped (never by name), the port must be free to start
  rather than some holder being killed, and the worktree is removed. Each assertion prints an `ok` line; a failure
  names the scenario and assertion and the script exits 1.
- Two bugs the repeatable version exposed: git writes progress to stderr, which becomes a terminating error under
  `$ErrorActionPreference = 'Stop'` (one helper runs native commands and reports the exit code instead), and an
  interrupted `git worktree add` leaves a locked registration `git worktree remove` refuses — cleanup now unlocks,
  deletes the folder and prunes.
- Verified by running it three times: the first full run found the git/worktree bugs (both fixed); after that all
  three scenarios passed in one run, then again after the review pass below. `%AppData%\PoolScoreboard` comes back as
  it was, no worktree is left registered and no Controller is left running.
- Review pass, once it was working: the exe existence check now runs before `Resolve-Path`, so a missing build prints
  "run dotnet build first" instead of a raw resolver error; three copies of "stop the Controller and wait for the port"
  are gone, since the per-scenario teardown already does exactly that; the duplicated teardown in the outer `finally`
  is gone for the same reason; and the foreign-listener script is deleted in the outer `finally` rather than at the end
  of a scenario that could fail before reaching it. The leftover scenario also asserts the hidden window's handle is
  zero, since "windowless now" is the precondition the rest of it depends on.
- No version bump: developer tooling, nothing the shipped app changes.

### 2026-11-27 — End-cap icons are written the way the files that name them are

- The icons under `icons\` were the last in-place write: `File.WriteAllBytes` straight onto the final name, so a
  power cut mid-write left a truncated image that `settings.json` or `roster.json` still pointed at — a broken end
  cap the overlay would serve and nothing would detect.
- `DurableFile` now has `WriteAllBytes`, sharing one private write with `WriteAllText`; `OperatorSettingsStore`'s
  icon write and `PlayerRosterStore.SetIcon` both use it. Every saved file in the app now goes: write the temporary
  name, flush it to the disk, move it into place.
- Tests: `WriteAllBytes` puts exact binary bytes on the disk (including `0x00`/`0xFF`) with no `.tmp` left, replaces
  a 4 KB icon with a 3-byte one without leaving a tail, and the two stores' "leaves no temporary file behind" tests
  now cover the icon they save as well as the JSON.
- Version 0.25.0 → 0.26.0.

### 2026-11-27 — The profile files get the same flushed write as the match

- `settings.json` and `roster.json` had exactly the gap `match.json` just lost: `File.WriteAllText` to a temporary
  file, then a move, with nothing asking the OS to put the bytes on the disk. A killed process kept the previous
  file, but a power cut could publish a name whose contents were still in the cache — a half-written profile or
  player list, read back as `.corrupt.json` on the next launch.
- Rather than a third copy of the flush block, the write lives in `Persistence/DurableFile`: encode, write under
  `path + ".tmp"`, `Flush(flushToDisk: true)`, then move. `MatchStateStore`, `OperatorSettingsStore` and
  `PlayerRosterStore` all use it, so the three files cannot drift apart on this again. The rename's metadata is
  still left to NTFS; the guarantee is about the contents.
- Tests (3 new, `DurableFileTests`): the text lands exactly as given (UTF-8, no BOM, length checked in bytes), no
  `.tmp` is left, replacing a 5 KB file with a short one leaves no tail, and a write into a missing folder throws
  an `IOException` so a store's `TrySave` reports a failed save instead of a silent success. The existing store
  round-trip tests now run through it. The match test's length check moved to `Encoding.UTF8.GetByteCount` so it is
  right for non-ASCII content too.
- Not covered: the icon files are still written in place with `File.WriteAllBytes` — not atomic at all, so an
  interrupted icon write can leave a truncated image the roster points at. Separate fix, noted in CLAUDE.md.
- Version 0.24.0 → 0.25.0.

### 2026-11-27 — The saved match survives a power cut, not just a killed process

- `MatchStateStore.TrySave` serialized straight through `File.WriteAllText` and then moved the file into place: a
  process kill was safe (the previous snapshot stayed whole, and the live check killed the app and resumed 2–1), but
  nothing ever asked the OS to put the bytes on the disk. After a power cut the rename could be recorded while the
  contents were still in the cache — `match.json` present, empty or half written, and the match gone.
- The snapshot is now written through a `FileStream` and `Flush(flushToDisk: true)` before the move publishes it, so
  the name cannot point at bytes that are not on the disk yet. The rename itself is metadata, which NTFS journals
  rather than anything this code can flush; the guarantee is about the snapshot's contents.
- Test (1 new): `TrySave_LeavesTheWholeSnapshotOnDisk_SoAFreshReadLoadsIt` reopens the published file through a
  handle of its own with no sharing, asserts its length is exactly the serialized payload's, parses it back with all
  15 balls and leaves no `.tmp`. The flush call is not observable from a test — what is pinned is the published file
  being complete — so power-loss tolerance remains an argument plus the live process-kill proof, not a measurement.
- Version 0.23.1 → 0.24.0 (a durability change in behaviour, the same bucket as the corrupt-file and leftover-port
  fixes).

### 2026-11-26 — Crash-safety has tests instead of a hand-run

- The match autosave — debounce timer, `OnExit` snapshot, delete when back at setup, resume, unreadable file — lived
  in `App`, so a live run was its only proof. It is now `Persistence/MatchAutosave`, taking a `GameManager`, an
  `IMatchSnapshotStore` (which `MatchStateStore` implements) and an optional delay: the real timer can be driven with
  a 40 ms delay, and a store that counts writes replaces watching a file's timestamp.
- `App` keeps only the wiring — it builds one after `SharedGameManager` exists, calls `Restore()` where
  `RestoreMatch()` was, and `SaveNow()`/`Dispose()` in `OnExit`.
- Watching starts at `Restore()`, not in the constructor. A first cut subscribed on construction, which is before
  `OnStartup` applies the operator profile; those `ApplyTo` calls fire `GameStateChanged` five times while the match
  is still at setup, so the timer they armed would run `SaveNow()` → `Delete()` — deleting `match.json` in the 500 ms
  window before `Restore()` read it, and losing the resume it was there to perform. Starting to watch at the resume
  removes the race and also stops a fresh launch scheduling a pointless delete. Dropped the `_disposed` flag with it:
  `Timer.Dispose` and `-=` are both idempotent, so it guarded nothing.
- Tests (8 new): a 20-point burst is one write carrying the latest score; a reset is one delete and no write; the
  exit snapshot writes immediately despite a 30 s delay; a change after `Dispose` writes nothing; a profile applied
  before `Restore()` leaves the saved match on disk and the resume then finds it; a saved 8-ball split match comes
  back with its scores, groups and balls; a fresh install stays at setup; a hand-broken `match.json` leaves the game
  at setup and is kept as `match.corrupt.json`.
- Mutation check: replacing the timer re-arm with an immediate `SaveNow()` fails four of them (burst, reset, exit and
  dispose), so they pin the debounce instead of restating it.
- Verified live against the real app: START MATCH plus two home points produced one 241-byte `match.json` with
  `"homeScore": 2`; `/api/control/match/reset` deleted it; `Stop-Process -Force` (no `OnExit`) then a relaunch served
  2–0 from the restored snapshot.
- Build 0 warnings; 200/200 tests (97 Core + 103 Controller). No version bump: no behaviour change.

### 2026-11-26 — The banner says what to do about the window it cannot close

- A Controller that still has a window is left running by design, but the banner only named it — the operator had to
  work out that closing that window is what frees the port. It now ends with "Close that Controller window, then
  choose Retry to take the port back." The sentence is built from the same `StaleController.Observation` the close
  attempt made, so the banner and the decision cannot disagree about who holds the port; it appears only for another
  copy of this app with a window — a windowless leftover has already been dealt with, and another program's window is
  not ours to instruct on.
- `DescribeFailure` is separated from the session (static, taking the port, the owner description and the
  observation) so the wording is asserted without a socket: a windowed Controller gets the advice, a windowless
  leftover and another program do not, and nothing listening still says the port could not be taken.
- The banner's text block now wraps instead of trimming with "…", because two sentences do not fit the one line the
  old message needed; `Observation` carries the PID so the message and the decision name the same process.
- Build 0 warnings; 193/193 tests (97 Core + 96 Controller). Version 0.23.1 (UI copy).

### 2026-11-26 — The leftover signal, checked rather than assumed

- Review of the window-aware leftover close. `Process.MainWindowHandle` is the right signal: it enumerates the
  process's visible top-level windows without sending it a message, so a minimized Controller (proved live:
  `iconic=True` with its handle intact) and a hung one both still count as windowed and are left alone, while a
  hidden window counts as none — which is the only case the port is taken back from. A packaged build would report
  its exe name the same way, since this app is unpackaged.
- Two things were wrong. An unreadable process — gone, or protected by a higher integrity level — used to fall
  through as "no window", so the app would try to close something it could not see and log that it was doing so;
  "cannot tell" is now its own answer, nothing is closed on it, and the banner goes on naming the holder. And the
  name came from one `Process` open and the window from another, so the holder exiting in between could have the
  second look describe a recycled PID; both now come from one `Observe(pid)` snapshot, and `TryClose` re-checks the
  name on its own handle before killing.
- The log only states what happened now: "closed it" is written after the close and after the exit was waited for, a
  holder that refused to close says so, and the old "closing it …" line that ran before the attempt is gone.
- Trimmed: `PortOwner.FindOwner`/`Owner` went away again — nothing needed the name and the PID from two places — so
  `Describe` is back to its single lookup and the session asks `PortOwner` only for the PID.
- Tests: `Observe_*` covers one snapshot, a real window, no window and an unreadable PID; `TryClose_*` covers the
  real kill and the recycled-PID guard (a mismatched name leaves the process running). Build 0 warnings; 190/190
  (97 Core + 93 Controller).
- Verified live against the pre-guard build on the port as PID 14544: minimized (handle 4 327 624, `iconic=True`) →
  Retry logged "still has a window; leaving it alone." and 14544 kept serving; hidden (handle 0) → Retry logged "was
  held by a leftover … closed it so the overlay can start.", closed it, and the new PID served 200 with the banner
  cleared. Version stays 0.23.0: this is a correction to the same unreleased feature.

### 2026-11-26 — The overlay banner is tested, and a leftover Controller no longer keeps the port

- `GameViewModel` now depends on `IOverlaySession` instead of the concrete `OverlayHostSession`, so the top bar's
  banner and Retry can be driven with no socket. `OverlayBannerTests` uses a fake session: the reason the session
  reports is exactly what the window shows (the view model does not re-word it), Retry goes back to the session and
  clears the banner when the start succeeds, and a retry that fails again keeps the session's new reason.
- The dispatcher hop is asserted, not just observed: a real `Dispatcher` runs on its own STA thread, `Stopped` is
  raised from the test thread, and the test checks the banner's `PropertyChanged` arrived on the dispatcher's thread
  and not the reporting one. Before this, only a hand-run outage proved that.
- A leftover Controller on `127.0.0.1:51234` used to make the banner permanent — its Retry could never win, because
  the holder has no window for the operator to close. `OverlayHostSession` now closes such a holder and retries the
  bind once (`Diagnostics/StaleController`, using `PortOwner.FindOwner`), but only when it runs this app's own
  executable **and shows no window**. A copy that still shows a window is somebody's session — an older build in
  use, or an instance that was never shut down — so it is left running and only named; closing it would end their
  window mid-match. Anything that is not this app, and this process itself, are never targets either.
- Verified live, both ways, against a pre-guard build (`1acddbd`, no single-instance mutex) holding the port as PID
  18476. With its window up, this build logged `… which still has a window; leaving it alone.`, kept 18476 running
  and showed `The overlay is off: 127.0.0.1:51234 is held by PoolScoreboard.Controller (PID 18476).` with its Retry
  button. Hiding that window and pressing Retry logged `… is held by a leftover PoolScoreboard.Controller (PID
  18476); closing it so the overlay can start.`, closed 18476, bound the port as the new PID, served
  `/overlay/api/scoreboard/state` with 200 and cleared the banner.
- Build 0 warnings; 188/188 tests (97 Core + 91 Controller).

### 2026-11-26 — The window's startup state is testable, and tested

- `GameViewModel` built its own `PlayerRosterStore` (reading `%AppData%`) and read `Application.Current` for its
  dispatcher and its dialogs' owner, so nothing about what the window shows could be asserted. There is now one
  internal wiring constructor taking the roster store, dispatcher and owner; `MainWindow`, its only producer, passes
  itself and its dispatcher. A null dispatcher means "no UI thread", so state handlers run inline, which is what a
  test wants. It is internal so no caller can leave the dispatcher out and end up writing bound state from a Kestrel
  control thread — the convenience public overload it replaced had no callers left. The view model no longer reads
  `Application.Current` at all (the flag picker's owner came from it too, which is why that method stopped being
  static).
- Tests: `GameViewModelTests` builds the view model with a roster store in a throwaway folder, no dispatcher and no
  window. A fresh launch must stay on 9-ball (a state at setup carries the 8-ball enum default, which is not a
  choice anyone made), and a resumed 8-ball match must come up on the live view with its restored scores, its
  `Solids`/`Stripes` groups, the 7+7 group layout, the single centre 8-ball and no numeric row.
- Re-introducing the unconditional "take the choices from state" line makes the first of those fail with
  `Expected: NineBall / Actual: EightBall`, so the test guards the regression rather than restating the code.
- Build 0 warnings; 171/171 tests (97 Core + 74 Controller).

### 2026-11-26 — A crash mid-match no longer costs the scores

- A match lived only in memory, so a crash or a forced restart mid-stream lost the scoreboard with it — the app
  already had crash hooks precisely because it can die. `App` now subscribes to `GameStateChanged` and re-arms a
  pool timer, so the burst of changes a point produces becomes one small `match.json` write 500 ms later (never
  on the UI thread: the event arrives inside `GameManager`'s lock, often from the Stream Deck's HTTP calls); a
  final snapshot is written in `OnExit`. A state back at setup deletes the file, so a reset match is not resumed.
- `Core.Persistence.ResumableMatch` captures what a resumed overlay would otherwise get wrong: scores, game type
  and Race-To mode, ball groups, pocketed balls and whose turn it is. The players' names, Race-To targets and
  artwork are not duplicated — the operator profile carries them and is restored first, which is what the scores
  are judged against.
- `GameManager.RestoreMatch` puts a saved match back atomically: scores floored at zero, ball numbers outside
  1-15 dropped, ball groups only on 8-ball, the winner recomputed from the scores instead of trusted from the
  file, and the undo history cleared — the points it would undo are no longer on screen, so the first undo after
  a restart does nothing rather than removing a point the resumed match never recorded.
- `GameViewModel` takes the game type and Race-To mode from state — but only when a match was actually resumed,
  so a resumed 8-ball match shows its groups and its ball layout — and fills the live view from the restored
  snapshot, because no event fires until the operator touches something. A state at setup holds the 8-ball enum
  default, which is not a choice anyone made, so a fresh launch keeps the screen's own 9-ball default. Restore
  is automatic, not prompted: RESET MATCH returns to setup and clears the file.
- `Persistence/MatchStateStore` writes through a temporary file and moves it into place, as the other stores do,
  and serializes its saves and deletes: the debounce timer and `OnExit` can both reach it when a point is scored
  just before the app closes, and two writers sharing one temporary file would lose one of the snapshots.
  An unreadable `match.json` is kept as `match.corrupt.json`; a file written by another version is left where it
  is and ignored, so a newer build can still read it.
- Tests: Core covers capture before a match starts, a full capture/restore round trip, winner recomputation with
  junk ball numbers and a negative score, and the cleared undo history; the Controller store covers round trip,
  delete (including when there is nothing to delete), the preserved corrupt file and the version mismatch.
- Build 0 warnings; 169/169 tests (97 Core + 72 Controller).

### 2026-11-26 — An overlay that dies mid-session shows the banner again

- The banner only reflected startup and manual retries: a host that came up and then stopped serving left the
  window advertising a dead overlay for the rest of the match. `OverlayHostSession` now probes the running
  host's `/overlay/api/scoreboard/state` every 3 s (2 s timeout) and, after a confirming retry 250 ms later
  fails too, reports `The overlay stopped serving on 127.0.0.1:51234.` in the same top-bar banner, with the
  same **Retry** that starts the host again.
- The probe rather than `IHostApplicationLifetime.ApplicationStopped`: the host is started with `Start()`, not
  `Run()`, so nothing stops it when its stopping token is cancelled — a listener fault would never produce that
  event. Probing the endpoint also catches a host that is still bound but no longer answering.
- A round can outlast the interval (two 2 s timeouts plus the pause), so a tick that arrives while a round is
  still running is skipped instead of stacking more requests on a host already suspected of being down.
- A stop the app asked for is not a failure: `Stop` disables the timer before the host goes down, and a probe
  already in flight is ignored because the session no longer holds that host by the time it reports.
- The dead host is stopped and disposed before the event is raised, so the port is already free when the
  operator's Retry arrives, and the stop leaves one `Trace` entry in `controller.log`.
- The view model subscribes to the session's new `Stopped` event and hops to the WPF dispatcher before
  refreshing the banner, the same way game-state events already do.
- Tests: a real Kestrel host is stopped out from under the session without calling `Stop` — the session
  reports the port, and Retry serves again on the same port; a host left serving through several probes raises
  nothing, and a plain `Stop` leaves no reason; the existing port-taken/retry test still covers startup.
- The Controller test assembly now runs its classes one at a time: several of them attach and detach
  process-wide `Trace.Listeners` while `TraceLogFileTests` enumerates that collection, which made it fail
  intermittently with "Collection was modified" — unrelated to this feature, but it had to stop flaking for the
  suite to mean anything.
- Build 0 warnings; 160/160 tests (93 Core + 67 Controller).

### 2026-11-26 — A dead overlay is visible in the window, named, and retryable

- A failed overlay start was only a log line: the window kept showing overlay URLs that answered nothing, and
  the only way back was to restart the whole app. `OverlayHostSession` now owns the host and the reason it is
  not running, and the main window's top bar shows that reason (visible on both the setup and live screens)
  with a **Retry** button that starts the host again.
- The reason names the process on the port: `Diagnostics/PortOwner` reads the listener table through
  `GetExtendedTcpTable` (no managed API maps a port to a process) and formats "PoolScoreboard.Controller
  (PID 21940)", falling back to the bare pid, or to a generic message when nothing listens. The same name is
  added to the startup error in `controller.log`.
- `OverlayHostRunner.Start` takes an optional port (tests use a throwaway one) and the retry path was verified
  to free the port on stop. The Retry button is a no-op while the host is already running.
- Tests: `PortOwnerTests` binds a listener in-process and asserts the lookup returns this process;
  `OverlayHostSessionTests` holds a port with a `TcpListener`, asserts a failed start reports the port and
  owning pid, then releases it and asserts Retry starts a real Kestrel host and stop releases it.
- Build 0 warnings; 158/158 tests (93 Core + 65 Controller).

### 2026-11-26 — A second Controller launch brings the running window forward and exits

- Two copies of the app shared `%AppData%\PoolScoreboard` (settings and roster are last-writer-wins) and both
  wanted 127.0.0.1:51234, so the second could not host its overlay and could overwrite the first's saved setup.
  `SingleInstanceGuard` now holds a session-scoped named mutex; a launch that finds it taken signals a named
  auto-reset event, which the running instance listens on and turns into `Activate()` — restoring a minimized
  window, with a topmost flick to raise one that is behind another app — then logs "Another PoolScoreboard
  Controller is already running…" and shuts down.
- The duplicate exits through `OnExit` without restoring or saving the profile: saving there would replace the
  running instance's settings with defaults, so `OnExit` has an explicit early path that only closes the log.
  The guard is created right after the log attaches, so the exit reason is in `controller.log`.
- The activation callback runs on a pool thread, so it checks that the dispatcher has not begun shutting down
  and that the window is still loaded before posting: a signal arriving as the Controller closes is dropped
  instead of throwing into a closed window or a dead dispatcher.
- Election is `Local\`-scoped: another Windows session cannot raise this session's window, and this is a
  single-operator console. The kernel object disappears with its last handle, so a crashed Controller leaves no
  stale lock; if the running instance has not created its event yet, the duplicate retries for up to 500 ms and
  then exits anyway.
- Tests: a second guard is refused and wakes the first, a released guard lets the next launch become primary,
  and a duplicate whose activation event does not exist still exits instead of throwing. Window activation
  itself is WPF-only and was checked by hand.
- Build 0 warnings; 156/156 tests (93 Core + 63 Controller).

### 2026-11-26 — A rejected roster keeps its artwork too

- The recovery path from the previous entry preserved `roster.corrupt.json` but left its icons in `icons\`, where
  the next roster save pruned them as orphans — the preserved entries pointed at artwork that no longer existed.
  `PlayerRosterStore.Load` now moves every `icons\roster-*` file into `roster.corrupt.icons\` (same file names)
  right after preserving the JSON, and says so in the warning. Recovering a roster means renaming
  `roster.corrupt.json` back to `roster.json` and moving the files from `roster.corrupt.icons\` into `icons\`.
- Only runs when the JSON itself was moved aside, and each icon is moved independently: one that cannot be moved
  is left for the usual pruning instead of failing the load. The files are moved rather than copied, so startup
  does not duplicate up to 1.5 MB per saved player.
- Tests: the corrupt-roster test now also asserts the icon left `icons\` for the archive and is named in the log;
  two more cover the save that used to prune the artwork and a locked icon that must not break the fallback.
- Build 0 warnings; 153/153 tests (93 Core + 60 Controller).

### 2026-11-26 — A rejected settings or roster file is kept for recovery

- `OperatorSettingsStore.Load` and `PlayerRosterStore.Load` used to leave an unreadable `settings.json` or
  `roster.json` in place after the warning, where the very next save replaced it with defaults — so the setup
  the operator had was gone exactly when they most needed it back. Both now move the rejected file to
  `settings.corrupt.json` / `roster.corrupt.json` (replacing an older rejected copy) before falling back, and
  the warning names the file that was kept. A file that cannot be moved — still held by whatever made the read
  fail — stays where it is and the warning is unchanged.
- Tests: the corrupt-file read tests now assert the original path is gone, the `.corrupt.json` copy holds the
  original bytes and the log names it; two more cover a second rejection replacing the earlier copy and a file
  locked with no sharing, where the preserve step must fail quietly and the load still fall back.
- Known gap (closed by the next entry): roster icon files were not moved with the JSON, so the next roster save
  pruned artwork no saved player referenced.
- Build 0 warnings; 151/151 tests (93 Core + 58 Controller).

### 2026-11-26 — Crashes and unreadable settings leave evidence in controller.log

- Unhandled exceptions now reach the log: `App.OnStartup` subscribes to `DispatcherUnhandledException`,
  `AppDomain.UnhandledException` and `TaskScheduler.UnobservedTaskException`, each recording through the new
  `Diagnostics/UnhandledExceptionLog` (source, exception type and message, stack trace, inner exceptions) as a
  `TraceError`. None of the handlers marks the exception handled, so the app still fails exactly as it did —
  the difference is that the log now ends with *why* instead of stopping at the last warning. A dispatcher
  failure reaches the CLR as well, so the AppDomain handler skips the exception the UI handler already
  recorded: one crash writes one entry, while a different thread's failure still gets its own.
- `OperatorSettingsStore.Load` and `PlayerRosterStore.Load` used to return defaults/empty on a `JsonException`,
  `IOException` or `UnauthorizedAccessException` without a word, so a hand-edited or truncated `settings.json`
  looked like a fresh install and the next save replaced it. Both now log "Could not read the operator
  settings/player roster: …" and still fall back, so a bad file cannot block startup.
- Settings writes get the treatment the roster already had: `OperatorSettingsStore.TrySave` logs
  "Could not write the operator settings: …" and returns false, and `App.SaveOperatorProfile` calls it instead
  of swallowing the exception. A save that silently did not happen now says so.
- Tests: three for the crash entry (type/message/stack, inner exceptions, and the UI-then-AppDomain duplicate
  being suppressed) against a real `TraceLogFile`, the read tests for both stores now also assert the warning
  line, and one for the settings save (a failure reports and logs instead of throwing).
- Build 0 warnings; 149/149 tests (93 Core + 56 Controller).

### 2026-11-26 — Overlay host errors reach the log, and a taken port no longer kills startup

- New `PoolScoreboard.Overlay/Diagnostics/TraceLoggerProvider` forwards the host's `ILogger` records at Warning
  and above to `Trace` — errors as `TraceError`, with the exception type and message appended — registered
  in `OverlayHost.Start` next to the existing minimum level. Until now Kestrel's failures and endpoint faults
  went only to the Console and Debug providers, and a WPF process has no console, so a control-API 500 or a
  failed bind left no evidence anywhere.
- `OverlayHostRunner.Start` returns `IHost?` and catches a bind failure (`IOException`/`SocketException`,
  including one wrapped in an `AggregateException`), tracing "Overlay host could not start on
  127.0.0.1:51234: …" and letting the app open without the overlay. Previously that exception escaped
  `App.OnStartup`, so a leftover Controller holding the port meant no window, no log line and no explanation.
- Deliberately narrow: only IO/socket failures are absorbed; any other exception still surfaces rather than
  being disguised as "couldn't start".
- Tests: three for the provider (warning keeps its category, error carries the exception and reads as Error,
  Information/Debug are dropped) and one for the start path that holds the port with a `TcpListener`, asserts
  `Start` returns null and that the log names the port and the reason.
- Verified live in the real app: one instance bound 51234 and served state 200, then a second was launched — it
  opened its window and logged both `Microsoft.Extensions.Hosting.Internal.Host: Hosting failed to start
  [IOException: Failed to bind to address http://127.0.0.1:51234 …]` (the bridge working) and `Overlay host
  could not start on 127.0.0.1:51234: …` (the new guard), while the first kept serving 200.
- Build 0 warnings; 145/145 tests (93 Core + 52 Controller).
- Known gap: a failed start is only logged — the Controller still shows the overlay URLs in Help as if they
  worked, and there is no retry short of restarting with the port free.

### 2026-11-26 — controller.log can be read while the Controller runs

- `TraceLogFile` used to hold one writer open for the whole session, so a reader that asks for the ordinary
  `FileShare.Read` — `File.ReadAllText`, and plenty of editors — got a sharing violation until the app
  exited. Each line is now opened, appended and closed with `FileShare.ReadWrite | FileShare.Delete`, so no
  handle outlives a single write: the log reads normally mid-session, and a crash cannot leave a locked file.
- Cold cost is one open/close per line, which is irrelevant at the volume this log sees (warnings only), and
  the lock around writes stays so rotation or a second instance can never interleave a half line.
- The header line lost its duplicated timestamp (`--- Controller started ---`), since every line is stamped.
- Fixed while verifying: Trace raises a warning as a `Write` prefix ("… Warning: 0 :") and then the message
  through `WriteLine`, so every warning had been producing a stray half-line above it. The prefix is now
  buffered and emitted with its message — the live log reads
  `2026-11-26 15:31:58 PoolScoreboard.Controller Warning: 0 : VERIFY shutdown probe` on one line.
- Tests: a new one reads the log with plain `File.ReadAllText` while the listener is still attached; the
  end-to-end shutdown-warning test dropped its `FileShare.ReadWrite` workaround and now reads the same way.
- The buffered warning prefix is bounded (512 characters): a `Write` with no `WriteLine` behind it is written
  out on its own instead of accumulating for the life of the process or gluing itself to an unrelated later
  line. Tested both ways — the fragment reaches the file, and the next warning does not carry it.
- Verified in the running Controller: the log reads with a plain reader mid-session, a warning written during
  `OnExit` (a temporary probe, then removed) was on disk after the process exited, and the file deleted
  cleanly afterwards.
- Build 0 warnings; 141/141 tests (93 Core + 48 Controller).

### 2026-11-26 — End-to-end coverage for the shutdown warning reaching the log

- One test joins the two halves that were only tested apart: a hung host (its stop never completes) goes
  through `OverlayHostRunner.StopAndDispose` with a real `TraceLogFile` attached, then the log file is read
  back *while the listener is still attached* — in a real shutdown the process exits right after, so a line
  buffered only in memory would be lost. The same test asserts the call still returns `false` inside its
  bound (under two seconds for a 100 ms timeout plus the settle margin), which is what stops a hung host from
  turning the log listener into a hang.
- Build 0 warnings; 139/139 tests (93 Core + 46 Controller).

### 2026-11-26 — Cap controller.log and rotate the previous run

- `TraceLogFile.Attach` now checks the log's size before opening it: over 1 MiB, the file moves to
  `controller.old.log` (replacing whatever previous run was there) and a fresh log starts. Keeping exactly
  one previous run is the point — the cap exists so a long-lived install cannot accumulate an unbounded
  file, not to bound any realistic session.
- Rotation failure is not fatal: if the move is blocked, the session appends to the large log instead.
  Losing the log or refusing to start would both be worse than an oversized file.
- Three tests (`TraceLogFileTests`) use a small cap so the fixtures stay tiny: an oversized log moves to
  `controller.old.log` and the new session starts clean, a second rotation discards the older previous run,
  and a log under the cap is appended to with no previous-run file created.
- Verified in the running Controller: a 1.1 MiB `controller.log` became `controller.old.log` on launch with a
  fresh 55-byte log beside it, and the next launch appended without rotating again.
- Build 0 warnings; 138/138 tests (93 Core + 45 Controller).

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
- Known gap at the time: no rotation or size cap — capped by the rotation entry above.

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
