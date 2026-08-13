# Round-5 discovery fixes — follow-ups — 2026-08-12

Companion to `docs/agent/reviews/2026-08-12-round5-discovery-review.md`.
Each open bug/suggestion becomes a checkable task. Verify against current source; do not chain-trust the review.

## Open items (from the review)

- [x] **F1** — `ExpressionTypeAnalyzer.CheckCompatible` (line 355): reject a bare `PropertyAccess` naming a **non-member** of an enum-typed target on assign and create/create-in paths (move `CS1061` from compile-time to analyze-time). Repro: `probes/review-check/enum-bare.poly`, `probes/review-check/enum-assign-bare.poly`. Add a regression test for both forms.
- [x] **F2** — `CheckDefault` (line ~459): reject `default(now)`/`default(today)`/`default(utcnow)` on non-Date targets at analysis (mirror the `guid` check). Current behavior: late-rung codegen failure. Add a regression test for `Number default(now)`.
- [x] **F3** — `CheckInvokeArgumentTypes` / `InferBinderRootType` (line ~152): extend binder-root arg type inference to arithmetic over binder-root properties (e.g. `amount: line Qty + 1`); non-scalar binder roots rejected at analysis. Add a regression test.
- [x] **F4** — `DomainToCSharpExporter` F10 `All` gate (line ~491): guard the gate on the target having a stage enum; reject `when all Rel Stage` on a stageless target at analysis, or emit `linkedTarget.CurrentStage` only when the target has stages. Repro: `probes/review-check/sub-invoke.poly` (currently CS0111 + CS1061).
- [x] **F5** — runtime coverage: add a test that `assign DateProp to now` stores a `DateOnly` (not a DateTime) on the runtime `DomainEntityInstance` path (currently the assign-RHS adaptation is export-only; the runtime path relies on `EvaluateDefaultValue`, untested for assign).
- [x] **F6** — `CheckDefault` rung: reject `default(now)` on `Number`/`Text` at analysis (currently fails at codegen). This overlaps F2; implement both in one pass.
- [x] **F7** — `CheckCreateInitializers` / `ResolveEntityProps` (line ~437): use `DomainTypeLookupMetadata` (catalog) for target resolution instead of a linear `domain.Types.OfType` scan; add a non-catalog test.
- [x] **F8** — F10 gate target stage enum name: resolve via the same `stageEnumTypeName` metadata as the subscriber (currently falls back to the default convention); add a test with a customized stage enum name.

## Process follow-ups

- [x] **P1** — The F4 fix addressed only the string-literal `create-in` case; the bare-identifier sibling paths were not covered by the new regression test (`ExpressionTypeAnalysisTests.CreateIn_NonMemberEnumLiteral_Rejected` only tests the string form). When fixing an enum-membership gap, enumerate **all** authoring forms (string literal, bare identifier, assign, create-in, default) and add a test for each. Add this to the discovery-loop protocol's "slice" checklist.
- [x] **P2** — The `default(now)`-on-Time family is a parse-level dead-end (unreachable), but the codegen failure for `Number default(now)` is a real rung gap. The discovery loop's "re-sweep" only checks the compile gate (0/0); it does not check **late-rung failures** (codegen exceptions) or **sibling forms** of the same semantic. Consider adding a "sibling-form" sweep to `scripts/discovery-round.sh` or the protocol's slice checklist.

## Dispositions

- **Fixed** — F6 (VM heap comparison), F1–F3 (keyword default adaptation), F4 (string-literal create-in enum membership), F7 (invoke arg typing for scalar args), F8 (entity-returning invoke rejection), F10 (when-all export gate), F5 (combined export header), F9 (reverse-side for diagnostic), F11 (guide docs).
- **Still open** — the 8 items above (F1–F8 of this file) are residuals found by the review, not regressions of the round-5 fixes themselves.
- **Invalid** — none.
