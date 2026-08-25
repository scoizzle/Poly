# gpure follow-ups — 2026-08-07

Source: [`gpure-local-review-2026-08-07.md`](./gpure-local-review-2026-08-07.md) (phenomenal review, mode multi, Pass A + Pass B merged).  
Suite: gpure (DONE 2026-08-07). These are post-gate follow-ups — do **not** reopen the suite gate for them; they are new tasks for the next agent.

## Open items

- [x] **S1** — Pin the fold-vs-span `not`-in-chain divergence.
  - File: `Poly.Tests/DomainModeling/Parsing/DslExprParityTests.cs` (+ comment in `Poly/DomainModeling/Parsing/DslGrammar.cs:167`).
  - Do: add `GrammarMatch("a + not b").Accepts == false` and `GrammarMatch("a + not b > c").Accepts == false` next to the fold-side IR oracles (`Add(Prop(a),Not(Prop(b)))`); narrow the DslGrammar comment from "cannot start with `not`" to "the comparison LHS chain is no-not (product `not` re-enters only via primary-Not/group)". Add a tracking note to reconcile the span table with the fold when the span tables gain a live consumer (printer/validator).
  - Done (2026-08-08): `SpanVsFold_NotInChain_TableRejectsFoldAccepts` pins both span rejects + both fold IR oracles; `DslGrammar.cs` comment narrowed to "no-not END TO END"; tracking note added to `gpure-inventory-notes.md` §A1.
- [x] **S2** — Make operator identity name-independent.
  - File: `Poly/DomainModeling/Parsing/DslExpressionParser.cs:96-121`.
  - Do: in `ParseAdd`/`ParseMultiply`, read the operator kind from `op.Tokens[0].Kind` instead of `op.PatternName` (`"plus"`/`"minus"`/`"star"`/`"slash"`), matching `ParseComparison`. Keeps `Add`↔`Subtract` / `Multiply`↔`Divide` correct across pattern renames.
  - Done (2026-08-08): both loops now read `op.Tokens[0].Kind` (`TokenKind.Plus` / `TokenKind.Star`), matching `ParseComparison`.
- [x] **S3** — Single source of truth for the comparison-op set.
  - File: `Poly/DomainModeling/Parsing/DslExpressionParser.cs:279` (private `IsComparisonOp`) vs `Poly/DomainModeling/Parsing/DslGrammar.cs:26` (`IsCompareOpKind`).
  - Do: make `IsComparisonOp` delegate to `DslGrammar.IsCompareOpKind`, or add a test asserting identical results over all `DslTokenKind` values (sibling-path forcing test).
  - Done (2026-08-08): `IsComparisonOp` now delegates to `DslGrammar.IsCompareOpKind` — single source of truth, no sibling copy.
- [x] **S4** — Prove the pack extension on the live product path.
  - File: `Poly.Tests/Grammar/DslExpressionE1Tests.cs` (PackPattern test, lines 83–98).
  - Do: extend the test to parse `P: policy { 12 days == 12 days }` with a registered duration `IExpressionPrimaryForm` (live path), or amend the comment to state that pattern registration covers the Matcher/probe surface only and the handler form covers the live path.
  - Done (2026-08-08): test now proves BOTH — matcher/probe surface (patterns on both primaries) and the live path (`DurationLiteralForm` parses `P: policy { 12 days == 12 days }` end-to-end → `Cmp(Lit("12 days"), Equal, Lit("12 days"))`).
- [x] **S5** — Make parent plan §8 wording match the implementation.
  - File: `docs/plans/grammar-pure-end-state.md:109` (§8 checkboxes) and `docs/plans/simple-agent-tasks/gpure-README.md` DONE claim.
  - Do: reword "handlers only map matches → IR" to reflect that the expression **precedence ladder is table-guided (Option A)** and the LeftAssoc span tables are not yet the live fold driver; note this as a planned successor step (or file a task to drive the live fold from the LeftAssoc rules).
  - Done (2026-08-08): parent §8 first checkbox + README "End state" item 3 reworded to "construct dispatch table-selected; precedence ladder table-guided (Option A); LeftAssoc tables not yet the live fold driver — planned successor step". README status line references the follow-ups.
- [x] **N1** — LeftAssoc 10_000-iteration cap fail-open edge.
  - File: `Poly/Grammar/Matcher.cs:213-238`.
  - Do: after the cap loop, fail the element unless the next peek is end-of-file or a non-operator; or add a comment documenting the cap's fail-open behavior (matches ManyOf convention).
  - Done (2026-08-08): after the cap loop the element now fails when the next peek is still an operator kind (truncated match / trailing op both malformed); pinned by `GrammarLeftAssocTests.LeftAssoc_CapReached_TrailingOperator_FailsClosed` (20_002-token input).
- [x] **N2** — Canonical() fallback should fail loud.
  - File: `Poly.Tests/DomainModeling/Parsing/DslExprParityTests.cs:92`.
  - Do: make the `_ => Unknown(...)` fallback throw (or comment "must never appear in an oracle") so a future unmapped `DomainExpression` subtype fails the test instead of vacuously passing.
  - Done (2026-08-08): fallback now throws `InvalidOperationException` naming the unmapped subtype.
- [x] **N3** — Document or fail-loud on unknown rule names in `MatchRule`.
  - File: `Poly/DomainModeling/Parsing/PolyDslParser.cs:1222` (and `Matcher.GetPatterns`).
  - Do: document the null-on-unknown contract on `MatchRule`, or throw on unknown rule names so a pack-author typo fails at the source.
  - Done (2026-08-08): `Matcher.TryMatch` and `TryMatchRule` (RuleRef/LeftAssoc operands) now throw `ArgumentException` on unknown rule names — a typo fails at the source, never silently never-matches. `Grammar.GetPatterns` stays lenient + gains `HasRule` so `ManyOf`-on-undefined-rule keeps its documented zero-many edge (`GrammarEdgeCaseTests.ManyOf_RuleWithZeroPatterns`); `PolyDslParser.MatchRule` doc notes the throw.
- [x] **N4** — Restore effect tail-error specificity (optional).
  - File: `Poly/DomainModeling/Parsing/PolyDslParser.cs:631-650`.
  - Do: fold old tail expectations into the generic error ("…got 'transition' (expected 'to <stage>')") or track once heads are table-driven. Rejections are all preserved today; this is DX only.
  - Done (2026-08-08): head-keyword tail hints folded in — `transition` → "expected 'to <stage>'", `assign` → "expected '<property> to'", `create` → "expected '<type> { … }' or 'in <relationship> { … }'", `if` → "expected '(condition)'". Rejection surface unchanged.

## Process follow-ups

- [x] **P1** — Add a "span table vs live fold parity" class to the suite gate: when a grammar table models a language the live path also implements, every divergence class must be pinned on BOTH sides (accept and reject) with tests, not only documented in inventory notes.
  - Reason: S1 shows enforcement-vs-documentation drift survives an otherwise honest plan; the fix is a test that forces the sibling (span) path.
  - Done (2026-08-08): rule added to `gpure-gate.md` as "Post-gate follow-up — P1" with the S1 pins as the first instance and a checkable grep (`rg -n "SpanVsFold" Poly.Tests`).

## Disposition of prior review items

Prior review: `docs/plans/simple-agent-tasks/gpure-review-feedback.md` (Incorporation status table). All B1–B3 / F1–F10 items were verified **fixed** in the current tree (re-checked by reviewer, primary evidence):
- B1 head/body split — fixed (effect patterns are head-only; handlers consume + loop).
- B2 parity harness — fixed (DslExprParityTests exists, frozen-IR corpus, gate grep present).
- B3 `not` operand layer — fixed (expr-not = Not+expr-add; `not a > b` rejects both paths).
- F1 RuleRef naming + gate grep — fixed.
- F2 gpure-7 sole deletion — fixed.
- F3 inventory grep extended — fixed.
- F4 longest-match + zero-width facts recorded + RuleRef longest-match test — fixed.
- F5 LeftAssoc flat tokens + Option A — fixed.
- F6 fail-closed negatives — fixed.
- F7 `when` rejection + text predicates — fixed.
- F8 ExpectedTokens note — fixed (no live callers today; inventory records it).
- F9 printer deferral — fixed (CORE/README state it).
- F10 perf note — fixed (documented).
