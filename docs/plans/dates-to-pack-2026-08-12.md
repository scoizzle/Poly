# Plan: date/time surface out of core → DatePack (deferred)

**Date:** 2026-08-12 · **Status:** recorded, not scheduled
**Origin:** discovery pass `discovery-dates` — probing the date/default surface surfaced
compile-fail bugs (F-D1, F-D2) whose root cause is **unauthorized core-DSL surface**, not
isolated defects. The date surface was added to the core DSL by an agent without approval;
the intent is to ship dates **as a pack** (like `Poly.Packs.Sqlite` / `Poly.Packs.SqlServer`).

## Decision (recorded)

- **Do not fix-forward** on the core date surface (no "target-typed lowering" polish on
  code that shouldn't be in core).
- Keep the code as-is until the pack work is scheduled; this doc is the record.
- When scheduled: **strip** the date surface from the core DSL and ship it as an opt-in
  `DatePack` added to the authoring context (`DomainInputBuilder`), mirroring how storage
  packs add their surface.

## Footprint (core files touched by the date surface)

| Layer | Files | Date surface |
|-------|-------|--------------|
| Parser / grammar | `Parsing/DslTokenKind.cs`, `Parsing/DslTokenReader.cs`, `Parsing/DslGrammar.cs`, `Parsing/PolyDslParser.cs` | `Date`/`DateTime` type keywords, token kinds, type-name mapping |
| Type system | `Introspection/PrimitiveType.cs`, `Introspection/TypeCategory.cs` | `PrimitiveType.DateOnly/DateTime/Guid`, `TypeCategory.DateOnly` (+ `Guid` identifier category) |
| Expression IR | `DomainModeling/DomainExpression.cs`, `DomainModeling/DomainExpressionDispatch.cs` | `DateOperation` node, `DateOperationKind {AddDays, AddMonths, DiffDays}` |
| Analysis | `DomainModeling/Analysis/ExpressionTypeAnalyzer.cs`, `Analysis/EffectAnalyzer.cs` | Date typing, Date-vs-DateTime incompatibility check |
| Lowering / export | `DomainModeling/Lowering/DomainExpressionLoweringPass.cs`, `EffectLoweringPass.cs`, `DomainTypeMapping.cs`, `DomainToCSharpExporter.cs` | `now/today/guid → DateTime.UtcNow / DateOnly.FromDateTime / Guid.NewGuid`, Date→`DateOnly` CLR mapping, the `DateOnly + long` CS0019 workaround |
| Runtime | `DomainModeling/Runtime/DomainEntityInstance.cs`, `Runtime/DomainExpressionRewriteBase.cs` | `now/today/guid` evaluation, Date/DateTime type dispatch |
| VM / interpretation | `Interpretation/Vm/DirectVmAbiEmitter.cs`, `Interpretation/AbiValueTypes.cs`, `Interpretation/Analysis/Semantics/ValueRepresentationPass.cs`, `SyntaxTypeCompatibilityAnalyzer.cs`, `TypeDefinitionNodeAnalyzer.cs`, `CSharpGenerator.cs` | DateTime/DateOnly/Guid as heap-handle value types, `default(Guid)` |

Note: `PrimitiveType.DateTime/Guid` and the VM heap-handle treatment may be legitimately
needed CLR-host infrastructure (the VM must carry DateTime values regardless). The
**DSL authoring surface** is the part to remove/gate: the `Date`/`DateTime` type keywords,
`now`/`today`/`guid` defaults + assign RHS, and `DateOperation` arithmetic. Decide per-file
during the pack work whether the primitive/VM support is infra or date-crap.

## Shipped (today, because the surface leaked) vs guide

The guide line 713–714 lists "Date operations" under **Not yet shipped** — that was
correct intent. The code ships, despite the guide: `EndDate + 30`, `TargetDate - 7`,
date comparisons, and `default(now)`/`default(today)`/`assign X to now` all work (when
types align). The guide stays as-is; the code is what drifted.

## Known bugs in the leaked surface (do NOT fix forward — fold into pack)

- **F-D1** `default(guid)` on `Text` → export `string ?? Guid.NewGuid()` → CS0019.
  Runtime stores a GUID string. (`probes/discovery-dates/guid-on-text.poly`)
- **F-D2** `now`/`today` Date↔DateTime confusion accepted, export breaks:
  `Date default(now)` → CS0019, `DateTime default(today)` → CS0019,
  `assign DateProp to now` → CS0029. (`probes/discovery-dates/date-now-confusion.poly`)
- **F-D4** `default(guid)` rejection message hints at `Uuid`/`Guid` DSL types that do
  not exist.

## Pack design sketch (for when scheduled)

A `DatePack` (or fold into the sql pack family) added via `DomainInputBuilder` would:
1. Register `Date`/`DateTime` (and `Guid`?) primitives in the **input context** type
   registry — not the core `Poly.DslCompiler`/`Introspection` defaults.
2. Contribute the parser tokens/grammar for the type keywords and `now`/`today`/`guid`.
3. Provide the lowering/export CLR mappings (Date→`DateOnly`, now→`DateTime.UtcNow`, …).
4. Carry the fixed default/assign-RHS lowering **typed for the target property**
   (string→`Guid.NewGuid().ToString()`, `now`→`Date` as `DateOnly.FromDateTime`, …).
5. Keep the core DSL = `Text`/`Number`/`Boolean` (+ enums, entities, stages, actions,
   policies).

## Reference

- Discovery findings: [`probes/findings/discovery-dates.md`](../../probes/findings/discovery-dates.md)
- Probes: `probes/discovery-dates/`
- Pack precedent: `src/Poly.Packs.Sqlite/`, `src/Poly.Packs.SqlServer/`
