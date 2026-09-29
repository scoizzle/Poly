# PR #82 @ 0e03bcf3: Final Boss follow-ups, 2026-09-28

Verdict: **NOT SHIP**, 1 bug (F45). Review: `docs/agent/reviews/2026-09-28-pr82-0e03bcf3-final-boss.md`. Carried ids kept; new findings F45 and F46.

## Blocking

- [ ] **F45 (bug, regressed by 0e03bcf3)**: `Poly/DomainModeling/Lowering/DomainExpressionLoweringPass.cs:429-436`. `CollectValueHops` looks through arithmetic only. With the advisor unlinked, `Ready is (not advisor Active)`, `Ready is (advisor Active and Ready)` and `Ready is (advisor Active or Ready)` now throw in simulate and NRE in print; at 754d7e72 they were False/False. Fix: add `case Ontology.Not or Ontology.And or Ontology.Or:` to the look-through (or guard Boolean-leaf navigations at `:180-182`). Add an agreement test and update the summary at `:417-426`.

## Non-blocking

- [ ] **F46 (suggestion, pre-existing)**: `DomainExpressionLoweringPass.cs:180-182`. Boolean value hops outside comparisons are unguarded: `advisor Active` or `not advisor Active` as a policy root, and `advisor where Active and mentor Active` with the mentor unlinked, all throw or NRE. Same on master.
- [ ] **F42 (suggestion, pre-existing, parked)**: `:158-164`, `:445`. The binder name is matched at any depth, giving a silent wrong answer. Label the summary sentence at `:424-425` as a known issue.
- [ ] **F44 (suggestion, pre-existing)**: `:258-259`. `IsCollectionNav` is outer-scoped at nested hops, both in quantifier bodies and in `advisor mentor Age` chains; export throws loudly.
- [ ] **F41 (suggestion, pre-existing)**: `Poly/DomainModeling/Analysis/StructuralDomainAnalyzer.cs:69-74`.
- [ ] **F40 (nit, accepted)**: `Poly.Tests/DomainModeling/Lowering/UnlinkedComparisonAgreeTests.cs:204-205`.
- [ ] **F4 remainder**: allow-case and unknown-enum-member tests.
- [ ] **F7 (parked, broadened)**: `Poly/DomainModeling/Language/DslExpressionParser.cs:190-199`. Leading-path comparison folding: a silent self-compare, plus loud CS0019/CS1061/rejections for `path X - path Y < Z` forms.

## Closed at 0e03bcf3

F36, F37 and F43 (`ForeignRootedHopAgreeTests.cs:22`, `:71`, `:119`). F1–F3, F5, F6 and F8 are still closed (F8 mutations re-run and live).
