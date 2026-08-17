# Stream Deck setup guide

Step-by-step instructions for wiring a physical Elgato Stream Deck to control PoolScoreboard.
No plugin install is required — every button is a built-in **Website** action pointed at a local
URL. For the full list of endpoints and what each one does, see [README.md](README.md); this
guide is about the Stream Deck software mechanics of setting them up.

## Before you start

- The Stream Deck app and PoolScoreboard.Controller must run **on the same PC**. The Controller's
  control server only listens on `127.0.0.1` (localhost) — that's intentional (CLAUDE.md's
  offline/local-only rule) but it means a Stream Deck plugged into a different machine on the same
  network can't reach it.
- **Launch PoolScoreboard Controller before you test any button.** The `/api/control/*` endpoints
  only exist while the Controller process is running; with it closed, every button will fail
  silently (or show a Stream Deck error icon, depending on your software version).
- You don't need a match started to set the buttons up — they're safe to press with no active
  match, they just won't do anything until a match is actually running (see "Notes" in
  README.md).

## Step 1 — Confirm the local server is reachable

With the Controller running, open a browser on the same PC and go to:

```
http://127.0.0.1:51234/overlay/api/scoreboard/state
```

You should see a small block of JSON (game type, scores, colors, etc.). If you get a "can't
connect" error instead, the Controller isn't running or hasn't finished starting yet — fix that
before moving on, since every Stream Deck button depends on this same server.

## Step 2 — (Optional) Create a dedicated profile

In the Stream Deck app, profiles let you switch your whole button layout at once (e.g., leaving
your streaming-software controls on one profile and pool-scoring buttons on another).

1. Open the Stream Deck app.
2. Click the **+** under your device's profile list (usually on the left side) to add a new
   profile.
3. Name it something like **PoolScoreboard**.

This step is optional — you can just as easily drop these buttons onto an existing profile or a
folder within one.

## Step 3 — Add your first button (Home +1)

1. On the right-hand side of the Stream Deck app, find the **Website** action under the
   **System** category and drag it onto an empty button.
2. With the button selected, fill in its action settings:
   - **Title**: `Home +1` (this is the text shown on the button's icon)
   - **Website**: `http://127.0.0.1:51234/api/control/score/home/add`
3. Look for a checkbox/toggle near the URL field for opening the page in a browser (wording
   varies by Stream Deck software version — you may see "Open in browser", or a "Run in
   background" toggle). **Make sure it's set so the URL is fetched in the background, not
   opened in a browser window** — otherwise every button press will pop up (and stack up) browser
   tabs instead of quietly hitting the endpoint.
4. Optionally set an icon/color for the button so it's easy to find at a glance.
5. Press the button once to test it — nothing visually dramatic should happen on the Stream Deck
   itself, but if you have `/overlay/scoreboard` open in a browser tab (or in OBS) with a match
   started, you should see the home player's score increase immediately.

## Step 4 — Repeat for the rest of the buttons

Repeat Step 3 for each row in the table in [README.md](README.md) — same **Website** action,
same "run in background" setting, just a different **Title** and **Website** URL each time:

| Title           | Website (URL)                                              |
|-----------------|--------------------------------------------------------------|
| Home +1         | `http://127.0.0.1:51234/api/control/score/home/add`           |
| Away +1         | `http://127.0.0.1:51234/api/control/score/away/add`           |
| Home -1         | `http://127.0.0.1:51234/api/control/score/home/subtract`      |
| Away -1         | `http://127.0.0.1:51234/api/control/score/away/subtract`      |
| Undo            | `http://127.0.0.1:51234/api/control/score/undo`                |
| Toggle Shooter  | `http://127.0.0.1:51234/api/control/shooter/toggle`            |
| Home: Solids    | `http://127.0.0.1:51234/api/control/balls/home/solids`         |
| Home: Stripes   | `http://127.0.0.1:51234/api/control/balls/home/stripes`        |
| Away: Solids    | `http://127.0.0.1:51234/api/control/balls/away/solids`         |
| Away: Stripes   | `http://127.0.0.1:51234/api/control/balls/away/stripes`        |
| New Rack        | `http://127.0.0.1:51234/api/control/rack/new`                  |
| Reset Match     | `http://127.0.0.1:51234/api/control/match/reset`                |

### Ball display buttons (1-15)

These follow the same pattern but with a ball number baked into the URL, so you need one button
per ball you want to control — there's no single "any ball" button. Set the **Title** to the ball
number (e.g., `1`, `9`, `15`) and the **Website** field to:

```text
http://127.0.0.1:51234/api/control/balls/<N>/toggle
```

replacing `<N>` with that ball's number (1 through 15). Each press **toggles** that ball's
pocketed state — press it once to grey the ball out in the ball display, press again to bring it
back if you marked it by mistake. There's no separate "pocket" vs "unpocket" button, matching how
clicking a ball in the Controller's own live view already works.

You don't need all 15 wired up — pick whatever your game type actually uses:

- **9-ball**: balls 1-9
- **10-ball**: balls 1-10
- **8-ball**: balls 1-7 and 9-15 (the two groups), plus 8 for the 8-ball itself — assigning which
  group belongs to which player still needs the Home/Away Solids/Stripes buttons above; these
  per-ball buttons only mark them pocketed, independent of group assignment (see CLAUDE.md's
  "Ball Display Detail")

Fifteen individual buttons is a lot of Stream Deck real estate — a 6-key Mini won't fit them
alongside everything else. Consider a dedicated folder/profile page just for ball toggles, or
skip them entirely and mark pocketed balls by clicking directly in the Controller window instead
(the Stream Deck buttons are an addition, not a replacement for that).

A few layout suggestions, entirely up to you:

- Group the two ball-assignment buttons per side together (8-ball only — you can leave them off
  the layout entirely for 9-ball/10-ball matches).
- Put **Reset Match** on its own, ideally in a folder/sub-page rather than sitting next to the
  scoring buttons — it's the one destructive action in the list (see README.md's Notes), and you
  don't want a mis-press mid-match to wipe the scoreboard.
- If your Stream Deck has fewer keys than the full list, prioritize Home +1 / Away +1 / Undo /
  Toggle Shooter for the main page, and put ball-assignment + rack/reset controls on a folder or
  second profile page.

## Step 5 — Full test pass

1. Start a match in the Controller (any game type, any players).
2. Open `/overlay/scoreboard` in a browser tab (or watch it live in OBS) so you can see the
   effect of each press.
3. Press through every button once, confirming:
   - **Home +1** / **Away +1** increase the correct side's score.
   - **Home -1** / **Away -1** decrease the correct side's score (it does nothing below 0 — that's
     expected, not a bug).
   - **Undo** decreases whichever side is currently ahead (it does nothing at a tie — that's
     expected, not a bug).
   - Pushing a side to their Race-To target shows the winner banner but doesn't lock anything —
     confirm **Home +1** / **Away +1** / **Home -1** / **Away -1** still work afterward, so a
     mistaken final point can be corrected without needing "Reset Match".
   - **Toggle Shooter** flips the shooter indicator to the other player.
   - **Home/Away: Solids/Stripes** (8-ball only) assigns the ball group and auto-flips the other
     player's group.
   - Each **ball toggle button** greys that ball out in the ball display, and un-greys it if you
     press the same button again.
   - **New Rack** clears any pocketed-ball markers without touching the score.
   - **Reset Match** ends the match and returns the Controller to the Match Setup screen.
4. Re-run Step 5 after any change to your Controller/Overlay setup (e.g., after an app update) to
   make sure the endpoints still line up.

## Troubleshooting

- **Button does nothing, no error**: Controller isn't running, or the URL has a typo. Re-check
  Step 1.
- **A browser window pops up every time you press a button**: the "run in background"/"open in
  browser" setting from Step 3.3 isn't set the way you want — revisit that button's action
  settings.
- **Everything worked, then suddenly stopped**: the Controller was closed or restarted. Since
  there's no persistence yet (see PROGRESS.md's Known Gaps), a Controller restart also clears the
  match — you'll need to start a new match and the buttons will work again immediately (they
  don't need any reconfiguration after a restart, since the URLs never change).
- **Ball-assignment buttons seem to do nothing**: they're 8-ball-only by design (see
  `GameManager.AssignBallGroup`) — they safely no-op for 9-ball/10-ball matches.
- **A ball toggle button shows an error / does nothing**: double check the number in the URL is
  between 1 and 15 — anything outside that range is rejected outright.

## Not covered here: the cue-ball overlay

The cue-ball spin overlay (`/overlay/cueball`) isn't controlled by Stream Deck buttons — its
contact dot is placed by clicking directly on the OBS browser source (via OBS's right-click →
**Interact** on the source) and cleared with the "Clear" button built into that page itself. See
README.md's Notes for why, and PROGRESS.md's Phase 4 entry for how that page works.
