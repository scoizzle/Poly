# gpure suite review — feedback

**Date:** 2026-08-07  
**Scope reviewed:** `docs/plans/grammar-pure-end-state.md`, `gpure-README.md`, `gpure-0`…`gpure-8`, `gpure-gate.md`, plus the engine (`Poly/Grammar/*`) and product parser (`Poly/DomainModeling/Parsing/*`) they target.

**Verdict:** Direction and sequencing are sound (engine → product tables → wire → delete). Locks G1–G7 are the right guardrails. Two design gaps should be closed **before** gpure-1 starts, plus a batch of concrete fixes. No blocking flaw in the end-state itself.

---

## Blocking-ish: close before starting

### B1. Nested-span IR problem (gpure-5 `if` bodies, gpure-3 groups)

`MatchRule(rule)` is **lossy for spans**: it Unreads the head, `TryMatch`s, then restores `_current` to the token **after** the whole match (see `PolyDslParser.MatchRule`, lines 1227–1235). IR construction needs the tree, but a flat `MatchResult` token list is all that survives.

The suite leaves this open:
- gpure-5 step 2 suggests `Many("effect")` / `Balanced(LBrace, RBrace)` for `if` bodies and also allows "loop `MatchRule("effect")` until fail".
- If an `if` pattern consumes the **whole** block via `Balanced`, the handler has no cursor left at the body start — it would have to re-parse from captured tokens.
- If patterns match only the **statement head** (e.g. `If LParen Rule("expr") RParen LBrace`), the body is consumed by a nested `MatchRule("effect")` loop and everything works with the existing dual-cursor API.

**Decision needed:** effect patterns match **statement head + condition only**; bodies are consumed by table-selected `MatchRule("effect")` loops. Add this to gpure-5 step 1 explicitly. Same rule for `expr` group patterns (head + `Rule("expr")` + close is fine since `expr` is a single matched span the handler re-enters via a layer call — but that only works under gpure-4 Option A, not Option B single-`MatchRule`-then-fold).

### B2. Parity harness — "1–2 dual-run goldens" is too thin for a precedence rewrite

gpure-4 step 6 (full suite is the bar, add 1–2 goldens) is under-scoped for replacing the expression precedence layers. There is repo precedent for exactly this: `VmParityTests` (VM as canonical semantics).

**Recommendation:** add a dedicated parity suite (e.g. `Poly.Tests/Grammar/ExprParityTests.cs` or DomainModeling side) that runs a fixed probe list plus the existing DSL corpus through **old RD vs new Matcher path** and asserts identical `DomainExpression` IR. It lives from gpure-3 through gpure-7 and is deleted only after gpure-7. This is the strongest G4 guard the suite can have.

### B3. `not` precedence quirk must be pinned, not just "mirrored"

Current product `ParseNot` parses its operand as `ParseAdd()` — not `ParseComparison` (see `DslExpressionParser.ParseNot`). So `not a > b` today is a **parse error** (unconsumed `> b`), and `not a + b` binds as `not (a + b)`. The suite says "mirror product… Document any intentional difference (there should be none)" — but the layer table in gpure-3 (§ expr-not → "not, comparison") is ambiguous about the operand level.

**Fix:** gpure-3 must spell out the operand rule for `not` (`expr-add`, matching today) and add a parity probe for it. Otherwise the rewrite silently changes precedence and semantic tests catch it late.

---

## Concrete fixes

| # | Where | Issue | Fix |
|---|-------|-------|-----|
| F1 | gpure-gate step 1 | Grep `rg "RuleRef\|LeftAssoc\|class Rule"` — no type named `class Rule` exists (task 1 proposes element `RuleRef` with fluent `PatternBuilder.Rule`). The grep would **pass vacuously** (or fail wrongly). | Align naming in task 1 (element `RuleRef<TKind>`, builder method `Rule(name)`, README table `Rule(ruleName)`); gate greps `RuleRef`. |
| F2 | gpure-4 step 4 vs gpure-7 | Contradiction: gpure-4 says "Prefer delete old ParseOr/ParseAdd bodies in same PR once green"; gpure-7 exists to delete the residual. | gpure-4 = wire + parity only (old layers stay until 7). Remove the "delete in same PR" wording. |
| F3 | gpure-0 step 2 | Grep undercounts the RD surface: misses `ParsePrimary`, `ParseComparison`, `ParseMultiply`, `ParseNot`, `ParseConditionalEffect`, `ParseCreateEffect`, `ParsePropertyInitializers`. Inventory A will under-report. | Extend the grep list. |
| F4 | gpure-0 §C | Engine semantics the later tasks depend on aren't documented: **`ManyOf` uses first-match (break after first), while `TryMatch` uses longest-match** (Matcher lines 35–47 vs 161–186). `RuleRef` in gpure-1 must reuse longest-match, **not** the `ManyOf` loop. `Optional` succeeds with zero width — `Many(Rule("empty"))` termination depends on `ManyOf`'s `subTokens.Count > 0` guard. | Record both in inventory notes as engine facts; add `Many(RuleRef(empty))` no-hang regression in gpure-1. |
| F5 | gpure-2 | `LeftAssoc` result shape unspecified: flat token list forces re-split for folding. | Specify: `MatchResult` keeps flat tokens (op identity recoverable from kinds); gpure-4 must use **Option A** (recursive layer `MatchRule` calls) — document Option B's span-loss problem (B1). |
| F6 | gpure-4/5 | No explicit fail-closed negatives in wire tasks (pre-ship gate theme). gpure-2 has `LeftAssoc_TrailingOp_Fails` — good model. | Add 2–3 per wire task: `assign x to <missing expr>`, `if (x) { unterminated`, `invoke any Action` (existing fail-closed local check must survive the rewrite). |
| F7 | gpure-5 step 1 | `when` appears in effect bodies as an error ("Unexpected 'when' inside action body") — if `effect` gains a `when` pattern the rejection must stay. `invoke any/all/where` and subscription quantifiers are identifier-*text* matched; the `effect`/`when` patterns need `MatchPredicate` on Identifier text, not new token kinds. | Call both out in gpure-5. |
| F8 | gpure-3 step 1 | Extending `expr-primary` group pattern changes `ExpectedTokens("expr-primary")` (used for error messages) — error-shape drift. | Note in gpure-3; gpure-8 step 6 covers the guide only "if error messages changed" — make this explicit so it lands. |
| F9 | gpure-8 | Printer parity is a deferred non-goal but "eventually print" is in the parent §1/§3. Round-trip tests still run on the domain-walk printer — that's consistent, but CORE/README should say printer table-parity is deferred, else "pure Grammar product path" overclaims. | One sentence in gpure-8 step 1. |
| F10 | Perf note | `MatchRule` re-scans from head per call (Unread+TryMatch+Read). Layered Option A is O(n²) worst case on long chains. | Fine for DSL authoring — say so in gpure-4 so no one adds an optimization prematurely. |

---

## Strengths worth keeping

- **Engine-first ordering** (1–2 before product 3–5) with tests-in-`Poly.Tests/Grammar` proves capability before porting — matches repo "smaller tested loop" principle.
- **Dual-path** (gpure-3/4 keep RD alive until parity) — correct for a precedence rewrite.
- **Lock G6** (no temporal product features; p1 separate, bridge OK) keeps scope tight; gpure-6's "document how temporal p1 should register" is the right bridge handoff.
- **Fail-closed examples** already present in gpure-2 (trailing op) and gpure-7 greps give gate teeth.
- Named-rule indirection (`Rule("expr")`) already has the right precedent in `ManyOf(ruleName)` — the engine grew this shape once; `RuleRef` is the natural completion.

## Suggested admission note

With B1–B3 resolved in the task docs (small edits to gpure-3/4/5 + gate grep fix), the suite is ready to admit as sole CURRENT per READY-TO-TASK ordering (gpure → mcp-minify → mut-safety → p1).

---

## Incorporation status (2026-08-07)

| ID | Status | Where |
|----|--------|-------|
| B1 | Done | gpure-5 B1 lock; gpure-0 §C nested-span; README review locks |
| B2 | Done | gpure-3 `DslExprParityTests`; gpure-4 ≥15 IR equality; gpure-7 oracle conversion; gate grep |
| B3 | Done | gpure-0 §E; gpure-3 pin + probe; README |
| F1 | Done | gpure-1 RuleRef naming; gpure-gate greps `RuleRef` not `class Rule` |
| F2 | Done | gpure-4 F2 lock (dual only); gpure-7 sole delete |
| F3 | Done | gpure-0 step 2 grep extended |
| F4 | Done | gpure-0 §C facts; gpure-1 longest-match + zero-width + longest-match test |
| F5 | Done | gpure-2 flat MatchResult + Option A; gpure-4 Option A required |
| F6 | Done | gpure-4/5 fail-closed negative tables |
| F7 | Done | gpure-5 F7 when + text predicates |
| F8 | Done | gpure-3 ExpectedTokens note; gpure-8 step 6 explicit |
| F9 | Done | gpure-8 F9 printer deferred sentence |
| F10 | Done | gpure-4 F10 perf note |
