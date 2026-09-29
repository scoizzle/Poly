# PR #82 round 6 follow-ups (head d7c1da48): verdict SHIP

No blockers. Everything below is pre-existing (identical on master), outside the PR's comparison contract, and is a follow-up queue in suggested order. No new findings this round; F49 is unused.

## Follow-up queue
1. **F46** (`Poly/DomainModeling/Lowering/DomainExpressionLoweringPass.cs:180-182`): a boolean or value hop outside a comparison at its own scope is unguarded (NullForgiving).
   - Shapes:
     - policy root `advisor Active` and `if (advisor Active)`
     - `Ready is (advisor where Age < 100 and mentor Active)` with mentor unlinked
     - `Ready is (any items where product Active)` with an item's product unlinked
     - `assign Score to advisor Age` and `create Note { Val: advisor Age }` (value contexts)
   - Fix: lower a boolean-leaf navigation as predicate-shaped (`rel != null && rel.X`). That closes the root, `where`-body and quantifier-body cases together. Value contexts need a spec decision (fail vs default).
   - Highest value of the queue, because it is the remaining source of NRE/throw at head.
2. **F47** (`Poly/DomainModeling/Lowering/DomainToCSharpExporter.Actions.cs:908-932`): `WalkPathPrefixRequireGuards` recurses into quantifier bodies with the outer entity as subject.
   - Repro: `Q: policy { any items where Qty < product Stock }` with `Go: action require Q {…}`.
   - With no `product` on Order the print fails to compile (CS1061) and simulate rejects it. With an unrelated `product` on Order the action is refused ("requires a linked 'product'") although every item's product is linked.
   - Fix: skip quantifier bodies or re-root them on the item.
3. **F42** (`DomainExpressionLoweringPass.cs:158-164`, `:452`): a binder name is matched at any depth, so `advisor order Total` reads the binder. Documented as a known issue in the `CollectValueHops` comment. Fix: match a binder only at the root hop.
4. **F44** (`DomainExpressionLoweringPass.cs:258-259`, `:174-177`): `IsCollectionNav` uses the outer entity's scope at nested hops, so a hop named like an outer `many` relation throws ("requires exactly one linked target"). Loud, never silent. Fix: resolve collection-ness against the hop's target entity.
5. **F7** (`Poly/DomainModeling/Language/DslExpressionParser.cs:190-199`, `FoldPathCmp`): a leading-path comparison scopes its right-hand side under the path. `advisor mentor Age > Age` self-compares silently. `advisor Age - advisor mentor Age < Age` fails loudly. Parenthesizing the path avoids both. Fix: scope only the leading path.
6. **F41** (`StructuralDomainAnalyzer.cs:69-74`): only enum names are checked for the `{Entity}Stage` collision; an entity or value type named `{Entity}Stage` still gives CS0101. Fix: check every type name.
7. **F4 remainder** (`Poly.Tests/.../EnumStageNameCollisionTests.cs`): the tests are reject-only. Add allow-cases for no-stages and suffix names, and an unknown-enum-member test.
8. **F40** (`UnlinkedComparisonAgreeTests.cs:204-205`): accepted nit. The test counts `this.Advisor != null` on the signature line and fails loud if the policy stops being one line.

## Closed this round
- **F48**: `CollectHopChain` now walks every navigation target on that hop (`:461`). The test at `UnlinkedComparisonAgreeTests.cs:258-314` fails 2 of 6 on 1312e586 and passes on head.

## Optional test hardening (not required)
- The both-linked rows in the new test use advisor 25, mentor 30 and customer 20. The result is the same whichever entity's `Age` is read, so those rows can't detect a self-Age misread.
- Values like self 50, advisor 10, mentor 20 would make them discriminating (`Age < (advisor where Age + mentor Age)` is False, but would be True if it read self's Age). This round's probe used those values, and lowering, simulate and print all read the advisor's Age.
