# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

**PoolScoreboard** is a Windows desktop application for managing pool game scoreboards with OBS streaming integration and Stream Deck support. It's a three-project solution targeting .NET 8.0 with nullable reference types enabled.

## Quick Start Commands

### Build the solution
```powershell
dotnet build
```

### Run the WPF Controller application
```powershell
dotnet run --project PoolScoreboard.Controller/PoolScoreboard.Controller.csproj
```

### Build in Release mode
```powershell
dotnet build -c Release
```

### Clean build artifacts
```powershell
dotnet clean
```

## Architecture

The solution is organized into three projects:

### PoolScoreboard.Core (Game Logic Layer)
- **Purpose**: League-agnostic game logic, rules, and data models
- **Key Files**:
  - `Enums/League.cs` — League types: APA, USAPL, BCA, TAP
  - `Enums/GameType.cs` — Game types: EightBall, NineBall, TenBall
  - `Models/Player.cs` — Player with name, team, league affiliation, and skill level
  - `Models/GameState.cs` — Current game state (players, scores, break holder, winner)
  - `GameManager.cs` — Main orchestrator; manages game initialization, scoring, breaks, and win detection
  - `Rules/RaceRules.cs` — League- and skill-level-specific race-to calculations (handles all 4 leagues × 3 game types)

**Design Notes**:
- `GameManager` raises `GameStateChanged` events when state updates occur
- `RaceRules` uses tuple pattern matching to map (league, game type, skill level) to race-to values
- All core logic is UI-agnostic and testable

### PoolScoreboard.Controller (WPF Desktop Application)
- **Purpose**: Windows desktop UI for scoreboard control
- **Target**: `net8.0-windows` with WPF enabled
- **Dependencies**: CommunityToolkit.Mvvm 8.4.2 for MVVM pattern support
- **Current State**: Scaffolding only (App.xaml and MainWindow.xaml are templates)

**Design Approach**:
- Follow MVVM pattern for all UI logic
- ViewModels should subscribe to `GameManager.GameStateChanged` events
- Keyboard input (numeric keys, arrow keys) and Stream Deck integration planned
- Use data binding for real-time score display

### PoolScoreboard.Overlay (HTTP Server for OBS)
- **Purpose**: ASP.NET Core server providing browser-based overlay for live streaming
- **Target**: `net8.0` console application with ASP.NET Core framework reference
- **Current State**: Project structure only, no implementation

**Design Approach**:
- HTTP endpoint(s) to serve overlay UI (HTML/CSS/JS)
- Potential WebSocket support for real-time state sync with Controller
- Stateless or minimal state — Controller is the source of truth

## Key Workflows

### Game Initialization
1. `GameManager.InitializeGame(player1, player2, gameType)` creates a new game
2. `RaceRules` is instantiated for the selected league and game type
3. `GameStateChanged` event is raised; UI subscribes and updates display

### Scoring
1. `GameManager.AddPoint(isPlayer1)` increments the appropriate player's score
2. `RaceRules.IsPlayerWinner()` checks if the player has reached the race-to value
3. If winner detected, `GameManager.EndGame()` is called automatically
4. `GameStateChanged` event notifies subscribers of updated state

### Undo/Reset
- `GameManager.UndoPoint()` reverts last score, preserving history intent
- `GameManager.ResetGame()` clears state for a new game

## Dependencies & Tools

- **.NET 8.0**: All projects target net8.0 or net8.0-windows
- **Implicit Usings**: Enabled in all projects (global using statements)
- **Nullable Reference Types**: Enabled in all projects
- **MVVM Toolkit**: CommunityToolkit.Mvvm 8.4.2 in Controller
- **WPF**: UseWPF=true in Controller project

## Current Implementation Status

### Complete
- Core game logic (GameManager, RaceRules, data models)
- All four league rulesets with skill-level-based race-to logic
- Event-based state notification system

### In Progress / Planned
- Controller UI (MVVM views and viewmodels)
- Overlay HTTP server and HTML/CSS/JS for OBS
- Keyboard input handling
- Stream Deck integration

## Common Tasks

### Adding a new league ruleset
1. Add league enum value to `League.cs` if needed
2. Extend `RaceRules.GetRaceToValue()` pattern match with new (league, gameType, skillLevel) rules

### Connecting UI to GameManager
1. Inject `GameManager` into a ViewModel
2. Subscribe to `GameManager.GameStateChanged` event
3. Update ViewModel properties on event; binding automatically refreshes UI

### Testing game logic
- No test project yet; all Core logic is dependency-free and easily unit-testable
- Future: add `PoolScoreboard.Core.Tests` project using xUnit or NUnit

## Patterns & Conventions

- **Namespaces**: Follow folder structure (e.g., `PoolScoreboard.Core.Rules`, `PoolScoreboard.Core.Models`)
- **File-scoped namespaces**: Use `namespace X;` syntax throughout
- **Events**: Use `EventHandler<TEventArgs>` with custom `EventArgs` subclass for state changes
- **Immutability**: Models are mutable for now; if needed, transition to immutable record types
