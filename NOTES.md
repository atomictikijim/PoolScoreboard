# Notes

Running log of issues discovered during development and the fixes used.
Newest entries at the top.

## 2026-08-14 — Fixed: "Choose Flag..." crashed the app on 10 specific flags

**Issue:** Clicking "Choose Flag..." crashed the Controller outright. The crash log showed
`System.IO.IOException: Cannot locate resource 'assets/flags/countries/bonaire%2c%20sint...'` —
thrown from `SvgViewbox` while the picker's `WrapPanel` measured/rendered every tile up front (no
UI virtualization), so a single bad entry anywhere in the 305-item catalog crashes the whole
window on open, not just the one a user happens to scroll to.

**Root cause:** `FlagCatalog.Pack()` built each pack URI via `Uri.EscapeDataString(fileName)`.
For plain-ASCII names this is harmless — spaces become `%20`, and confirmed (via a throwaway probe
reading the compiled assembly's `.g.resources` manifest directly) that WPF's resource build task
*also* stores `%20` for spaces in its resource keys, so those always matched. But
`Uri.EscapeDataString` also percent-encodes commas, parentheses, and apostrophes (`,` → `%2C`, etc.)
per strict RFC 3986 — and WPF's resource build task does **not** encode those characters the same
way when it generates its manifest keys, so the two sides disagreed and the resource lookup failed
for any name containing them. Diacritics (Curaçao, Côte d'Ivoire, Réunion, Saint Barthélemy,
Türkiye) were suspected of a similar mismatch and fixed pre-emptively even though only the comma
case was directly observed crashing.

**Fix:** Renamed the 10 affected files (on disk, under `Assets/Flags/Countries/`) to ASCII-only,
punctuation-free slugs — e.g. `Bonaire, Sint Eustatius and Saba.svg` → `Bonaire Sint Eustatius and
Saba.svg`, `Côte d'Ivoire.svg` → `Cote dIvoire.svg` — and regenerated `FlagCatalog.cs` so each
entry's `Label` (shown in the UI) keeps the original accented/punctuated name, while only the
on-disk filename/pack URI uses the safe slug. Verified with a throwaway console probe (not
committed) that reads the compiled assembly's `.g.resources` manifest directly and checks all 305
`FlagCatalog.All` entries resolve — confirmed 305/305 ok, not just the specific flags a manual
click would happen to reach.

## 2026-08-14 — Bundled end-cap icon set: sources, licensing, and rendering approach

**Decision:** Sourced country flags from
[lipis/flag-icons](https://github.com/lipis/flag-icons) (MIT licensed, 249 ISO-tagged
country/territory SVGs plus a `country.json` name mapping) and US state/territory flags from
[nibsbin/us-state-flags-svg](https://github.com/nibsbin/us-state-flags-svg) (56 SVGs; the repo
itself carries no explicit license file, but its README states the images were pulled from
Wikipedia's public-domain holdings for US state/territory flags on 2019-10-11 and explicitly
frames the repo as "a public resource for artists"). Flagging the second source's softer legal
basis transparently — proceeding since the developer named this exact repo directly and the
underlying artwork is public domain regardless of the GitHub repo's own (absent) license.

**Decision:** WPF has no native SVG renderer. Rather than pre-rasterizing ~305 files to PNG (a
fragile one-time conversion pipeline, and loses vector crispness), added the `SharpVectors.Reloaded`
NuGet package — a well-established, actively-maintained WPF SVG rendering library. Verified via a
throwaway console probe (not committed) that `SharpVectors.Converters.FileSvgReader.Read(stream)`
returns a `DrawingGroup` that wraps cleanly into a `DrawingImage` (a `System.Windows.Media.ImageSource`),
and separately that `SharpVectors.Converters.SvgViewbox.Source` accepts a `pack://` URI directly —
confirmed against several real downloaded flag files including Nepal's non-rectangular flag before
committing to this approach.

**Decision:** Flag SVGs are added as WPF `Resource` build items (physical files under
`PoolScoreboard.Controller/Assets/Flags/`, pack-URI addressable) rather than `EmbeddedResource`s —
`Resource` is the idiomatic WPF mechanism for bundling images referenced from XAML (`SvgViewbox.Source`),
whereas `EmbeddedResource` (already used for the Overlay's `scoreboard.html/css/js`, read via
`Assembly.GetManifestResourceStream`) suits text/manifest-style assets better and doesn't get a
convenient pack-URI XAML binding path.

**Decision:** `FlagCatalog.cs` (the `Label`/`Group`/`PackUri` list backing the picker) is a
generated, static, hardcoded list rather than something built by enumerating embedded resources at
runtime — `Resource`-build-action files compile into a single `.g.resources` container that's
awkward to enumerate dynamically with per-item names, whereas a flat static list is trivial to keep
alphabetically sorted per group and guarantees no runtime ordering surprises.

**Note:** flag-icons' `iso:true` set and us-state-flags-svg's territory set overlap on five entries
(American Samoa, Guam, Northern Mariana Islands, Puerto Rico, US Virgin Islands each have their own
ISO code AND are US territories) — these appear in both the STATES and COUNTRIES groups of the
picker. Not de-duplicated; harmless, and each group is clearly labeled.

## 2026-08-14 — Bundled end-cap icon set requested (country + US state flags)

**Ask:** In addition to the per-player "Choose Icon..." custom file picker (data-URI end-cap
icons, shipped earlier the same day — see below), the developer wants a built-in icon set shipped
with the app covering country flags and United States state flags, so operators don't need to
source their own flag artwork for the common case. Recorded as a pending item in PROGRESS.md
("Pending: Bundled end-cap icon set") rather than implemented immediately — needs a licensing
source for the artwork and a small picker-UI decision before it's buildable.

## 2026-08-14 — Configurable overlay styling + live show/hide: implementation decisions

**Decision:** `ScoreboardStyle` (corner roundness, overall scale, glossy/flat, end-cap style,
shooter-indicator style) is applied once at match setup, matching `ColorTheme`'s existing
lifecycle — not live-editable mid-match. `ScoreboardVisibility` (score bar/ball tracker/winner
banner show-hide) is the opposite: live-toggled from the running match via new buttons, matching
`CurrentShooter`/`PocketedBalls`'s lifecycle. Two different lifecycles because styling is a
"how does the whole broadcast look" setup choice, while visibility is an operational
during-the-show action (hide the ball tracker for a moment, bring back the winner banner, etc.).

**Decision:** `OverallScale` is implemented as a single CSS `transform: scale()` on the outermost
wrapper rather than converting every dimension to relative units. Trade-off accepted deliberately:
scaling above 100% can render outside the OBS browser-source's configured width/height and get
cropped — the fix is sizing the OBS source generously, not "fixing" the CSS. Flagged inline in
`scoreboard.css` so this isn't mistaken for a bug later.

**Decision:** The winner banner's pre-existing win-detection fade (`.winner-banner.visible`) was
retired and folded into the new generic `.sb-hideable`/`.sb-hidden` mechanism rather than layered
underneath it — keeping both would have had two `opacity`/`transform` transitions fighting over
the same element. Its visibility is now `winnerName present AND operator hasn't hidden it`,
computed once in `scoreboard.js`'s `render()`.

**Decision:** `scoreboard.js` tracks a `hasRenderedOnce` flag so the very first SSE message of any
connection snaps all three hideable elements to their correct state with no animation. This
matters because `ScoreboardEndpoints.cs` resends the full current snapshot as the first message on
*every* new SSE connection, including an OBS scene switch or browser-source reload mid-match — not
just a true first page load — so "just connected" and "reconnected with something already hidden"
are the same code path and both need to skip the slide+fade, not just the literal first-ever load.

**Decision:** the hide-transition's `transitionend` handler re-checks that the element is still
meant to be hidden (`el.classList.contains('sb-hidden')`) before setting `display:none`, guarding
against an operator rapidly toggling an element hidden-then-shown-again before the hide animation
finishes (which would otherwise let a stale listener re-hide an element that was already re-shown).

## 2026-08-14 — WNT visual reference: second screenshot adds shooter-indicator and ball-tracker cues

**Input:** Developer supplied a second WNT score-bar screenshot (players "Shane Van Boening" vs.
"Eklent Kaçi", "Race to 11", both with a ball still at 0). Unlike the first screenshot, this one
shows a current-shooter indicator and a ball-tracker graphic — the two elements the first
screenshot didn't cover.

**Extracted design tokens** (new, in addition to the ones already in CLAUDE.md's "Visual
Reference"):

- **Current-shooter indicator:** a small right-pointing triangle (▶) sitting immediately to the
  left of the shooting player's score, right next to the center Race-To segment — it points at
  whichever side is currently shooting, rather than glowing/highlighting that entire half of the
  bar.
- **Ball tracker:** a separate white capsule-shaped bar, horizontally centered *below* the main
  score bar (not embedded inside it), containing small colored circular ball icons — standard
  ball coloring with a white number centered on each.
- **End-cap badges are actual national flags** (rectangular, slightly rounded corners) on the
  black rounded end-caps — not a generic "team-sponsor mark" as the first screenshot's small,
  ambiguous badges suggested, and not circular.

**Discrepancy flagged, not resolved:** in this screenshot the ball tracker only shows balls still
on the table — pocketed balls are absent entirely, not greyed out. This conflicts with the
developer's earlier explicit decision (see "2026-08-13 — Pocketed-ball tracking confirmed in
scope" below) that pocketed balls render **greyed out in place**, not removed. That earlier
decision stands — it came from a direct instruction, and this screenshot is a style reference,
not a new instruction to remove balls. Noting this so the WNT reference isn't copied literally
on this one point.

**Decision:** Recorded as reference only, same as the first screenshot — not yet applied to code.
Earmarked for a `/overlay/scoreboard` revisit (shooter-indicator/ball-tracker currently use
reasonable defaults per Phase 3, see PROGRESS.md). Also noting an open question: the flag end-
caps aren't backed by any field in the current domain model (`Player` only has `Name`/
`TeamName`, no nationality) — no model change made; worth asking the developer whether flags
should become a real feature or stay a decorative placeholder if this styling is adopted.

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
