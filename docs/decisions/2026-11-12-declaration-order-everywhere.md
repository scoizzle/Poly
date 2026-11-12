# Every document projection lists types and members in declaration order (Q12, #659)

Status: **decision record for owner ruling Q12, recorded by slice D-01 ([#688](https://github.com/scoizzle/Poly/issues/688)) from [#647](https://github.com/scoizzle/Poly/issues/647).**
The ruling is the Q12 row of the 2026-11-11 owner rulings on epic [#659](https://github.com/scoizzle/Poly/issues/659#issuecomment-6801963063)
([comment 6801963063](https://github.com/scoizzle/Poly/issues/659#issuecomment-6801963063)): declaration order everywhere, in all exports.
This file records how D-01 implements it and the contract the dependants cite.

## Context

- **The defect.** Thirty `OrderBy`/`ThenBy` sites in six files under `Poly/DomainModeling/Queries/` sorted type names and member
  names ordinally "for determinism": the Markdown report, the HTML document, the JSON Schema, the JSON-LD graph, the SHACL graph
  and the shared `JsonLdContext`. A reader of any projection saw `Apple` before `Zebra` even though the source declared `Zebra` first.
- **What landed first.** S-15 made `Domain.Types` document order and gave every entity `MemberOrder`: properties, navigations,
  stages, entity-level actions and entity-level policies, interleaved as authored, as `(Kind, Name)` pairs.
- **The gap in `MemberOrder`.** No member change appends to it: `SetEntityMemberOrderChange` is its only writer, so builder evolution
  (v1 `EvolveTool`, `AddPropertyToEntityChange`, `RemovePropertyFromEntityChange`) and programmatic models leave it empty or partial.
  A reader that trusted it only when it was complete would reorder existing members when one was added.
- **`DslOutline` gaps.** It cannot see enum members or value-type properties (its container rule is entities only), and it classifies
  a member as a navigation only when the third token is `many`, `one` or `owned`. The test oracle inherits both.

## Decision

Projections list what the model lists. There is no determinism sort: determinism comes from the model's own order.

1. **Types.** `Domain.Types` is document order. A projection with one ordered collection over all types (`$defs`, JSON-LD `@graph`,
   SHACL `@graph`) lists types in `Domain.Types` order, kinds interleaved as authored. A projection with per-kind sections (Markdown,
   HTML) keeps its section order and orders each section by `Domain.Types`. `DomainExportOrder.Types` is the projection-side reader.
2. **Entity members** come from `EntityDeclarationOrder.Members(entity)`, one seam over all five kinds. It walks `MemberOrder`, takes the
   first match per `(Kind, Name)` and drops an entry that resolves to no member of that kind, then appends every still-unplaced member in
   list order (Property, Navigation, Stage, Action, Policy). The merge is monotone: adding a member never reorders existing ones,
   removing one drops only that member, and an empty `MemberOrder` degrades to per-list order. The fallback is by design, not
   fail-closed, because builder evolution and programmatic models are legitimate inputs. `Properties` and `Navigations` are filtered views
   of `Members`, so the table projections and the JSON projections cannot disagree about relative order.
3. **Enum members** follow `EnumType.MemberNames`, including the key order of `x-poly-enum`.
4. **Value-type properties** follow `ValueType.Properties`.
5. **No sort.** `grep -rnE "OrderBy|ThenBy|\.Order\(|Sort|Sorted|Reverse" Poly/DomainModeling/Queries/` is empty.
6. **`@context` key order** carries no meaning in JSON-LD, so `JsonLdContext` emits types and then member terms in the caller's document
   order; a member name used once and not claimed by a type or prefix keeps its bare key, any other is qualified `Owner.Name` in
   encounter order.

## Consequences

- **G-18 (the DSL printer)** must print entity members through `EntityDeclarationOrder.Members`. It does not walk `MemberOrder` today:
  `DomainDslPrinter.Entities.cs` prints properties, then navigations sorted by name, then policies, stages and actions. G-18 must not
  re-implement the merge.
- **L-07 / L-08** read `Members` for Create order.
- **C-10** needs no seam: `ConstraintCompilation` only drops its own `OrderBy` and reads `entity.Properties`.
- **D-02, D-04, D-06, D-08, D-09** consume the same seam where they need member order.
- A new reader of entity-member order uses `EntityDeclarationOrder`; a second order rule in a producer is the defect this record closes.

## Open

- `SOURCE_SENSITIVE` in the review-bundle classifier matches `*schema*`, so `JsonSchemaExporter.cs` classifies as a migration path, a
  lexical false positive. Narrowing it is an owner ask outside this repository.
- `DslOutline` exposing enum members and value-type properties, or classifying a bare navigation, is a later G-owned change.
