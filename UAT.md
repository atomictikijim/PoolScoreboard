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
5. If you deleted the match still won behind the dialog, look at the live view after closing it.

*Expected:* step 2 changes nothing. Step 3 removes just that row — the others stay in their order, and any chip
that existed only because of the deleted match is gone while the rest keep their counts. Step 4 shows the same:
the deletion is in the file, not just on screen. `history.json` no longer contains that match. At step 5 the
banner is still up but "Saved to history" is gone and **SAVE TO HISTORY** is offered again, because the record it
was reporting has been deleted.

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

## Shot clock

The overlay draws a countdown only while a shot's clock is running. It is started by `T` on the live view or by the
Stream Deck start button, at the length chosen in Match Setup, and it is not the same thing as a match timer: a new
rack, a reset or a won match hides it. Each player can call one extension per rack with `E`, the CALL EXTENSION
button, or the Stream Deck button.

### SC-1 — Nothing before a match

1. At Match Setup, press `T`.

*Expected:* nothing happens — no clock on the overlay, nothing in the Controller. The key is live-view only, like
the others.

### SC-2 — A countdown, started by hand

*Precondition:* a match running, the overlay open in a browser.

1. Press `T`.
2. Watch the overlay without touching anything else for the full countdown.

*Expected:* a "SHOT 0:40" pill appears under the score bar and counts down on its own to 0:00, turning red for the
last five seconds. It reaches 0:00 and stays there — OBS keeps showing it, the app is not frozen.

### SC-3 — Pressing again restarts the whole shot

1. Start the clock, wait about 15 seconds, then press `T` again.

*Expected:* the display jumps back to 0:40 rather than continuing from 0:25 — each press is a fresh shot.

### SC-4 — A point starts the next shot's clock

*Precondition:* a clock running.

1. Score a point (keyboard `Q` or `P`, or the Stream Deck).

*Expected:* the clock restarts from 0:40 for the next shot. A match where the clock was never started must not sprout
one when a point is scored.

### SC-5 — Hiding it

1. Start the clock, then press **NEW RACK**.
2. Start it again, then **RESET MATCH**.
3. Start it once more, then press the Stream Deck hide button (`/api/control/shotclock/clear`).

*Expected:* each of the three removes the clock from the overlay at once. Steps 1 and 2 also do what they already
did to the score and balls; nothing else on the overlay changes.

### SC-6 — What is written down

1. Open **Help** and read the Stream Deck endpoint list, then the shortcut list.

*Expected:* all three shot-clock endpoints appear with the current port, and both `T — start or restart the shot
clock` and `E — call the current shooter's extension` are listed with the other shortcuts.

### SC-7 — The lengths come from Match Setup

*Precondition:* the Controller on Match Setup.

1. Set **Shot length** to `20` and **Extension** to `10`, then START MATCH and press `T`.
2. Watch a full countdown, letting the field lose focus before starting.
3. Set **Shot length** to `2`, tab away, and start a match.

*Expected:* the pill counts down from 0:20 rather than 0:40. The `2` snaps back to `5` as soon as the field loses
focus, because that is the shortest length the clock will use — the field never claims a length the clock is not on.

### SC-8 — An extension, once each

*Precondition:* a match running with a clock started, extension set to 10.

1. Press `E` (or CALL EXTENSION).
2. Press it again straight away.
3. Toggle the shooter, then press `E`.
4. Press NEW RACK, start the clock, and press `E`.

*Expected:* step 1 adds 10 seconds to the countdown that is already running — it does not jump back to the full shot.
Step 2 changes nothing, because that player has had theirs. Step 3 adds 10 seconds for the other player, whose
extension was still there. Step 4 adds another 10, because a new rack gives both of them one back.

### SC-9 — A ball or a shooter change starts the next shot

*Precondition:* a clock running with a few seconds spent.

1. Click a ball in the live view to mark it pocketed.
2. Click the same ball again to bring it back.
3. Press Space to toggle the shooter.

*Expected:* step 1 restarts the countdown at the full length; step 2 changes nothing, because putting a ball back is
a correction rather than a shot; step 3 restarts it again. Nothing happens to the clock in any of the three if it was
never started.

### SC-10 — A won match has no clock

*Precondition:* a clock running, and a match one point from its target.

1. Score the winning point.
2. Press `T`, then `E`.

*Expected:* the clock disappears as the winner banner appears, rather than restarting behind it, and neither key
brings it back while the banner is up. RESET MATCH returns to setup with no clock.

### SC-18 — The keys work while OBS has focus

*Precondition:* a match running, OBS in front, the Controller behind it but visible so its score can be watched.

1. With OBS focused, press `Ctrl+Shift+Q` and `Ctrl+Shift+P`.
2. Press `Ctrl+Shift+Space`, then `Ctrl+Shift+A`.
3. Press `Ctrl+Shift+T`, then `Ctrl+Shift+E`.
4. Press `Ctrl+Shift+N`.
5. Click RESET MATCH in the Controller, click back into OBS, and press `Ctrl+Shift+Q` again.
6. Type into any text field in OBS while the chords are registered (during step 3, for instance), including an
   AltGr character such as `@` or `€` if the keyboard has one.

*Expected:* every chord in steps 1-4 changes the Controller exactly as its own key does — a point each side, the
shooter flipped and then put back by the undo, a clock started and then given its extension, a new rack leaving the
Clock waiting. Step 5 does nothing at all, because the chords are released at setup. Step 6 types normally: only
Ctrl+Shift+letter is taken, never a bare letter, the spacebar, or anything AltGr types.

### SC-17 — Undo takes back more than a point

*Precondition:* a match running, the live view in front, nothing scored yet.

1. Press `Space` to put the other player at the table, then press `A`.
2. In 8-ball, press a group button (SOLIDS or STRIPES), then press `A`.
3. Pocket a ball on the tracker, then press `A`.
4. Score a point, then press `A`.
5. Press `A` five more times with nothing left to undo.

*Expected:* each `A` puts the screen back to before the press in front of it — the shooter returns to where it was,
both groups read unassigned again, the ball comes back on the table, the score drops by one. Step 5 changes nothing
and does not crash: an empty history is simply ignored.

### SC-16 — The overlay says when its stream is dead

*Precondition:* the Controller running with a match up, OBS (or a browser) showing `/overlay/scoreboard`, and
`Stale Cue: Off` on the OVERLAY row.

1. With the cue off, close the Controller and watch the overlay for ten seconds.
2. Relaunch the Controller, press `Stale Cue` so it reads `On`, and confirm the overlay looks normal.
3. Close the Controller again and watch.
4. Relaunch and watch.

*Expected:* step 1 leaves the overlay exactly as it was — frozen numbers, no mark, by design. Step 3 shows the board
dimmed and drained of colour after about four seconds, with a red `SIGNAL LOST` badge at the bottom, and it stays
that way. Step 4 brings the board back to full colour and takes the badge away on the first frame, with no refresh.

### SC-15 — The live view says who still has an extension

*Precondition:* a match running, the Controller's live view in front, the header readable.

1. Read the two chips after `EXTENSIONS` before anything is called.
2. Press `T`, then CALL EXTENSION once for the current shooter.
3. Toggle the shooter to the other player and press CALL EXTENSION.
4. Toggle back to the first player and press CALL EXTENSION again.
5. Press NEW RACK.

*Expected:* step 1 reads `Home: 1 left  Away: 1 left` and CALL EXTENSION is live. Step 2 leaves the shooter's chip at
`Home: used`, dimmed, with the other side still `1 left`, and CALL EXTENSION greys out — it does not respond to a
click, and `E` does nothing either. Step 3 spends `Away` the same way, so the button is live again after the toggle
and grey again after the call. Step 4 leaves the greyed button alone, because there is nothing to press. Step 5 puts
both back to `1 left` with the button live again.

### SC-14 — The Controller reads the clock out too

*Precondition:* a match running, the Controller's live view in front.

1. Without starting anything, look at the top left of the live view.
2. Press NEW RACK.
3. Press `T` and watch for about five seconds.
4. Let the shot run past zero.

*Expected:* step 1 shows nothing — there is no clock. Step 2 shows `SHOT CLOCK 0:40 · waiting`. Step 3 changes to
`0:32 · running` and counts down on its own, second by second, without touching the overlay. Step 4 settles on
`0:00 · time up`, which stays until the clock is hidden or the rack is reset.

### SC-13 — A new rack resets the clock but does not start it

*Precondition:* a match running with a clock started, and the overlay open in a browser.

1. Press `N` (NEW RACK), then wait about fifteen seconds without touching anything.
2. Press `T`.
3. Press NEW RACK again, then press the Stream Deck hide button (`/api/control/shotclock/clear`).

*Expected:* step 1 leaves the clock on screen at the full shot length, dimmed and not counting — a fresh rack's
allowance, waiting. Step 2 starts it from the full time. Step 3 removes the waiting clock, so hiding works on a
clock that was never started.

### SC-12 — The money ball stops the clock

*Precondition:* a match running with a clock started, and a rack where the money ball is still on the table.

1. Mark any other ball pocketed.
2. Start the clock again, then mark the money ball pocketed (the 8 in 8-Ball, 9 in 9-Ball, 10 in 10-Ball).
3. Start the clock, then click the money ball again to bring it back, and start it once more.

*Expected:* step 1 restarts the countdown for the next shot. Step 2 removes the clock from the overlay instead of
restarting it, because the rack is over. Step 3 shows the same "bring it back and restart" behaviour as any other
ball — the clock only stops when the money ball leaves the table, not when it is put back.

### SC-11 — The countdown as a line, or as numbers

*Precondition:* a match running with the overlay open in a browser.

1. Press **Shot Clock: Time** in the OVERLAY row, then start the clock with `T`.
2. Watch the shot out.
3. Press the button again (it now reads **Shot Clock: Line**) and start another shot.
4. Restart the Controller and read the button's label.

*Expected:* step 1 swaps the pill's numbers for a green line that drains from the right and runs out as the time
does. Step 2 turns it red for the last five seconds, and at 0:00 the whole track fills red rather than going
empty — an empty line would look like a clock nobody started. Step 3 brings the numbers back, and both drawings
agree about when the clock is nearly up. Step 4 still shows **Shot Clock: Line**,
because the choice is saved with the rest of the look.
