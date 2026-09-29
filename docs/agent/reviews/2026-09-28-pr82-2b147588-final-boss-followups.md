# PR #82 @ 2b147588: Final Boss follow-ups, 2026-09-28

Verdict: **NOT SHIP**, with 2 bugs (F36, F37). Review: `docs/agent/reviews/2026-09-28-pr82-2b147588-final-boss.md`. IDs: Razor F1–F8 carried; new findings from F36.

## Blocking

- [ ] **F36 (bug)**: `Poly/DomainModeling/Lowering/DomainExpressionLoweringPass.cs:432-433`. `CollectValueHops` walks quantifier bodies with the outer subject, so `(count items where Qty < product Stock) > 0` prints `this.Product != null && …`. The result is CS1061 plus a simulate VM compile throw, or a silent False when the outer entity has its own `product`. Master works. Fix: stop or re-root at quantifier, `OwnedAccess` and library nodes. Add a simulate-and-print agreement test using that DSL.
- [ ] **F37 (bug)**: `DomainExpressionLoweringPass.cs:421-428`. The peer binder `when orders Active as order { if (Balance < order Total) … }` prints `this.Order != null && …`, giving CS1061. Master works. Fix: consult `_parameters` the way `RelationshipNavigation` does (`:154-164`). Add a subscription agreement test.
- [ ] **Gate**: after the F36 and F37 fixes, reword the comment at `:400-405` to say which subject the walk guards and where it stops.

## Non-blocking

- [ ] **F40 (nit)**: `Poly.Tests/DomainModeling/Lowering/UnlinkedComparisonAgreeTests.cs:204-205`. Count guards in `ExportedCSharp.ExtractMethod(...)` rather than on one source line.
- [ ] **F41 (suggestion, pre-existing)**: `Poly/DomainModeling/Analysis/StructuralDomainAnalyzer.cs:69-74`. Reject any declared type (entity or value), not just an enum, named `{Entity}Stage`; today it gives CS0101.
- [ ] **F4 remainder**: allow-case tests (a no-stages `{Entity}Stage` enum, a `PreOrderStage` suffix) and an unknown-enum-member test.
- [ ] **F7 (parked)**: `Poly/DomainModeling/Language/DslExpressionParser.cs:190-199`, path-comparison RHS scoping. Flip `MentorOlder` when fixed.

## Closed at 2b147588

F1, F2, F3, F5, F6, F8. W4 (from PR 83) is fixed and pinned by `UnlinkedComparisonAgreeTests.cs:29` and `:82`.
