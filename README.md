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
  hardcoded — so the overlay can match a stream's branding. Home and Away text colors are
  set separately, and the corner-roundness, scale and gloss finish are adjustable too.
- **End-Cap Icons**: each player's end cap can show a team logo or a flag — bundled national
  and state flags, or any image file the operator picks — instead of the plain accent-colored
  dot. The overlay fetches the icon separately from the score data, so swapping artwork does
  not re-send the whole scoreboard to OBS.
- **Saved Players**: everyone you play is remembered — name, team, Race-To and end-cap icon — so a
  returning player can be dropped into either side from a dropdown instead of being retyped. Players
  join the roster automatically when a match starts (or when the window closes), are listed most
  recently used first, and each side's picker has a **Delete** button to drop a mis-typed name (its
  saved icon goes with it).
- **Overlay Show/Hide**: the score bar, ball tracker and winner banner can each be turned off
  from the Controller without touching the OBS scene; hiding the score bar hides the other two
  with it. The same toggles are available on the Match Setup screen, so a stream can be
  prepared before a match starts.
- **Cue Ball Spin Overlay**: a second, independently placeable OBS browser source showing
  a cue ball graphic. The operator clicks it to drop a red contact-point dot showing the
  spin/english being used on a shot. Lives in its own browser source separate from the
  scoreboard, so it can be sized and positioned anywhere in the scene.
- **Setup Presets**: save a venue's whole setup — colours, scoreboard style, show/hide, how the clock is drawn,
  game type, Race-To mode, both shot-clock lengths and the overlay port — under a name, then swap between venues
  with one pick on the Match Setup screen.
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

Start one Controller at a time: a second launch brings the running window to the front, records why it is
leaving in the log and exits — two copies would otherwise share `%AppData%` and compete for the overlay port.
If the overlay cannot start because something else holds the port, the window says so — naming the process
when it can identify it — and offers a **Retry**, so the overlay can come back without restarting the app. The
port is editable too: the **OVERLAY PORT** field in the top bar takes 1024–65535, rebinds the host as soon as it
loses focus, and is saved in `settings.json`, so another program sitting on the default 51234 costs one edit rather
than the overlay for the rest of the session. The
same banner returns if the host stops serving mid-session — the state endpoint is probed every few seconds
while it runs — so a dead overlay is noticed during a match rather than after it.
If the port is held by a copy of this app that shows **no window** — a Controller that outlived its window, or one
from an older build — the Controller closes it and starts the overlay itself, which is the one case where **Retry**
could never have won on its own. A copy that still shows a window is somebody's session: the banner names it, asks for that window to be closed,
and never closes it for them — closing it by hand hands the port back, and Retry takes it from there. Another
program holding the port is only named, never touched.

An OBS browser source that was already connected rides out that outage by itself: the page holds its last
frame while the server is down, and within a few seconds of **Retry** it is live again with no refresh in OBS
and no lost score change. The pages use `EventSource`, which reconnects on its own and is handed a fresh
snapshot when it does. That was checked against the scoreboard page in Chromium (not OBS's own CEF); the
cue-ball page uses the same pattern but was not exercised.

Add the overlay as an OBS **Browser Source** pointed at the local URL the Controller
shows in **Help** — `http://127.0.0.1:51234/overlay/scoreboard` at the default port — and
the cue-ball overlay as a second, separate Browser Source at
`http://127.0.0.1:51234/overlay/cueball`. If the port was changed, Help and the Stream Deck
page in this repo's `streamdeck/` folder both use the new one.

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
- `/api/control/*` — local-only HTTP endpoints for Stream Deck buttons (add or subtract a
  point per side, undo the last point, toggle shooter, assign ball group, toggle a ball's
  pocketed state, new rack, reset match).
- Bound to `localhost` only — never reaches out to the internet, and nothing outside the
  machine can reach it either.

## Development Notes

- All projects target `.NET 8.0`.
- Nullable reference types enabled.
- Follow MVVM in the WPF Controller.
- Core stays UI-free and fully unit-testable.
- Persistence lives in `%AppData%\PoolScoreboard\`: `settings.json` holds the colour theme,
  scoreboard style, show/hide state and the last two players, while `roster.json` holds the saved
  player list and `presets.json` the named setups Match Setup offers. End-cap artwork is stored as files under `icons\`, not inline. The match in progress is
  written to `match.json` after every change — scores, game type and Race-To mode, ball groups, pocketed balls
  and whose turn it is — and removed when the match is reset, so a crash mid-match resumes where it was on the
  next launch. There is still no match history.
- Diagnostics land in `%AppData%\PoolScoreboard\controller.log`, appended across runs with a
  "Controller started" line per session. Shutdown warnings (such as the overlay host missing its stop
  timeout) go there, since by then there is no window to show them in. ASP.NET Core's own warnings and errors
  from the overlay host — a failed bind, a fault while serving — are forwarded there too. An exception that
  kills the app is recorded with its stack trace, and a `settings.json` or `roster.json` that cannot be read
  or written says so there instead of failing silently — a file that cannot be read is first moved to
  `settings.corrupt.json` or `roster.corrupt.json`, so the next save cannot destroy the operator's setup; a
  rejected roster's artwork moves with it into `roster.corrupt.icons\`. Lines are appended
  one at a time and nothing holds the file open, so it can be read while the Controller runs. A log that
  grows past 1 MiB is rotated at the next startup to `controller.old.log`, keeping one previous run.
- Pushing runs the same Release build and full test suite as CI. Enable the committed hook once
  per clone with `git config core.hooksPath .githooks`; bypass it deliberately with
  `git push --no-verify`.
