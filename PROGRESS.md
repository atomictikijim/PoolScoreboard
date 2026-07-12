# Progress

Tracks what's changed and what's next during development. Newest entries at
the top of each section.

## Current Status

**v0.0 (Project Setup Complete):** The PoolScoreboard project is initialized with a three-project solution structure (Core, Controller, Overlay) and foundational game logic. The Core project contains complete, working implementations of game state management (`GameManager`), all four league rulesets (APA, USAPL, BCA, TAP) with skill-level-based race-to calculations (`RaceRules`), and essential data models (Player, GameState). The initial compilation issue in `RaceRules`' pattern matching (invalid `and` syntax on tuple relational patterns) was identified and fixed during setup — the solution now builds clean with `dotnet build`. A `.gitignore` for .NET projects and comprehensive project documentation (CLAUDE.md, NOTES.md, PROGRESS.md, README.md) are in place. The Controller (WPF) and Overlay (ASP.NET Core) projects are structural scaffolding only, with no UI or HTTP implementation yet.

## Next Steps

### Phase 1: Core Logic Completion & Testing (CURRENT)
- [x] **v0.0 complete:** Project scaffolding and game logic (GameManager, RaceRules)
- [ ] Create `PoolScoreboard.Core.Tests` (xUnit or NUnit) with comprehensive tests for:
  - [ ] `GameManager` initialization, scoring, break changes, undo/reset
  - [ ] `RaceRules` for all leagues and game types (verify race-to calculations)
  - [ ] Win detection across all formats and skill levels
- [ ] **Estimated:** ~30-40 unit tests to cover all core logic paths

### Phase 2a: Basic Scoreboard UI (MVP)

**Design Reference:** Two-column layout with HOME/AWAY panels (dark navy theme, cyan accents for active state)

- [ ] Implement MainWindow XAML: Grid with 2 equal columns (HOME | AWAY)
- [ ] Create player panels with:

  - [ ] Team Name (TextBox, editable)
  - [ ] Player Name (TextBox, editable)
  - [ ] Skill Level (ComboBox, 1-9, triggers Race To recalculation)
  - [ ] Race To (TextBlock, read-only, auto-calculated from RaceRules)
  - [ ] Status button ("At Table" / "Set Shooting", cyan when active, gray when inactive)
  - [ ] Game counter with +/- buttons (increments game score)
  - [ ] Match counter with +/- buttons (increments match score)

- [ ] Create ViewModels (`GameViewModel`, `PlayerViewModel` for HOME/AWAY)
- [ ] Wire UI to GameManager:

  - [ ] Skill Level change → recalculate Race To via RaceRules.GetRaceToValue()
  - [ ] +/- buttons → GameManager.AddPoint() and GameManager.UndoPoint()
  - [ ] Status button → GameManager.SetBreak()
  - [ ] Subscribe to GameStateChanged events for live UI updates

- [ ] Styling: Dark navy background (#1a2332), cyan accents (#00d4ff), rounded corners, subtle borders
- [ ] Add keyboard shortcuts (1-9 for player skill, +/- for scores, Space to toggle break)
- [ ] **Estimated:** ~1 week for basic playable scoreboard (MVP)

### Phase 2b: Ball Assignment & Tracking

**Features:** Game-specific ball display and tracking

- [ ] Add GameState property tracking: `BallsPocketed` (list of ball numbers per player)
- [ ] Implement ball display UI component (colored circles 1-15 + 8-ball center)
- [ ] **8-Ball Mode:**

  - [ ] Display SOLIDS/STRIPES assignment buttons above ball display
  - [ ] Show stripes (9-15) under HOME player, solids (1-7) under AWAY player (configurable)
  - [ ] Clicking a ball toggles its "pocketed" state (grayed out / off-table)
  - [ ] Reset button to clear ball state and start new rack

- [ ] **9-Ball & 10-Ball Mode:**

  - [ ] Show all balls (1-9 or 1-10) in order
  - [ ] Clicking a ball marks it as pocketed by current player (at table)
  - [ ] Visual indicator (grayed out) for pocketed balls
  - [ ] Reset button for new rack

- [ ] Wire to GameManager:

  - [ ] Expose `GameState.BallsPocketed` property
  - [ ] Add methods: `GameManager.PocketBall(ballNumber)`, `GameManager.ResetBalls()`
  - [ ] Update UI on GameStateChanged events

- [ ] **Estimated:** ~4-5 days for ball tracking and visual feedback

### Phase 2c: Shot Clock & Match Controls

**Features:** Tournament-style shot timer and match management

- [ ] Implement Shot Clock component:

  - [ ] Large countdown display (seconds)
  - [ ] Start/Reset buttons (green Start, dark Reset)
  - [ ] Preset timer buttons (30s, 45s, 60s)
  - [ ] Show/Hide toggle to display/hide clock during play
  - [ ] Timer countdown with visual feedback (color change near timeout)

- [ ] Implement Match Control buttons:

  - [ ] "New Rack" button (resets BallsPocketed, increments game counter)
  - [ ] "Reset Entire Match" button (red warning style, resets all scores and match state)

- [ ] Wire timer to GameManager (optional: track shot time in GameState for analytics)
- [ ] **Estimated:** ~3-4 days for timer UI and match controls

### Phase 4: Overlay (OBS Integration)

- [ ] Design HTTP endpoint(s) for game state (e.g. `/api/game/current`)
- [ ] Implement minimal ASP.NET Core controller returning JSON
- [ ] Build HTML/CSS/JS browser overlay (read-only display of scores, players, break, ball state)
- [ ] Consider WebSocket for real-time updates (vs. polling)
- [ ] **Estimated:** ~1 week for basic streaming-overlay functionality

### Phase 5: Player Management & Persistence

- [ ] Add Entity Framework Core for local SQLite database
- [ ] Create `Player` entity with name, league affiliation, skill levels (Fargo, APA, TAP, etc.)
- [ ] Implement player loading/saving UI on Controller
- [ ] Extend tournament features (multiple games, statistics)

### Phase 6: Stream Deck Integration

- [ ] Research Stream Deck HTTP API
- [ ] Wire score increment/decrement buttons
- [ ] Add game control buttons (start, reset, etc.)

## Current Implementation Status

### Complete
- Core game logic (GameManager, RaceRules, data models)
- All four league rulesets with skill-level-based race-to logic
- Event-based state notification system
- Solution builds cleanly with zero warnings

### In Progress / Planned

- **Phase 1:** Core unit tests (30-40 tests for full coverage)
- **Phase 2a:** Basic Scoreboard UI (MVVM views, player setup, score management)
- **Phase 2b:** Ball Assignment & Tracking (ball display, 8-ball solids/stripes, 9/10-ball pocketing)
- **Phase 2c:** Shot Clock & Match Controls (timer, new rack, match reset)
- **Phase 4:** Overlay HTTP server and HTML/CSS/JS for OBS
- **Phase 5:** Player database with Entity Framework Core
- **Phase 6:** Stream Deck integration

## Known Gaps & Simplifications

- **No persistence:** All game state is in-memory; match history and player ratings are not persisted.
- **No UI yet:** Controller and Overlay are structural scaffolding with empty XAML templates.
- **No player database:** Eventually will need a local SQLite database for player names and ratings across sessions.
- **No configuration UI:** League selection, skill level assignment, and player team assignment are all hardcoded for now.

## Change Log

### v0.0 — 2026-07-12

- **Project setup complete:** PoolScoreboard.sln with three projects (Core, Controller, Overlay).
- **Core game logic:** `GameManager` (game orchestrator, state mutations, event notification), `RaceRules` (league/skill-level-specific race-to calculations for APA, USAPL, BCA, TAP across 8-ball, 9-ball, 10-ball), `Player` and `GameState` models.
- **Enums:** `League.cs` (APA, USAPL, BCA, TAP), `GameType.cs` (EightBall, NineBall, TenBall).
- **Fixed:** Initial `RaceRules` pattern-matching syntax error (invalid `and` combinator on tuple relational patterns); now builds clean.
- **Documentation:** CLAUDE.md (architecture and best practices), NOTES.md (issues log), PROGRESS.md (this file), README.md (user-facing overview).
- **Build:** `dotnet build` succeeds with 0 errors, 0 warnings. Solution ready for UI/Overlay implementation.
