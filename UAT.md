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

A finished match is recorded **only** when **SAVE TO HISTORY** is pressed — the button appears with the winner
banner and is disabled, reading "Saved to history" beside it, once that match is in the file. The **History**
button in the top bar, beside **Help**, lists what has been saved, newest first, with each name's wins, and lets a
single match be deleted behind a confirmation.

### MH-1 — Empty history

*Precondition:* no `history.json` (or delete it with the app closed, then relaunch).

1. Click **History** before playing anything.

*Expected:* the window opens at once with no win chips and the sentence "No matches recorded yet — finish a match
and it appears here." No rows, no header over an empty table. Close returns to whichever screen was behind it.

### MH-2 — A win is not a record until you save it

*Precondition:* empty history; two different player names filled in; 8-Ball, Race-To 2.

1. Press **START MATCH** and score Home to 2 so the winner banner appears.
2. Before saving, click **History** and close it.
3. Back on the live view, press **SAVE TO HISTORY**.

*Expected:* at step 2 the history is still empty — the win alone wrote nothing. At step 3 "Saved to history"
appears beside a now-disabled button, `history.json` exists and is readable JSON with a `matches` list and
`"fileVersion": 1`, and **History** shows one row: today's local date and time, the Home name, `2 – 0`, the Away
name, **Won by** the Home name, and one win chip reading the Home name with 1.

### MH-3 — SAVE TO HISTORY is absent until there is a winner

*Precondition:* any.

1. Look at the live view at match setup and again mid-match at 1–0.
2. Finish the match.

*Expected:* no save button at setup or mid-match, and no "Saved to history" text; both appear with the winner
banner, and both disappear again after **RESET MATCH**.

### MH-4 — One record per match, however often you press save

*Precondition:* MH-2 done, with the winner banner still showing.

1. Press **SAVE TO HISTORY** again if it will take a press.
2. Toggle the shooter, pocket two balls, press **NEW RACK**.
3. Click **History**, close it, and click it again.

*Expected:* the button reads "Saved to history" and is disabled after the first save; steps 2 and 3 leave exactly
one row for that match — same time, same score, every time.

### MH-5 — A saved match stays saved across a relaunch

*Precondition:* a saved match still showing with its banner (do not press RESET).

1. Close the window, relaunch the app.
2. Look at the live view, then click **History**.

*Expected:* the banner and "Saved to history" are both there, the button is disabled, and History shows the same
single row — the app read the file rather than offering to save the match a second time.

### MH-6 — A rematch is a new record

*Precondition:* MH-2 done, same two players.

1. **RESET MATCH**, start again, win 2–0 the same way, and press **SAVE TO HISTORY**.

*Expected:* the button is offered again for the new match (it is not still marked saved), and History shows two
rows with identical names and score but different times; the Home chip reads 2.

### MH-7 — Correcting the winning point does not need a second save

*Precondition:* a match just saved, Home won at 2–0.

1. Press Home **−** (the banner clears), then Home **+** (the banner returns).
2. Open **History**.

*Expected:* the save button comes back with the cleared banner but reads "Saved to history" and is disabled once
the banner returns, and History still has one row for that match — the correction did not make a new match.

### MH-8 — One player across spellings

*Precondition:* empty history.

1. Play and save three matches, typing the same person's name as `Bob`, then `bob`, then `Bob ` (trailing space).

*Expected:* one chip for that person with 3 wins, not three chips. The spelling shown is the one from the most
recent match.

### MH-9 — Split race, and a side with no name

*Precondition:* empty history; Race-To **Split** with Home 7 and Away 5; Away's name left blank.

1. Start the match, let Home win 7–4, and save it.
2. Click **History**.

*Expected:* the row reads `7 – 4`, the Away cell shows `—` rather than being blank or crashing, **Won by** names
Home, and only Home appears in the chips.

### MH-10 — Deleting one match

*Precondition:* at least two saved matches with different players.

1. Open **History** and press **Delete** on the newest row.
2. Answer **No** to the confirmation.
3. Press **Delete** again and answer **Yes**.
4. Close the window, reopen it, and look at the win chips.

*Expected:* step 2 changes nothing. Step 3 removes just that row — the others stay in their order, and any chip
that existed only because of the deleted match is gone while the rest keep their counts. Step 4 shows the same:
the deletion is in the file, not just on screen. `history.json` no longer contains that match.

### MH-11 — Deleting the last match

*Precondition:* exactly one saved match.

1. Open **History**, delete it, and confirm.

*Expected:* the placeholder sentence from MH-1 comes back, the chips are empty, and the window does not close or
error.

### MH-12 — Read-and-delete only, and modal

*Precondition:* at least one saved match.

1. Open **History**: try to edit anything, click a row's text, and try to use the window behind it.
2. Close with the button, then reopen and close with the title-bar X.

*Expected:* nothing is editable, row text does nothing when clicked, Delete and Close are the only controls, the
main window does not respond while the history is open, and both ways of closing work — the app and the overlay
keep running.

### MH-13 — A damaged history file

*Precondition:* app closed; `history.json` replaced with the text `{ not json`.

1. Relaunch and click **History**.

*Expected:* the empty-state sentence rather than a crash, and the bad file preserved as `history.corrupt.json`.
Opening History must not create a new `history.json` on its own, and saving a new match afterwards starts a fresh
history.

### MH-14 — Nothing else moved

*Precondition:* any.

1. Compare the setup screen and the live view with how they looked before the history existed.

*Expected:* unchanged, except that the live view gains **SAVE TO HISTORY** beside "Saved to history" when a match
is won, and the top bar gains **History** next to **Help**. History still opens while the overlay is off (for
example with another program holding the overlay port).
