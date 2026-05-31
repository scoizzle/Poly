# Micro-Task: Define First Concrete DomainChange Record Types (Add/Remove Entity + Property)

**Parent Workstream**: WS1 (Consolidated) - Evolution Layer Applicator + MVP Operations + NodeId Continuity  
**Difficulty**: Small-to-Medium (design + records, no heavy logic yet)  
**Estimated Context**: < 8k tokens

## Objective

Define the first 4 concrete `DomainChange` sealed record subtypes in `Poly/DomainModeling/Evolution/DomainChange.cs` (or a new `DomainChanges/` folder if it grows) that represent the minimal useful operations for the first applicator iteration: AddEntity, RemoveEntity, AddPropertyToEntity, RemovePropertyFromEntity.

These are pure data carriers. No applicator logic yet.

## Required Reading (Strictly Limited)

- Core Engineering Principles (build working code before abstractions; keep only what measurably helps): `docs/decisions/2026-core-engineering-principles.md`
- Evolution layer design (change representation section + anti-patterns): `docs/decisions/2026-05-31-evolution-layer-design.md` (read the "Change representation" open question and the anti-patterns list only)
- Current skeleton: `Poly/DomainModeling/Evolution/DomainChange.cs` (the abstract base only)
- V3 model shape for the affected types: `Poly/DomainModeling/Entity.cs` and `Poly/DomainModeling/Property.cs` (just the public surface + Children)
- `Poly/Syntax/Node.cs` (for NodeId reference)

**Do not read** the full plans, V2 mutation code, or large numbers of files.

## Exact Steps

1. Read the 5 files listed above (no more).
2. In `DomainChange.cs` (or a clean new file structure under Evolution/ if you prefer — keep it minimal), add four new sealed record types deriving from `DomainChange`:
   - `AddEntityChange(string Name, IReadOnlyList<Property> Properties, ...)` — capture only what is needed to construct a minimal Entity later.
   - `RemoveEntityChange(string NameOrId)`
   - `AddPropertyToEntityChange(string EntityName, Property Property)`
   - `RemovePropertyFromEntityChange(string EntityName, string PropertyName)`
3. Keep them simple and inspectable (public properties, no behavior). Follow the style of other records in the V3 model.
4. Add a short comment on each referencing the anti-pattern guidance (structured/inspectable, not opaque).
5. Ensure the file still compiles (add any necessary usings).
6. Add a tiny placeholder test file `Poly.Tests/DomainModeling/Evolution/DomainChangeTests.cs` (or extend an existing one) with just "can construct the four change types" assertions. No applicator yet.

## Verification

- [ ] `dotnet build Poly/Poly.csproj` (or the solution) succeeds cleanly.
- [ ] The four new record types exist, derive from `DomainChange`, and have clear public shape.
- [ ] A minimal test file exists that constructs instances of all four (even if the test does nothing else yet).
- [ ] No unnecessary methods, inheritance hierarchies, or "future-proofing" abstractions.
- [ ] Follows "build working code before extracting abstractions" — these are the smallest useful data carriers.

## Output

- Updated `Poly/DomainModeling/Evolution/DomainChange.cs` (or new sibling files) containing exactly the four new sealed records.
- One new or extended test file with construction smoke tests.
- The changes must be the minimal set that gives the next micro-task (applicator skeleton) something real to interpret.

## Status

**Claimed by**:  
**Started**:  
**Status**: Not Started / In Progress / Done (summary submitted)

**Notes / Blockers**:

---

**After completing this task**: Create a summary file in `../agent-summaries/` using `TEMPLATE-task-summary.md`. Only update the Status line here. Do not edit the master roadmap or workstream files yourself.