# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

**PoolScoreboard** is a Windows desktop application for managing pool game scoreboards with OBS streaming integration and Stream Deck support. It's a three-project .NET 8.0 solution: a game-logic Core, a WPF Controller UI, and an ASP.NET overlay server for live streaming.

## Project Layout

- `PoolScoreboard.sln` — solution file
- `PoolScoreboard.Core/` — game logic, rules, data models (class library)
- `PoolScoreboard.Controller/` — WPF desktop application for scoreboard control
- `PoolScoreboard.Overlay/` — ASP.NET Core HTTP server for OBS browser source
- `README.md` — project overview and quick-start guide
- `CLAUDE.md` — this file
- `NOTES.md` — running log of issues discovered during development
- `PROGRESS.md` — tracks current development status and next steps

## Build & Run Commands

```powershell
# Build the solution
dotnet build

# Run the WPF Controller (requires Windows)
dotnet run --project PoolScoreboard.Controller/PoolScoreboard.Controller.csproj

# Run tests (currently none, but structure exists)
dotnet test

# Clean build artifacts
dotnet clean
```

## C# / Windows Best Practices

- **Target an explicit TFM** in every `.csproj` (`net8.0` for Core, `net8.0-windows` for Controller with WPF, `net8.0` for Overlay with ASP.NET Core).
- **Enable nullable reference types** (`<Nullable>enable</Nullable>`) on all projects — in place here.
- **Implicit usings enabled** (`<ImplicitUsings>enable</ImplicitUsings>`) — all using statements are global.
- **File-scoped namespaces** — use `namespace X;` syntax throughout.
- **Separate UI from logic:** Core contains all game rules and state management (testable, UI-agnostic); Controller binds UI to Core via event subscriptions and data binding; Overlay reads state through an HTTP endpoint.
- **Event-based communication:** `GameManager` raises `GameStateChanged` events whenever state mutates; UI/Overlay subscribe to these events rather than polling.
- **Use tuple pattern matching** for rules logic (e.g. `RaceRules.GetRaceToValue` maps `(League, GameType, SkillLevel)` tuples to race-to values).

## Architecture & Key Files

### PoolScoreboard.Core (Game Logic)

**Entry point:** `GameManager` — the main orchestrator for all game state and operations.

**Enums:**

- `League.cs` — APA, USAPL, BCA, TAP
- `GameType.cs` — EightBall, NineBall, TenBall

**Models:**

- `Player.cs` — name, team, league affiliation, skill level
- `GameState.cs` — current game (both players, scores, break holder, winner status, timestamps)

**Rules:**

- `RaceRules.cs` — league- and skill-level-specific race-to calculations for all 4 leagues × 3 game types

**Design Patterns:**

- `GameManager` uses an event-notification pattern: `public event EventHandler<GameStateChangedEventArgs>? GameStateChanged;`
- All state mutations go through GameManager methods (`InitializeGame`, `AddPoint`, `SetBreak`, `UndoPoint`, `ResetGame`, `EndGame`)
- `GameStateChanged` is raised after every state mutation, allowing UI to stay in sync without polling
- No database or async I/O in Core — it's pure, fast, and testable

### PoolScoreboard.Controller (WPF Desktop Application)

**Status:** Scaffolding complete (App.xaml, MainWindow.xaml are templates), UI implementation not yet started.

**Dependencies:** CommunityToolkit.Mvvm 8.4.2 for MVVM pattern support.

**Design approach (when implementing):**

- Follow MVVM pattern — ViewModels handle all game logic coordination, bind UI to state
- Create ViewModels that subscribe to `GameManager.GameStateChanged` events
- Use data binding for all real-time display (scores, current break holder, game status)
- Keyboard input (numeric keys for score, arrow keys for break) and Stream Deck integration TBD
- Future: full match history, player management (name/rating input), real-time scoreboard display for projection

### PoolScoreboard.Overlay (ASP.NET Core Server)

**Status:** Project structure only, no implementation yet.

**Purpose:** HTTP server serving a browser-based overlay for OBS streaming.

**Design approach (when implementing):**

- Stateless HTTP endpoint(s) serving JSON state (current game, scores, players, break holder)
- Potential WebSocket support for real-time updates (browser source polls or subscribes)
- HTML/CSS/JS overlay UI consuming that endpoint
- Controller is the source of truth; Overlay reads state only

## WPF Theming & UI Patterns (for Controller implementation)

When building the Controller UI:

- **Implicit Window style:** Use `DynamicResource` brushes (`TextPrimaryBrush`, `AccentPrimaryBrush`, etc.) for all surfaces, text, and borders so the UI follows light/dark theme changes live.
- **Custom windows need theme application:** Any modal dialog or separate window must call `themeService.ApplyTitleBar(this)` from its `SourceInitialized` event so the native title bar matches the theme.
- **DataGrid/ListBox styling:** Don't trust `Style` `Setter`s for `Background`/`Foreground` on standard controls — they often have default `ControlTemplate`s that ignore those properties. Prefer local attribute values or custom `ControlTemplate`s with explicit `TemplateBinding`.
- **TextBlock foreground gotcha:** Any `TextBlock` with its own inline `<TextBlock.Style>` loses the implicit theme foreground and needs an explicit `Foreground="{DynamicResource TextPrimaryBrush}"` local value, or it renders nearly invisible on light surfaces.

## Known Simplifications & Future Work

- **No database yet:** Game state is in-memory only; persistence (match history, player data) is not implemented.
- **Core logic is complete for basic gameplay:** league rules, race-to calculations, break tracking, undo/reset are all functional and tested.
- **UI is not started:** Controller and Overlay are structural only; no scoreboard display, player management, or OBS integration yet.
- **No player database:** Controller will eventually need to load/save player names, ratings, and team info; no persistence layer yet.

## Testing Notes

- Core logic (GameManager, RaceRules) is UI-free and easily unit-testable — create tests under a future `PoolScoreboard.Core.Tests` project using xUnit or NUnit.
- Controller UI would be tested end-to-end (WPF integration tests against a real GameManager).
- Overlay/HTTP integration tests would mock the game state.

## Versioning & Commit Policy

Versions follow the pattern `0.<major>.<ui>` pre-1.0:
- Major bumps for functionality changes (new feature, significant refactor)
- UI bumps for cosmetic-only changes (layout, styling, zero behavior change)
- Trivial fixes (typos, comments) fold into the next functional/UI commit

Example commits:
- `v0.1: Initial game logic and Core types` — first working version
- `v0.1.1: Controller main window layout` — UI-only styling
- `v0.2: WPF scoreboard display with live score updates` — new feature
