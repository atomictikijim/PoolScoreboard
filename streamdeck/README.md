# Stream Deck button mapping

For a step-by-step walkthrough of actually setting this up in the Stream Deck app, see
[SETUP.md](SETUP.md). This file is the reference table of endpoints — what each button hits and
what it does.

No custom Elgato SDK plugin is used — every button below is built in the Stream Deck app using
its built-in **Website** action, pointed at the local URL in the "Endpoint" column, with
**"Run in background"** (a.k.a. "don't open a browser window") checked. Every endpoint is a plain
`GET` request with no request body, since that's all the built-in action can send. All endpoints
are served by the Controller's in-process Kestrel host on `127.0.0.1:51234` — the Controller must
be running for any of these to work, and they never leave the local machine.

There is no `.streamDeckProfile` file checked into this repo (Stream Deck profiles are a binary
export tied to a specific physical device layout) — build one manually in the Stream Deck app
using the mapping below, then export/share it from there if needed.

## Suggested layout

| Button label     | Endpoint                                | Effect |
|-------------------|------------------------------------------|--------|
| Home +1           | `/api/control/score/home/add`             | Adds one point to the home player. Ends the match if it reaches their Race-To target. |
| Away +1           | `/api/control/score/away/add`             | Adds one point to the away player. Ends the match if it reaches their Race-To target. |
| Undo              | `/api/control/score/undo`                 | Undoes the last scored point (decrements whichever player is currently ahead). |
| Toggle Shooter    | `/api/control/shooter/toggle`              | Switches the current-shooter indicator to the other player. |
| Home: Solids      | `/api/control/balls/home/solids`           | 8-ball only. Assigns Solids to the home player (away player auto-flips to Stripes). |
| Home: Stripes     | `/api/control/balls/home/stripes`          | 8-ball only. Assigns Stripes to the home player (away player auto-flips to Solids). |
| Away: Solids      | `/api/control/balls/away/solids`           | 8-ball only. Assigns Solids to the away player (home player auto-flips to Stripes). |
| Away: Stripes     | `/api/control/balls/away/stripes`          | 8-ball only. Assigns Stripes to the away player (home player auto-flips to Solids). |
| Ball N Pocketed   | `/api/control/balls/N/toggle` (N = 1-15)   | Toggles ball N's pocketed state — greys it out in the ball display if it was live, or brings it back if it was already marked pocketed. Same toggle behavior as clicking the ball in the Controller's live view. Returns `400` for N outside 1-15. |
| New Rack          | `/api/control/rack/new`                    | Clears all pocketed-ball markers for a fresh rack. Does not touch scores. |
| Reset Match       | `/api/control/match/reset`                 | Ends the current match entirely and returns the Controller to the Match Setup screen — same as clicking "Reset Match" in the app. Use between matches, not mid-rack. |

## Notes

- These endpoints assign/score against whichever two players are currently loaded into
  `GameManager` — there's no player selection parameter, since the app only ever tracks one
  match at a time.
- Score/undo/shooter-toggle/ball-assignment buttons no-op safely if no match is active yet (e.g.
  pressed while the Controller is still on the Match Setup screen) — pressing them won't crash or
  desync anything, they just have no effect until "Start Match" has been clicked.
- "Reset Match" is destructive: it clears the loaded players, color theme, and scoreboard style
  back to defaults, exactly like the in-app button. It's meant for wrapping up one match before
  setting up the next, not for a mid-match do-over — use "New Rack" (or "Undo") for that instead.
- The cue-ball overlay's "Clear" button (`/overlay/api/cueball/clear`) is intentionally left off
  this table — it's meant to be clicked directly on the `/overlay/cueball` OBS browser source via
  OBS's "Interact" mode, not wired to a physical Stream Deck button, though nothing stops you from
  adding one if you'd prefer that workflow.
