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
- No persistence for `ScoreboardStyle`/element-visibility choices between app runs — they reset
  to defaults every time a new match starts, same as `ColorTheme` today.
- Flag end-caps from the WNT reference are still not implemented as real flags (no nationality
  field on `Player`) — `EndCapStyle` only offers `Dot`/`Hidden` for now.
- Keyboard shortcut keys (Q/P/A/Space/N) are a first pass, not confirmed with the developer.

## Change Log

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
