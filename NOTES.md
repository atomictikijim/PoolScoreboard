# Notes

Running log of issues discovered during development and the fixes used.
Newest entries at the top.

## 2026-08-13 — Phase 3 overlay: SSE over polling, embedded assets over wwwroot

**Decision:** For `/overlay/scoreboard` live updates, used Server-Sent Events (native
`EventSource` in JS, no library) instead of polling — CLAUDE.md's "avoid a hard requirement on
JS libraries pulled from a CDN" plus the desire for instant updates on every operator action
(not just on a poll tick) both pointed at SSE. Each SSE connection subscribes to
`GameManager.GameStateChanged` and relays through a per-connection `Channel<string>`, since the
event fires from whatever thread called into `GameManager` (the WPF UI thread) and writing
straight to the `HttpResponse` stream from that thread would race with the SSE loop's own
writes.

**Decision:** The scoreboard HTML/CSS/JS are `<EmbeddedResource>`s in `PoolScoreboard.Overlay`,
read via `Assembly.GetManifestResourceStream`, rather than physical files under `wwwroot` served
by `UseStaticFiles`. Reasoning: the Overlay project is hosted in-process by the Controller (not
run from its own project directory) and Phase 6 wants a single-file, self-contained publish —
embedding avoids figuring out `WebRootPath`/content-root relative to wherever the Controller's
executable ends up, and avoids extra `CopyToOutputDirectory` bookkeeping for the publish step.

**Why keep this entry instead of deleting old ones below:** NOTES.md is append-only by
convention — old entries stay as a record of what was tried, even when the direction changes.

## 2026-08-13 — WNT visual reference: style cues extracted from developer screenshot

**Input:** Developer supplied one screenshot of a WNT broadcast score bar (players "Duong Quoc
Hoang" vs. "Chang Tzu Chien", "Race to 10"). It shows only the static score bar — no
current-shooter highlight and no ball-tracker graphic — and the developer confirmed this is
all they have for now; more may follow later.

**Extracted design tokens** (see CLAUDE.md "Visual Reference" for the canonical copy):

- Single glossy "pill"-shaped bar, capsule-rounded ends, subtle top-lit gradient.
- Deep violet/indigo bar fill, bold white sans-serif text.
- White rounded-rect score badges, dark high-contrast numbers, inside the bar next to each
  player's name.
- Darker center segment holding "Race to N" in smaller white text — separates the two player
  halves without a hard divider line.
- Small circular end-cap badges (flag/team-sponsor mark) on black rounded caps at each end.

**Decision:** Recorded as reference only for now (per developer's choice) — not yet applied to
any code. It's earmarked for the `/overlay/scoreboard` page in Phase 3; the Controller's own
WPF console is unaffected. Current-shooter-indicator and ball-tracker treatment remain open
questions pending more screenshots; Phase 3 will need reasonable defaults for those in the
interim.

## 2026-08-13 — WNT visual reference: capability limits on watching YouTube video

**Ask:** Developer wants the scoreboard styled with additional cues from the World Nine Ball
Tour (WNT / Matchroom Pool) broadcast scoreboard, which requires reviewing WNT YouTube match
footage.

**Limitation found:** Claude Code cannot watch/analyze YouTube video frame-by-frame — there is
no video-content tool available here, only text web search and single-page fetch (HTML→text).
A search for WNT broadcast scoreboard design turned up only tournament/news pages (AZBilliards,
Wikipedia, worldnineballtour.com, wntlivescores.com) and third-party OBS overlay products
(BallStream, CueSport Scoreboard) — nothing with actual visual specs of WNT's on-screen graphic.

**Resolution in progress:** Asked the developer to supply a few screenshots pulled from WNT
YouTube broadcasts (paste as images, or file paths) instead — screenshots can be read and
analyzed directly, unlike video. Style choices (colors, layout, ball-tracker treatment) will be
derived from those once provided. See PROGRESS.md for the pending task.

## 2026-08-13 — Pocketed-ball tracking confirmed in scope: balls render greyed out

**Decision:** Previously logged as an open question ("Ball-pocketed tracking ... unscoped").
Developer confirmed: pocketed balls should render **greyed out** in place (not removed from
the layout), for both the 8-ball grouped display and the numeric 9/10-ball row. This is
independent of 8-ball's solids/stripes group assignment — group (which player a ball is shown
under) and pocketed (is it still on the table) are separate states. Added to CLAUDE.md's domain
model (`GameState.PocketedBalls`, `GameManager.PocketBall`/`UnpocketBall`/`ResetBalls`) and to
PROGRESS.md Phase 2.

## 2026-08-13 — Project scope reset: dropping the league-based design entirely

**Decision:** The developer asked to build a general-purpose pool scoreboard (OBS overlay +
Stream Deck control, offline-capable, control style similar to overlays.uno's Billiards
Scoreboard) with an operator-set Race-To (single number or per-player split), operator-assigned
8-ball solids/stripes groups, a current-shooter indicator, a customizable color scheme, and a
separately placeable cue-ball spin-dot overlay. None of that needs a league/skill-level system,
so the existing APA/USAPL/BCA/TAP + SkillLevel/FargoRating design (`League.cs`, `RaceRules.cs`,
and the league-aware logic in `GameViewModel`/`PlayerViewModel`) is being replaced outright, not
extended. See CLAUDE.md and PROGRESS.md (Phase 0) for the target model and rework plan.

**Why keep this entry instead of deleting the old ones below:** NOTES.md is append-only by
convention — old entries stay as a record of what was tried, even when the direction changes.

## 2026-08-13 — Docs were stale relative to the actual code; found a broken uncommitted edit

**Issue:** PROGRESS.md and this file described the project as stuck at "v0.0 scaffolding only,"
but `git log` showed 6 further commits already landed beyond that point (manual Race-To entry
replacing the auto-calculated version, a working MVVM Controller UI with `GameViewModel`/
`PlayerViewModel`, league-specific race-to matrices for APA/TAP/USAPL/BCA). The docs were never
updated alongside that work.

Separately, `PoolScoreboard.Overlay/PoolScoreboard.Overlay.csproj` had an uncommitted, broken
edit: the SDK was changed to `Microsoft.NET.Sdk.Web` but the `FrameworkReference` for
`Microsoft.AspNetCore.App` was deleted and replaced with a `<ProjectReference ...>` element
missing its closing `/>` and quote — the project would not build as edited.

**Resolution:** Since the whole league-based direction is being replaced (see the entry above),
this is being handled as part of the Phase 0 rework rather than patched in place — the Overlay
project's structure is being redesigned anyway (hosted in-process by the Controller instead of
as a standalone exe). Flagging here so the broken edit isn't mistaken for intentional prior work.

## 2026-07-12 — RaceRules pattern matching: invalid `and` syntax with tuple relational patterns

**Issue:** The initial `RaceRules.GetRaceToValue` method used C# tuple pattern matching with attempts to combine relational patterns like `(League.APA, GameType.NineBall, >= 4) and (< 7)` - syntax that looked plausible but was invalid. The `and` combinator in pattern matching cannot be used to chain separate relational conditions on the same value; instead, the compiler tries to interpret `(< 7)` as a separate pattern being `and`'d to the tuple, which is a type mismatch. Result: 24 compile errors about "Cannot implicitly convert type 'int' to '(League, GameType, int)'" and "Relational patterns may not be used for a value of type '(League, GameType, int)'".

**Fix:** Rewrote the switch arms to use the correct pattern-matching order. In a tuple switch where the third element is an `int` with relational patterns, patterns are matched in descending priority — the first matching arm wins. Reordered each league's rules so higher thresholds come first (e.g. `>= 7` before `>= 4`), then use explicit `_` wildcards for the catch-all cases lower down. Example: `(League.APA, GameType.NineBall, >= 7) => 9, (League.APA, GameType.NineBall, >= 4) => 7, (League.APA, GameType.NineBall, _) => 5`. The `>= 4` arm never matches if `>= 7` already matched, so it implicitly covers the 4-6 range. This is standard tuple pattern matching fallthrough behavior and avoids the invalid `and` syntax entirely.

**Reference:** C# pattern matching is left-to-right through a switch arm list; each arm's pattern is tested until one matches. Relational operators (`>= < <=`) work fine in tuple patterns directly, but combining multiple conditions on the same value requires ordered fallthrough, not `and` combinator syntax.

<!--
Entry format:

## YYYY-MM-DD — Short title of the issue

**Issue:** What went wrong / what was discovered.

**Fix:** What was changed to resolve it.

-->
