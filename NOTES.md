# Notes

Running log of issues discovered during development and the fixes used.
Newest entries at the top.

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
