# gpure suite local review — 2026-08-07

- **Target**: local (uncommitted changes: gpure suite — Grammar engine + product DSL parser; `/tmp/gpure-review-full.txt`, 2075 lines)
- **Mode**: multi (Pass A full protocol + Pass B fresh diff-only context; merged below)
- **Issue counts**: 0 bugs, 5 suggestions, 4 nits
- **Verdict**: ship only with suggestions closed (no 🔴 bugs on valid product inputs; material follow-ups S1, S2, S3, S5)
- **Process notes**: this is the second review of the same suite in one day (pr1 gate + phenomenal review). The suite's own review-lock table (B1–B3, F1–F10) caught the pre-implementation design risks; this review found the residual *enforcement-vs-documentation* gaps the locks did not cover (S1 comment mismatch, S5 parent §8 overclaim).

## Summary

The gpure suite ports product expression folding and effect statement selection to Grammar-table-guided paths and adds `RuleRef<TKind>` / `LeftAssoc<TKind>` engine elements with longest-match + zero-width guards. Correctness posture is strong: engine offset arithmetic is consistent with peek-based matching, recursion terminates (zero-width guards), every effect head pattern's token-index consumption matches its handler (transition=2, assign=1, create-in=2, create=1), and the live fold preserves product semantics (suite 1928 green, re-run by reviewer; fail-closed tests construct real illegal states; `when` stays rejected; `else if` chains covered by pre-existing round-trip tests). Dominant residual risk is the **fold-vs-span split**: the live path and the `expr` span table disagree on valid product inputs (`a + not b`, `a + not b > c`) and no test pins either side; plus a docs overclaim ("handlers only map matches → IR") and two duplicated-op-set hazards. Oracle strength: the frozen-IR corpus is self-referential post-hoc (generated from the Grammar fold after the Rd dual was deleted), but the gpure-4 dual-run verified fold-vs-Rd equality while both existed and the pre-existing product suite (round-trips, MCP apply_dsl, VM eval) exercises the live path independently.

## Issues

### Issue 1 -- Severity: suggestion (found by Pass A + B)
- File: `Poly/DomainModeling/Parsing/DslGrammar.cs:167` (also `DslExprParityTests.cs:213`)
- Description: The span-table `expr` rule rejects valid product expressions the live fold accepts: `a + not b`, `a + not b > c`, `(a + not b)`. Fold accepts via `ParsePrimary`'s `case TokenKind.Not: return ParseNot()` re-entry (`DslExpressionParser.cs:154`); the table rejects because `expr-compare`'s LHS chain (`expr-add-no-not` → `expr-primary-no-not`) forbids `not` anywhere in the chain, not only at its start. The DslGrammar comment "Comparison LHS cannot start with `not`…" justifies only the *start* restriction, but enforcement is *anywhere-in-LHS* — comment/implementation mismatch. Honestly documented in `gpure-inventory-notes.md` §A1, but **no test pins either side**: `a + not b` appears only as a fold-side frozen IR oracle; `a + not b > c` appears in no test. A future agent wiring the live path through `TryMatch("expr")` or "fixing" `expr-primary-no-not` flips the product surface silently.
- Suggestion: Pin the span-side rejection (`GrammarMatch("a + not b").Accepts == false`, `a + not b > c` same) next to the fold-side oracle, and narrow the DslGrammar comment to "the comparison LHS chain is no-not (product `not` re-enters only via primary-Not/group)"; reconcile the table with the fold when the span tables gain a consumer.
- Status: open

### Issue 2 -- Severity: suggestion (found by Pass A)
- File: `Poly/DomainModeling/Parsing/DslExpressionParser.cs:96-121`
- Description: Operator identity in `ParseAdd`/`ParseMultiply` is read from `op.PatternName` (`"plus"`/`"minus"`/`"star"`/`"slash"`), while `ParseComparison` reads `opMatch.Tokens[0].Kind`. Two different identity mechanisms for the same "which operator did I match" question. Renaming a pattern (or reordering `expr-add-op`) silently swaps `Add`↔`Subtract` / `Multiply`↔`Divide` in the IR with no error — same-shape-different-meaning hazard.
- Suggestion: Read the operator kind from `op.Tokens[0].Kind` in `ParseAdd`/`ParseMultiply` (as `ParseComparison` does), making identity name-independent.
- Status: open

### Issue 3 -- Severity: suggestion (found by Pass A + B)
- File: `Poly/DomainModeling/Parsing/DslExpressionParser.cs:279` vs `Poly/DomainModeling/Parsing/DslGrammar.cs:26`
- Description: The comparison-operator set is implemented twice: `DslExpressionParser.IsComparisonOp` (private, used by `ParseRelatedAccess`/path-prefix) and `DslGrammar.IsCompareOpKind` (public, used by the span table and `expr-compare-op`). Textually identical today; no test forces agreement. A future change to one silently diverges the fold path (path-prefix comparisons) from the span table (op recognition) — sibling-path drift with no forcing test.
- Suggestion: Have `IsComparisonOp` delegate to `DslGrammar.IsCompareOpKind`, or add a test asserting identical results over all `DslTokenKind` values.
- Status: open

### Issue 4 -- Severity: suggestion (found by Pass B)
- File: `Poly.Tests/Grammar/DslExpressionE1Tests.cs:83-98` and `Poly/DomainModeling/Parsing/DslExpressionParser.cs:124`
- Description: `PackPattern_NumberUnit_ExtendsPrimarySurface` proves only that a pack-registered pattern extends the **Matcher probe** surface. The live product parser never consults grammar primary patterns — `ParsePrimary` calls `_forms.TryParsePrimary` (handler-based `IExpressionPrimaryForm`); the old `MatchRule("expr-primary")` probe was deleted (verified: `TryMatch("expr-primary")` has no product callers). The test comment ("packs must register on both primary rules" / "the hook the registry already wires") reads as if pattern registration alone extends product parse — a pack author following it gets a working Matcher probe but a non-working product parse of `12 days`. Inventory §Open forms are honest (p1 needs both `RegisterExpressionForm` and `ContributeGrammarPatterns`); the test comment is the misleading part.
- Suggestion: Extend the test to prove the product path (parse `P: policy { 12 days == 12 days }` with a registered duration `IExpressionPrimaryForm`), or amend the comment to state pattern registration covers the Matcher/probe surface and the handler form covers the live path.
- Status: open

### Issue 5 -- Severity: suggestion (found by Pass B)
- File: `docs/plans/grammar-pure-end-state.md:109` (parent §8) and `Poly/DomainModeling/Parsing/DslExpressionParser.cs:34-117`
- Description: Parent §8 "No recursive-descent **language** left … (handlers only map matches → IR)" overclaims for expressions. The live expression path is still a 6-method precedence ladder (`ParseOr`→`ParseAnd`→`ParseNot`→`ParseComparison`→`ParseAdd`→`ParseMultiply`) whose operator loops consult single-token `expr-*-op` rules; precedence, associativity, and operand layers are hard-coded in the ladder. The `expr`/`expr-or`/…/`expr-add-no-not`/`expr-mul-no-not` LeftAssoc tree that models the same language is **not used by the live path** (verified: only the parity suite's `GrammarMatch` calls `TryMatch("expr")`). "New syntax = pattern registration" is true only for ops, not for precedence structure — adding an operator requires editing both table and ladder. Internally consistent with gpure-4's Option A, but the §8 checkbox claims more than is delivered.
- Suggestion: Reword parent §8 (and the gpure-README DONE claim) to "operators/effects are table-selected; the precedence ladder is table-guided (Option A) until the LeftAssoc table becomes the live fold driver", or file a follow-up to drive the live fold from the LeftAssoc rules.
- Status: open

### Issue 6 -- Severity: nit (found by Pass A + B)
- File: `Poly/Grammar/Matcher.cs:213-238` (`LeftAssoc` 10_000 cap)
- Description: At 10_000 iterations `LeftAssoc` silently breaks and reports a truncated chain as success (fail-open). Reachability: requires a >10,000-operator chain on the span path (the live fold never runs `LeftAssoc`), so unreachable on realistic product input; matches the pre-existing `ManyOf` cap convention. Not a product bug; a documented fail-open edge in the new element.
- Suggestion: After the cap, fail the element unless the next peek is end-of-file / a non-operator; or add a comment noting the cap's fail-open behavior if left as-is.
- Status: open

### Issue 7 -- Severity: nit (found by Pass A + B)
- File: `Poly.Tests/DomainModeling/Parsing/DslExprParityTests.cs:92`
- Description: `Canonical()`'s `_ => $"Unknown({e.GetType().Name})"` fallback would vacuously pass a future oracle for an unmapped subtype ("Unknown(X)" equals itself). No current oracle produces it (all parse-reachable subtypes are mapped — verified), so nothing passes for the wrong reason today; the shape is the standard serializer-oracle vacuous-pass trap.
- Suggestion: Make the fallback throw (fail the test) or comment it as "must never appear in an oracle".
- Status: open

### Issue 8 -- Severity: nit (found by Pass B)
- File: `Poly/DomainModeling/Parsing/PolyDslParser.cs:1222` / `Matcher.cs:119-126`
- Description: `GetPatterns` returns an empty list for unknown rule names, so `MatchRule("bogus-rule")` returns null instead of failing at the typo. Live path: a null op match ends the loop; leftover tokens still fail at the enclosing `Expect`, so no silent acceptance — the failure just surfaces downstream with a confusing message. Reachability: only a typo'd rule name (pack author or future edit) hits it.
- Suggestion: Document the null-on-unknown contract on `MatchRule`, or throw on unknown rule names (fail-loud at the source).
- Status: open

### Issue 9 -- Severity: nit (found by Pass B + A)
- File: `Poly/DomainModeling/Parsing/PolyDslParser.cs:631-650`
- Description: Head-pattern dispatch changed several effect tail-shape errors to the generic "Expected effect (transition, assign, create, delete, invoke, if), got 'X'": e.g. `transition` alone (old "Expected To"), `assign x` without `to`, `if x` without `(`. Rejections are all preserved (verified per kind; three-layer defense still rejects); the guide asserts none of the old strings (verified by grep); the gate documents the drift. New messages are less specific for tail-shape errors.
- Suggestion: Acceptable as documented; consider folding tail expectations into the new text ("…got 'transition' (expected 'to <stage>')") or a follow-up to restore specificity once heads are table-driven.
- Status: open

## Verified-consistent (no issue)

- Test-count deltas 1901/1905/1915/1923/1927/1928 match actual `[Test]` counts (4 LeftAssoc, 5 RuleRef, 10+28+5 parity, 4 effect, +1 E1 pack); suite re-run by reviewer: 1928 passed / 0 failed.
- Gate greps pass: no RD `while (Kind == Plus/Minus/Star/Slash)` (exit 1); effect entry `MatchRule("effect")` at `PolyDslParser.cs:631`; `RuleRef`/`LeftAssoc` present; parity suite present.
- `else if` chains covered by pre-existing `PolyDslRoundTripTests` / `McpSmokeTests`; B3 pin (`not a > b` rejects both paths; `x > not y`, `(not a) > b` accept both); `when` stays rejected; effect head patterns consume exactly `match.Consumed`; zero-width guards prevent recursion.
