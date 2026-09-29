# PR #82 @ 754d7e72: Final Boss follow-ups, 2026-09-28

Verdict: **NOT SHIP**, 1 bug (F43). Review: `docs/agent/reviews/2026-09-28-pr82-754d7e72-final-boss.md`. Carried ids kept; new findings F42–F44.

## Blocking

- [ ] **F43 (bug, regression)**: `Poly/DomainModeling/Lowering/DomainExpressionLoweringPass.cs:429-441`. A predicate-shaped navigation used as a comparison operand (`Ready is (advisor where Age < mentor Age)`) is walked with the outer subject, which adds `this.Mentor != null`. The result is CS1061 plus a simulate throw, or a silent False when Customer has an unrelated `mentor`. Master gives True/True. Fix: return early when `expr is RelationshipNavigation p && IsPathPrefixPredicate(p.TargetProperty)`, and add that shape to `ForeignRootedHopAgreeTests`.
- [ ] **Gate**: reword the comment at `:400-409` so it claims only what the walk stops at (quantifiers, predicate navs after the F43 fix, binder names matched at any depth).

## Non-blocking

- [ ] **F42 (suggestion, pre-existing)**: `DomainExpressionLoweringPass.cs:154-164` and `:427`. An inner hop named like the binder (`advisor order Total` with `as order`) resolves to the binder, so simulate and print both give a silent wrong answer. Match the binder only at the root, in the lowering, the walk and `SubscriptionAnalyzer`.
- [ ] **F44 (suggestion, pre-existing)**: `DomainExpressionLoweringPass.cs:258-259`. A quantifier-body hop's cardinality is checked against the outer entity; a same-named `many` relation on the outer entity makes export throw. Resolve against the hop's source entity.
- [ ] **F40 (nit, accepted)**: `Poly.Tests/DomainModeling/Lowering/UnlinkedComparisonAgreeTests.cs:204-205`. Optional: have `ExportedCSharp.ExtractMethod` fall back to `=> …;` and count guards in the extracted method.
- [ ] **F41 (suggestion, pre-existing)**: `Poly/DomainModeling/Analysis/StructuralDomainAnalyzer.cs:69-74`. Reject any declared type named `{Entity}Stage`, not just an enum.
- [ ] **F4 remainder**: allow-case tests (a no-stages `{Entity}Stage` enum, `PreOrderStage`) and an unknown-enum-member test.
- [ ] **F7 (parked)**: `Poly/DomainModeling/Language/DslExpressionParser.cs:190-199`.

## Closed at 754d7e72

F36 (`DomainExpressionLoweringPass.cs:425-426`, `ForeignRootedHopAgreeTests.cs:22`) and F37 (`:427-428`, `ForeignRootedHopAgreeTests.cs:71`). F1–F3, F5, F6 and F8 are still closed.
