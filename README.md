# Pool Scoreboard Controller & OBS Overlay

A Windows desktop application for running a live pool-match scoreboard as an OBS Studio
overlay, controlled from a desktop window, the keyboard, or an Elgato Stream Deck — with
no internet connection required at any point.

Modeled loosely on the control experience of overlays.uno's Billiards Scoreboard
(https://app.overlays.uno/control/3qGQ7Nn39iB0pa7xemjnD0), but fully local: the app hosts
its own overlay server on localhost, so it keeps working with no network at all.

## Features

- **Game Types**: 8-ball, 9-ball, 10-ball — no league or skill-level system. The operator
  sets everything directly.
- **Race To**: a single shared target for both players, or a per-player split (e.g. Home
  races to 7, Away races to 5).
- **Ball Display**:
  - *8-ball*: operator assigns Solids or Stripes to each player; that player's group of
    balls displays underneath them. The 8-ball itself always stays in the center,
    unassigned to either side.
  - *9-ball / 10-ball*: balls display in numerical order (1–9 or 1–10), no assignment
    needed.
- **Current Shooter Indicator**: highlights whichever player is at the table right now.
- **Customizable Color Scheme**: background, accent, and text colors are operator-set, not
  hardcoded — so the overlay can match a stream's branding.
- **Cue Ball Spin Overlay**: a second, independently placeable OBS browser source showing
  a cue ball graphic. The operator clicks it to drop a red contact-point dot showing the
  spin/english being used on a shot. Lives in its own browser source separate from the
  scoreboard, so it can be sized and positioned anywhere in the scene.
- **Stream Deck Control**: a documented Stream Deck profile (button layout + local HTTP
  endpoints) drives scores, shooter toggle, ball assignment, and rack/match reset without
  touching the desktop window.

## Project Structure

- `PoolScoreboard.Controller/` — WPF desktop application; the operator's control window
  and the host process for the local overlay server.
- `PoolScoreboard.Core/` — game logic, state, and data models (no UI, no I/O).
- `PoolScoreboard.Overlay/` — the ASP.NET Core Kestrel server (hosted in-process by the
  Controller) serving the OBS browser-source pages and the local HTTP control API used by
  the Stream Deck.
- `streamdeck/` — Stream Deck profile documentation: the button-to-endpoint mapping used
  to build the actual profile in the Stream Deck app.

## Quick Start

```bash
# Build the solution
dotnet build

# Run the controller app (also starts the local overlay/control server)
dotnet run --project PoolScoreboard.Controller/PoolScoreboard.Controller.csproj
```

Add the overlay as an OBS **Browser Source** pointed at the local URL the Controller
prints on startup (e.g. `http://localhost:5000/overlay/scoreboard`), and the cue-ball
overlay as a second, separate Browser Source (e.g. `http://localhost:5000/overlay/cueball`).

## Architecture

### Core Module

- `GameManager` — orchestrates match state and raises `GameStateChanged` so the Controller
  UI and the Overlay server both stay in sync without polling each other.
- `GameState` / `Player` — data models: scores, race-to target(s), current shooter, 8-ball
  group assignment, color theme.
- No league, skill-level, or Fargo-rating concepts — race-to is always set directly by the
  operator.

### Controller Module (WPF)

- Operator's control window: match setup, score +/-, shooter toggle, ball assignment/marking,
  color pickers.
- Hosts the Overlay's Kestrel server in-process on startup, so there is exactly one process
  and one `GameManager` instance — the Controller, the OBS overlay, and the Stream Deck
  endpoints all read/write the same live state.

### Overlay Module (ASP.NET Core, hosted by Controller)

- `/overlay/scoreboard` — OBS browser source: scores, race-to, shooter indicator, ball
  display, operator-set colors.
- `/overlay/cueball` — OBS browser source: cue ball + click-to-place red spin dot.
- `/api/control/*` — local-only HTTP endpoints for Stream Deck buttons (increment/decrement
  score, toggle shooter, assign ball group, new rack, reset match).
- Bound to `localhost` only — never reaches out to the internet, and nothing outside the
  machine can reach it either.

## Development Notes

- All projects target `.NET 8.0`.
- Nullable reference types enabled.
- Follow MVVM in the WPF Controller.
- Core stays UI-free and fully unit-testable.
