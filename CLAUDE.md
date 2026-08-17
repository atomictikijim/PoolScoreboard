# CLAUDE.md

This file provides guidance to Claude Code when working with code in this repository.

## Project Overview

**PoolScoreboard** is a Windows desktop application for running a pool-match scoreboard as
an OBS Studio browser-source overlay, controllable from a desktop window, the keyboard, or
an Elgato Stream Deck. It must work fully offline — no calls to any external service, ever.
It is a three-project .NET 8.0 solution: a game-logic Core, a WPF Controller UI that also
hosts the overlay server in-process, and an ASP.NET Core Overlay module serving the OBS
browser sources and the local Stream Deck control API.

**This is a reset of an earlier attempt at this project.** The original build was
league-based (APA/USAPL/BCA/TAP rulesets with skill-level or Fargo-rating-driven auto
Race-To calculations). That direction is discarded — see NOTES.md, "2026-08-13 — Project
scope reset". Nothing here should reference leagues, skill levels, or Fargo ratings; if you
see that in the existing code, it is legacy and slated for removal (PROGRESS.md Phase 0).

## Project Layout

- `PoolScoreboard.sln` — solution file
- `PoolScoreboard.Core/` — game logic, state, and data models (class library, no UI, no I/O)
- `PoolScoreboard.Controller/` — WPF desktop application; operator's control window, and the
  process that hosts the Overlay's Kestrel server in-process
- `PoolScoreboard.Overlay/` — ASP.NET Core module: OBS browser-source pages + local HTTP
  control API for the Stream Deck (hosted inside the Controller process, not a separate exe)
- `streamdeck/` — Stream Deck profile documentation (button → local endpoint mapping)
- `README.md` / `CLAUDE.md` / `NOTES.md` / `PROGRESS.md` — project docs

## Build & Run Commands

```powershell
# Build the solution
dotnet build

# Run the Controller (starts the WPF UI and the in-process overlay/control server)
dotnet run --project PoolScoreboard.Controller/PoolScoreboard.Controller.csproj

# Run tests (once PoolScoreboard.Core.Tests exists)
dotnet test

# Clean build artifacts
dotnet clean
```

## C# / Windows Best Practices

- **Target an explicit TFM** in every `.csproj` (`net8.0` for Core, `net8.0-windows` for
  Controller with WPF, `net8.0` for Overlay).
- **Enable nullable reference types** (`<Nullable>enable</Nullable>`) on all projects.
- **Implicit usings enabled** (`<ImplicitUsings>enable</ImplicitUsings>`).
- **File-scoped namespaces** — use `namespace X;` syntax throughout.
- **Separate UI from logic:** Core contains all game rules and state (testable, UI-agnostic);
  Controller binds UI to Core via event subscriptions/data binding; Overlay reads/writes
  state through the same in-process `GameManager` instance — never a second copy of state.
- **Event-based communication:** `GameManager` raises `GameStateChanged` whenever state
  mutates; the Controller UI, the OBS overlay pages, and the Stream Deck API all subscribe
  to or read from this instead of polling each other.
- **Localhost only:** the Overlay's Kestrel server must bind to `localhost`/`127.0.0.1`
  only — never `0.0.0.0` — so the app stays usable and safe with no network connection and
  isn't reachable from outside the machine.
- **No CDN/external references** anywhere in the overlay HTML/CSS/JS (fonts, JS libraries,
  images) — everything must be bundled locally so the overlay renders with no internet
  connection.

## Domain Model (target design — see PROGRESS.md Phase 0/1 for current state)

- `GameType` — `EightBall`, `NineBall`, `TenBall`.
- `RaceToMode` — `Single` (one shared target) or `Split` (each player has their own target).
- `BallGroup` — `Unassigned`, `Solids`, `Stripes`. Only meaningful for 8-ball; the 8-ball
  itself is never assigned to a group and always renders center.
- `Player` — `Name`, `TeamName` (optional), `RaceToTarget`, `BallGroup` (8-ball only).
- `GameState` — both `Player`s, `GameType`, `RaceToMode`, scores, `CurrentShooter`,
  `ColorTheme`, `PocketedBalls` (ball numbers 1-15 currently off the table), active/winner
  status.
- `ColorTheme` — operator-set background/accent/text colors, applied to the overlay pages.
- No league, skill level, or Fargo rating anywhere in this model.

## Ball Display Detail

- Every ball display (8-ball's grouped layout and the numeric 9/10-ball row) supports
  marking individual balls **pocketed**: a pocketed ball renders **greyed out** in place
  (not removed/collapsed from the layout), so the operator and viewers can see at a glance
  what's left on the table.
- `GameManager.PocketBall(ballNumber)` / `UnpocketBall(ballNumber)` toggle membership in
  `GameState.PocketedBalls`; `ResetBalls()` clears it for a new rack.
- This applies independently of `BallGroup` assignment in 8-ball — a ball's group (which
  player it's displayed under) and its pocketed state are separate flags.

## Architecture & Key Files

### PoolScoreboard.Core (Game Logic)

- `GameManager` — orchestrator for all game state and operations: `InitializeGame`,
  `AddPoint`, `UndoPoint`, `SetCurrentShooter`, `AssignBallGroup`, `ResetGame`, `EndGame`.
  Raises `GameStateChanged` after every mutation.
- No database, no async I/O — pure, fast, unit-testable.

### PoolScoreboard.Controller (WPF Desktop Application)

- MVVM: ViewModels for match setup, player setup, and the live game view.
- Match setup: game type, Race-To mode (single/split) and value(s), color theme picker.
- Live view: score +/- per player, current-shooter toggle, ball-group assignment buttons
  (8-ball only) or ball display (9/10-ball), new-rack/reset-match controls.
- On startup, starts the Overlay's Kestrel host in-process, passing it the same
  `GameManager` instance the UI is bound to.
- Dark theme with operator-customizable accent color, consistent with the overlay's theme.

### PoolScoreboard.Overlay (ASP.NET Core, hosted in-process by Controller)

- `/overlay/scoreboard` — browser-source page: scores, Race-To, shooter indicator, ball
  display, operator colors. Read-only; polls or subscribes (SSE/WebSocket) for state.
- `/overlay/cueball` — separate browser-source page: a cue-ball graphic; clicking it places
  a red dot marking the spin/contact point. Independent of and separately positionable from
  the scoreboard overlay in the OBS scene.
- `/api/control/*` — local HTTP endpoints for Stream Deck buttons (score inc/dec, shooter
  toggle, ball assignment, new rack, reset match). Same `GameManager` instance as the UI.

## Stream Deck Integration

- No custom Elgato SDK plugin — Stream Deck buttons hit the local `/api/control/*` HTTP
  endpoints directly (built-in "Website"/HTTP-request-style action), which is simpler to
  build and maintain and needs no separate plugin install.
- `streamdeck/` documents the button layout and which endpoint each button calls, so the
  actual `.streamDeckProfile` can be built/exported from the Stream Deck app using that
  mapping.
- Because the control API is local-only, Stream Deck control works with no internet
  connection, same as the rest of the app.

## Visual Reference

The developer wants the scoreboard's styling to draw on the World Nine Ball Tour (WNT /
Matchroom Pool) broadcast scoreboard as an additional visual reference, alongside the
overlays.uno Billiards Scoreboard already noted in README.md. Two WNT screenshots have been
reviewed as of 2026-08-14; see NOTES.md ("WNT visual reference — style cues extracted" and
"WNT visual reference: second screenshot adds shooter-indicator and ball-tracker cues") for
detail. Concrete design tokens for the `/overlay/scoreboard` page in Phase 3:

- A single glossy "pill"-shaped bar, capsule-rounded ends, subtle top-lit gradient.
- Deep violet/indigo bar fill (`ColorTheme.HomeBackground`/`AwayBackground`), white bold
  sans-serif text (`ColorTheme.HomeText`/`AwayText`, editable independently per side).
- White rounded-rect score badges with dark, high-contrast numbers, sitting inside the bar
  next to each player's name.
- A slightly darker center segment holding "Race to N" in smaller white text, separating the
  two player halves without a hard divider line.
- End-cap badges are rectangular national flags (slightly rounded corners) on black rounded
  caps at each end — not circular, and not a generic team-sponsor mark.
- Current-shooter indicator: a small right-pointing triangle (▶) next to the center Race-To
  segment, on the side of whichever player is currently shooting — a pointer, not a glow/
  highlight across that player's whole half.
- Ball tracker: a separate white capsule-shaped bar, horizontally centered *below* the main
  score bar, holding small colored circular ball icons (standard ball coloring, white number
  centered) for balls still in play. Note: the WNT reference removes pocketed balls from this
  row entirely rather than greying them out — that does **not** override this project's own
  decision (see "Ball Display Detail" above) that pocketed balls render greyed out in place.
- `ColorTheme.Accent` maps naturally to the score-badge/center-segment treatment.

Open question, not yet resolved: the flag end-caps aren't backed by any field in the current
domain model (`Player` only has `Name`/`TeamName`, no nationality) — revisit with the developer
if/when this styling is actually adopted. This only informs the Overlay's own pages — the
Controller's WPF operator console keeps its existing dark theme (CLAUDE.md's "consistent with
the overlay's theme" is a soft aesthetic alignment, not a pixel match).

## Known Simplifications & Future Work

- No persistence yet: game state is in-memory only; no match history or saved player list.
- No player database: names/teams are typed in per match for now.

## Testing Notes

- Core logic (`GameManager`, models) is UI-free and unit-testable — put tests under
  `PoolScoreboard.Core.Tests` (xUnit or NUnit).
- Controller UI would be tested end-to-end against a real `GameManager`.
- Overlay/control-API endpoints should be tested against a `GameManager` test instance, not
  a live one.

## Versioning & Commit Policy

Version format while pre-1.0: **`0.<major>.<ui>`**

- **Major functionality updates** (new features, new workflows, significant logic changes)
  bump the middle number: `0.1` → `0.2` → `0.3` ...
- **UI-only updates** (layout, styling, cosmetic/no-behavior-change tweaks) bump the third
  number under the current major: `0.1.1` → `0.1.2` ... Resets when the major bumps.
- Trivial fixes (typos, comments, formatting) fold into the next functional/UI commit.

Commits are **not** auto-pushed in this repo — confirm with the developer before pushing,
same as any other repo, unless they explicitly grant standing authorization for this project.
