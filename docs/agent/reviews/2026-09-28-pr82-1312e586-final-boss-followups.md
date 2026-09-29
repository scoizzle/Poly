# PR #82 round 5 follow-ups (head 1312e586)

## Blocking (this PR)
- **F48** (BROKE-PR-CONTRACT): `DomainExpressionLoweringPass.cs:445-459`.
  - `CollectHopChain` follows only a navigation target that is itself a navigation. With the advisor linked and the mentor unlinked, `Age < (advisor where mentor Age * 2)` and `Age < (advisor where Age + mentor Age)` throw in simulate and NRE in print. This is identical on 0e03bcf3 and on master.
  - Fix option (a): walk a non-RN target with the hop as subject, never `_currentSubject` (F36/F43).
  - Fix option (b): reject non-boolean `where` bodies in analysis.
  - Test: advisor linked, mentor unlinked → False on both surfaces.
- Summary comment `:422-424`: "a navigation to a condition … guards its own hop" should read "its outer hop".

## Non-blocking, pre-existing (identical on master)
- **F46**: boolean and value leaves outside a comparison at their own scope are unguarded (`DomainExpressionLoweringPass.cs:180-182`).
  - Affected shapes: policy root `advisor Active`, `if (advisor Active)`, `where`-body leaves (`Ready is (advisor where Age < 100 and mentor Active)`), quantifier-body leaves (`Ready is (any items where product Active)`), and `assign`/`create` value contexts.
  - Fix: predicate-shape a boolean-leaf RN (`rel != null && rel.X`). Value contexts need a spec decision.
- **F47** (new, pre-existing): `DomainToCSharpExporter.Actions.cs:908-932`. `WalkPathPrefixRequireGuards` recurses into quantifier bodies with the outer subject.
  - `require Q` with `Q = any items where Qty < product Stock` gives CS1061 or a VM reject.
  - With an unrelated `product` on Order, the action is refused ("requires a linked 'product'") even though every item's product is linked.
  - Fix: skip or re-root at quantifier bodies.
- **F42**: binder matched at any depth (`:158-164`, `:448`). It is documented as a known issue. Fix: match a binder only at the root hop.
- **F44**: `IsCollectionNav` is outer-scoped (`:258-259`), giving a loud throw. Fix: resolve against the hop's target entity.
- **F7**: `DslExpressionParser.cs:190-199` `FoldPathCmp` scopes the RHS under the leading path.
- **F41**: `StructuralDomainAnalyzer.cs:69-74` checks enums only.
- **F4 remainder**: allow-case tests are missing.
- **F40**: accepted nit.
