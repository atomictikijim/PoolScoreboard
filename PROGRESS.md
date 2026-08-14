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

**Phase 0 is complete (2026-08-13).** The league system is gone and the domain model matches
CLAUDE.md:

- `PoolScoreboard.Core`: `GameManager`, `GameState`, `Player`, `GameType`, `RaceToMode`,
  `BallGroup`, `ColorTheme` — no league/skill-level/Fargo-rating anywhere. `GameManager` wins
  are decided by comparing each player's score to their own `RaceToTarget`; it also exposes
  `AssignBallGroup`, `SetCurrentShooter`, and `PocketBall`/`UnpocketBall`/`ResetBalls`.
- `PoolScoreboard.Controller`: `GameViewModel`/`PlayerViewModel` rebuilt around game type,
  Race-To mode (single/split, with single-mode syncing Home's target to Away), 8-ball
  ball-group assignment buttons, and a current-shooter toggle. `MainWindow.xaml`/`.xaml.cs`
  updated to match.
- `PoolScoreboard.Overlay`: `PoolScoreboard.Overlay.csproj` fixed — `Microsoft.NET.Sdk.Web`
  with `OutputType=Library` (no `Main`; it's hosted in-process by the Controller, not run as
  its own exe) and a `ProjectReference` to Core. No overlay pages or control API exist yet —
  that's Phase 3.
- `dotnet build` is clean across all three projects (0 warnings, 0 errors).
- No `streamdeck/` profile documentation yet.

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

### Phase 2: Scoreboard UI (Controller)

- [ ] Match setup screen: game type, Race-To mode + value(s), color theme picker
  (background/accent/text).
- [ ] Live view: score +/- per player, current-shooter toggle/indicator, ball display
  (8-ball group-assignment buttons + fixed center 8-ball; numeric 1-9/1-10 row for 9/10-ball),
  new-rack and reset-match controls.
- [ ] Pocketed-ball tracking: clicking a ball toggles it pocketed/live; pocketed balls render
  **greyed out** in place (not removed from layout) in both the 8-ball and 9/10-ball displays.
  Independent of 8-ball group assignment (group = which player it's shown under; pocketed =
  is it still on the table).
- [ ] Keyboard shortcuts for score +/-, shooter toggle, new rack.

### Pending: WNT visual reference

- [ ] Get screenshots from the developer of WNT (World Nine Ball Tour / Matchroom Pool)
  broadcast footage — Claude Code cannot watch YouTube video directly (see NOTES.md, "WNT
  visual reference — capability limits"), so this needs actual images to work from.
- [ ] Once screenshots are available, extract concrete style cues (color palette, ball-tracker
  treatment, typography/layout patterns) and fold them into the Phase 2/3 UI and overlay work
  alongside the overlays.uno reference already in README.md.

### Phase 3: Overlay (OBS Integration)

- [ ] Host the Overlay's Kestrel server in-process from the Controller on startup, bound to
  `localhost` only, sharing the same `GameManager` instance as the UI.
- [ ] `/overlay/scoreboard` page: scores, Race-To, shooter indicator, ball display, operator
  colors — no external CSS/JS/font references (must render offline).
- [ ] Live updates via polling or SSE/WebSocket (avoid a hard requirement on JS libraries
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
- Ball-pocketed tracking (marking individual balls down mid-rack, beyond group assignment)
  is unscoped — confirm with developer before building it.
- No configuration UI for anything beyond what's listed in Phase 2 yet.

## Change Log

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
