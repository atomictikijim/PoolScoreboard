# User Acceptance Tests

Manual checks for behaviour a unit test cannot see: what a window looks like, what survives closing the app, and
what a person would call wrong. The automated suite covers the logic behind each of these (see CLAUDE.md,
"Testing Notes"); this file is the part that still needs eyes and a mouse.

Run them against a Debug build of the Controller:

```powershell
dotnet build
PoolScoreboard.Controller\bin\Debug\net8.0-windows\PoolScoreboard.Controller.exe
```

Record the outcome next to each scenario when you run it, and add a scenario here when you add a behaviour worth
checking by hand. Match history is filed to `%AppData%\PoolScoreboard\history.json`; deleting that file (with the
app closed) resets every scenario below to a blank history.

## Match history

Opened from the **History** button in the top bar, beside **Help**. Read-only: it lists the recorded matches and
how many each name has won.

### MH-1 — Empty history

*Precondition:* no `history.json` (or delete it with the app closed, then relaunch).

1. Click **History** before playing anything.

*Expected:* the window opens at once with no win chips and the sentence "No matches recorded yet — finish a match
and it appears here." Close returns to whichever screen was behind it.

### MH-2 — A finished match is recorded

*Precondition:* empty history; two different player names filled in; 8-Ball, Race-To 2.

1. Press **START MATCH**.
2. Score Home to 2 so the winner banner appears.
3. Click **History**.

*Expected:* one row, showing today's local date and time, the Home name, `2 – 0`, the Away name, and **Won by**
the Home name. One win chip reading the Home name with 1. `history.json` now exists and is readable JSON with a
`matches` list and `"fileVersion": 1`.

### MH-3 — Newest first

*Precondition:* MH-2 done.

1. Press **RESET MATCH**, start a new match (Race-To 3), and let Away win 0–3.
2. Click **History**.

*Expected:* the Away win is the **first** row (`0 – 3`, Won by Away) and the Home win is below it. Chips read both
names with 1 win each, in alphabetical order.

### MH-4 — One record per match, no matter what happens afterwards

*Precondition:* a finished match with the winner banner showing.

1. Press the shooter toggle; pocket two balls; press **NEW RACK**.
2. Click **History**, close it, and click it again.

*Expected:* exactly one row for that match — same time, same score, every time.

### MH-5 — One record per match across a relaunch

*Precondition:* a finished match still showing (do not press RESET).

1. Close the window, relaunch the app.
2. Click **History**.

*Expected:* the same single row. A resumed match is the match that was already filed, not a second one.

### MH-6 — A rematch is a new record

*Precondition:* MH-2 done, same two players.

1. **RESET MATCH**, start again, and win 2–0 the same way.
2. Click **History**.

*Expected:* two rows with identical names and score but different times, and the Home chip now reads 2.

### MH-7 — Correcting the winning point does not re-file the match

*Precondition:* a match Home has just won at 2–0.

1. Open **History** and note the row count.
2. Press Home **−** (the banner clears), then Home **+** (the banner returns).
3. Open **History** again.

*Expected:* the same rows as before — the correction is the same match, not a new one.

### MH-8 — One player across spellings

*Precondition:* empty history.

1. Play three matches, typing the same person's name as `Bob`, then `bob`, then `Bob ` (trailing space).

*Expected:* one chip for that person with 3 wins, not three chips. The spelling shown is the one from the most
recent match.

### MH-9 — Split race, and a side with no name

*Precondition:* empty history; Race-To **Split** with Home 7 and Away 5; Away's name left blank.

1. Start the match and let Home win 7–4.
2. Click **History**.

*Expected:* the row reads `7 – 4`, the Away cell shows `—` rather than being blank or crashing, **Won by** names
Home, and only Home appears in the chips.

### MH-10 — Read-only and modal

*Precondition:* at least one recorded match.

1. Open **History** and try to edit anything; click a row; try to use the window behind it.
2. Close the window with the button, then reopen and close it with the title-bar X.

*Expected:* nothing is editable, rows do nothing when clicked, the main window does not respond while the history
is open, and both ways of closing work — the app and the overlay keep running.

### MH-11 — A damaged history file

*Precondition:* app closed; `history.json` replaced with the text `{ not json`.

1. Relaunch and click **History**.

*Expected:* the empty-state sentence rather than a crash, and the bad file preserved as `history.corrupt.json`.
Playing a match afterwards starts a fresh history.

### MH-12 — Nothing else moved

*Precondition:* any.

1. Compare the setup screen and the live view with how they looked before the history existed.

*Expected:* unchanged, with only **History** added to the top bar next to **Help**. History still opens while the
overlay is off (for example with another program holding the overlay port).
