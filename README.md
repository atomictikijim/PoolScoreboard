# Pool Scoreboard Controller & OBS Overlay

A Windows desktop application for managing pool game scoreboards with real-time OBS integration and Stream Deck support.

## Features

- **Multi-Game Support**: 8-ball, 9-ball, and 10-ball pool games
- **League Support**: APA, USAPL, BCA, and TAP league rulesets with automatic skill-level-based race calculations
- **Player Management**: Input player names, team names, and skill levels
- **Game Control**: Keyboard and Stream Deck input for real-time scoreboard management
- **OBS Integration**: Browser-based overlay for live streaming
- **Automatic Win Detection**: Race conditions auto-calculate and detect winners based on league rules

## Project Structure

- `PoolScoreboard.Controller/` — WPF desktop application for scoreboard control
- `PoolScoreboard.Core/` — Game logic, rules engine, and data models
- `PoolScoreboard.Overlay/` — HTTP server and overlay UI for OBS streaming

## Quick Start

```bash
# Build the solution
dotnet build

# Run the controller app
dotnet run --project PoolScoreboard.Controller/PoolScoreboard.Controller.csproj
```

## Configuration

### Supported Leagues

- **APA** (American Poolplayers Association)
- **USAPL** (US Amateur Pool League)
- **BCA** (Billiard Congress of America)
- **TAP** (Tough As Nails Pool League)

### Game Types

- **8-Ball**: Classic 8-ball pool format
- **9-Ball**: Fast-paced 9-ball format
- **10-Ball**: European-style 10-ball format

## Input Methods

- **Keyboard**: Numeric keys and arrow keys for score control
- **Stream Deck**: HTTP API integration for button controls
- **Mouse/Touch**: Direct UI interaction

## Architecture

### Core Module

The `PoolScoreboard.Core` module contains:
- `GameManager` — Manages overall game state and logic
- `RaceRules` — League-specific ruleset calculations
- `Models` — Data models for players and game state
- `Enums` — Game types, leagues, and status enumerations

### Controller Module

The `PoolScoreboard.Controller` WPF application provides:
- Game setup and player management
- Real-time scoreboard display
- Keyboard and input handling
- Integration with Core game logic

### Overlay Module

The `PoolScoreboard.Overlay` module provides:
- HTTP server for OBS browser source
- Real-time game state synchronization
- Customizable overlay UI
- WebSocket support for live updates

## Development Notes

- All projects target `.NET 8.0`
- Nullable reference types are enabled
- Follow MVVM pattern in WPF code
- Game logic should be testable and UI-agnostic
