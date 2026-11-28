# Stream Deck button mapping

For a step-by-step walkthrough of actually setting this up in the Stream Deck app, see
[SETUP.md](SETUP.md). This file is the reference table of endpoints — what each button hits and
what it does.

No custom Elgato SDK plugin is used — every button below is built in the Stream Deck app using
its built-in **Website** action, pointed at the local URL in the "Endpoint" column, with
**"Run in background"** (a.k.a. "don't open a browser window") checked. Every endpoint is a plain
`GET` request with no request body, since that's all the built-in action can send. All endpoints
are served by the Controller's in-process Kestrel host on `127.0.0.1:51234` — the Controller's default
port, and the one the table below uses. If you changed it with the **OVERLAY PORT** field, use that
number in every URL instead. The Controller must be running for any of these to work, and they never
leave the local machine.

There is no `.streamDeckProfile` file checked into this repo (Stream Deck profiles are a binary
export tied to a specific physical device layout) — build one manually in the Stream Deck app
using the mapping below, then export/share it from there if needed.

## Suggested layout

| Button label     | Endpoint                                | Effect |
|-------------------|------------------------------------------|--------|
| Home +1           | `/api/control/score/home/add`             | Adds one point to the home player. Sets the winner banner if it reaches their Race-To target — the match stays fully editable afterward. |
| Away +1           | `/api/control/score/away/add`             | Adds one point to the away player. Sets the winner banner if it reaches their Race-To target — the match stays fully editable afterward. |
| Home -1           | `/api/control/score/home/subtract`        | Removes one point from the home player (does nothing below 0). Clears the winner banner if their score drops back below their Race-To target. |
| Away -1           | `/api/control/score/away/subtract`        | Removes one point from the away player (does nothing below 0). Clears the winner banner if their score drops back below their Race-To target. |
| Undo              | `/api/control/score/undo`                 | Reverses the most recently scored point, taking it off whichever player scored it — including at a tie. Does nothing once the board is back to 0–0. |
| Toggle Shooter    | `/api/control/shooter/toggle`              | Switches the current-shooter indicator to the other player. |
| Home: Solids      | `/api/control/balls/home/solids`           | 8-ball only. Assigns Solids to the home player (away player auto-flips to Stripes). |
| Home: Stripes     | `/api/control/balls/home/stripes`          | 8-ball only. Assigns Stripes to the home player (away player auto-flips to Solids). |
| Away: Solids      | `/api/control/balls/away/solids`           | 8-ball only. Assigns Solids to the away player (home player auto-flips to Stripes). |
| Away: Stripes     | `/api/control/balls/away/stripes`          | 8-ball only. Assigns Stripes to the away player (home player auto-flips to Solids). |
| Ball N Pocketed   | `/api/control/balls/N/toggle` (N = 1-15)   | Toggles ball N's pocketed state — greys it out in the ball display if it was live, or brings it back if it was already marked pocketed. Same toggle behavior as clicking the ball in the Controller's live view. The money ball (8 in 8-ball, 9 in 9-ball, 10 in 10-ball) also stops a running shot clock, because potting it ends the rack. Returns `400` for N outside 1-15. |
| New Rack          | `/api/control/rack/new`                    | Clears all pocketed-ball markers for a fresh rack. Does not touch scores. |
| Reset Match       | `/api/control/match/reset`                 | Ends the current match entirely and returns the Controller to the Match Setup screen — same as clicking "Reset Match" in the app. Use between matches, not mid-rack. |
| Start Shot Clock  | `/api/control/shotclock/start`             | Starts the shot clock on the overlay at the length set in Match Setup (40 seconds by default), or restarts it from the full time for the next shot. A running clock restarts by itself when a point is scored, a ball leaves the table or the shooter is toggled; a new rack, a reset or a won match hides it. |
| Hide Shot Clock   | `/api/control/shotclock/clear`             | Hides the shot clock. The overlay draws it only while it is running, so this is "no clock on screen", not "paused". |
| Call Extension    | `/api/control/shotclock/extension`         | The player's extension: adds the extension time set in Match Setup (30 seconds by default) to the clock already running for the current shooter. One per player per rack — a second call in the same rack does nothing, and a new rack gives both players theirs back. Does nothing while no clock is running, and does not use the extension up. |

## Notes

- These endpoints assign/score against whichever two players are currently loaded into
  `GameManager` — there's no player selection parameter, since the app only ever tracks one
  match at a time.
- Score/undo/shooter-toggle/ball-assignment buttons no-op safely if no match is active yet (e.g.
  pressed while the Controller is still on the Match Setup screen) — pressing them won't crash or
  desync anything, they just have no effect until "Start Match" has been clicked.
- A player reaching their Race-To target only sets the winner banner — it does not lock the match, and the shot
  clock goes with the banner rather than counting down behind it.
  Home +1/-1, Away +1/-1, Undo, shooter-toggle and ball buttons all keep working after a win, so a
  scoring mistake spotted afterwards can still be corrected without "Reset Match".
- "Reset Match" ends the match and returns the Controller to Match Setup, exactly like the in-app
  button — but it is not a factory reset. Scores, undo history, pocketed balls and the winner are
  cleared; the loaded players and their end-cap icons, the color theme, scoreboard style, overlay
  show/hide settings, and the chosen game and Race-To mode all stay exactly as they were, so the
  next match can start with one press of Start Match. Use "New Rack" (or "Undo") for a mid-match
  do-over instead.
- The cue-ball overlay's "Clear" button (`/overlay/api/cueball/clear`) is intentionally left off
  this table — it's meant to be clicked directly on the `/overlay/cueball` OBS browser source via
  OBS's "Interact" mode, not wired to a physical Stream Deck button, though nothing stops you from
  adding one if you'd prefer that workflow.
