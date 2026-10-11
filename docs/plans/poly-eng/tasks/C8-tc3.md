# TASK C8-tc3 - Typed create follow-ups: event create, self and singular navs, public factories, failed Value
Status: planned (written for the local-agent handoff, 2026-10-10). Branch `slice/c8-tc3` (not created yet). Lane B. Review 2.
Plan card: **C8-tc**; part 3 of 3. Depends on C8-tc2.

## Scope
Findings G4+D15, G6, G8, G9, G12, and the folded C6b2 fix, from [`research/2026-10-10-codegen-review.md`](../research/2026-10-10-codegen-review.md).
- G4 `Poly.Tests/DomainModeling/Compile/EmitGolden/mcp-library/Patron.cs.golden:86` + D15 `:84`: `when loans Overdue … create Fine` lowers to `this.CreateFines(...)` so the Fine lands on `Patron.Fines`; failure is a `DomainResult`, not `throw new ConstraintFailureException` (`EffectLoweringPass.cs:448-454`, `:1170`); drop the dead `created1` local.
- G6 `Poly.Tests/DomainModeling/Compile/EmitGolden/crm/Account.cs.golden:59`: self-relationship `CreateParent` passes null for the new account's parent and only sets `this.Parent` (`DomainToCSharpExporter.Notify.cs:129`, `IsBackReference`).
- C6b2 (folded; branch `slice/c6b2-plan` has its task file): a self-referencing collection slot gets `new List<T>()`, not `this` (`Node.Create(name, this, this)` is CS1503). Same back-reference code as G6, so it is done here. **Handoff placement choice**, not from the original split.
- G8 `Poly.Tests/DomainModeling/Compile/EmitGolden/orders/OrderItem.cs.golden:26`: singular `Create{Nav}` attaches `this` onto the new parent's inverse collection (`AddCreateNavMethod` `Notify.cs:92`).
- G9 `Poly.Tests/DomainModeling/Compile/EmitGolden/orders/Customer.cs.golden:26`: `Create{Nav}` is public (`Notify.cs:271`, `AccessModifier.Private`).
- G12 `Poly.Tests/DomainModeling/Compile/EmitGolden/orders/Poly.Types.cs.golden:39`: `DomainResult<T>.Failure` must not expose `default!` as `Value` (`T?` or throw when `!IsSuccess`); `BuildDomainResultGenericTypeDef` (`DomainToCSharpExporter.Actions.cs:786`).
G9 and G12 were listed under tc2 in the parent request; moved here (see C8-tc2).

## Files
- `Poly/DomainModeling/Lowering/EffectLoweringPass.cs` (G4, D15)
- `Poly/DomainModeling/Lowering/DomainToCSharpExporter.Notify.cs` (G6, C6b2, G8, G9)
- `Poly/DomainModeling/Lowering/DomainToCSharpExporter.Actions.cs` (G12)
- Tests: `DomainToCSharpExporterTests.cs` (the WIP patch has `Export_SelfReferencingCollection_CreateGetsEmptyListNotThis`), `ParityTests.cs`, goldens.

## Shape matrix
| Input kind | Before | After | Test that proves it |
|---|---|---|---|
| `when loans Overdue` create Fine | `this.Create("Fine", …)`, throw, dead local; Fine not on `Patron.Fines` | `this.CreateFines(...)`, `DomainResult` failure | G4/D15 golden; parity row |
| self-rel `CreateParent` | parent/child cycle | new parent's parent is null | G6 golden |
| self-rel collection slot | `Node.Create(name, this, this)` (CS1503) | `new List<Node>()` | exporter test, export compiles |
| singular `CreateOrder` | inverse not attached | `order` lists `this` | G8 golden |
| `Create{Nav}` | private | public | G9 golden |
| failed `DomainResult<T>` | `Value = default!` | `T?` or throws on failure | G12 golden |

## Done when
Each cited golden no longer shows its finding; goldens regenerated, not hand-edited; printed C# for every sample compiles; full suite green.

## SHIP if / NOT SHIP if
SHIP if every finding above is gone from its golden. NOT SHIP if any remains, or a golden was hand-edited.

## Tests
```
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/DomainToCSharpExporterTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/ParityTests/*'
POLY_UPDATE_GOLDEN=1 dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/EmitGoldenTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj
```

## Hand-edit
Six local emit edits in three files; each maps to one finding id.

## Needs Scot
none

## Log
| Date | Who | Mill | SHA | Verdict / event | Findings |
|------|-----|------|-----|-----------------|----------|
| 2026-10-10 | handoff | - | - | planned from the C8-tc split | G4 D15 G6 G8 G9 G12 C6b2 |
