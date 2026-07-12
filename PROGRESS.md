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

### Phase 2: WPF Controller UI

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
- [ ] **Estimated:** ~1-2 weeks for basic playable scoreboard

### Phase 3: Overlay (OBS Integration)
- [ ] Design HTTP endpoint(s) for game state (e.g. `/api/game/current`)
- [ ] Implement minimal ASP.NET Core controller returning JSON
- [ ] Build HTML/CSS/JS browser overlay (read-only display of scores, players, break)
- [ ] Consider WebSocket for real-time updates (vs. polling)
- [ ] **Estimated:** ~1 week for basic streaming-overlay functionality

### Phase 4: Player Management & Persistence
- [ ] Add Entity Framework Core for local SQLite database
- [ ] Create `Player` entity with name, league affiliation, skill levels (Fargo, APA, TAP, etc.)
- [ ] Implement player loading/saving UI on Controller
- [ ] Extend tournament features (multiple games, statistics)

### Phase 5: Stream Deck Integration
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
- Core unit tests (30-40 tests for full coverage)
- Controller UI (MVVM views and viewmodels)
- Overlay HTTP server and HTML/CSS/JS for OBS
- Keyboard input handling
- Stream Deck integration
- Player database with Entity Framework Core

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
