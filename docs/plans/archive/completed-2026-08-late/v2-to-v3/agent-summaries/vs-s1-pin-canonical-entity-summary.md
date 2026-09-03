# vs-s1-pin-canonical-entity Summary

**Task:** Pin canonical vertical-slice entity  
**Date:** 2026-07-12  
**Status:** ✅ Done  

## Decision: **Person**

| Factor | Person | Order |
|--------|--------|-------|
| Policy property simplicity | `Age` (int) — simplest numeric guard | `Total` (decimal), `Status` (string) |
| Policy test files | 3 (`PolicyVmEvaluationTests`, `DomainValidatedEvaluationTests`, `EntityMutationRoundTripTests`) | 1 |
| Mutation round-trip tests | 3 | 0 |
| Type-mapper examples | Primary | Secondary |
| Natural lifecycle stages | born → child → adult → senior | cart → pending → paid → shipped |

Person's `Age` is the **minimum expressive type** for proving policy evaluation. `Age >= 18` compiles to a single `Member(Parameter, "Age")` → `GreaterThanOrEqual` node.

## Test files using Person

- `Poly.Tests/DomainModeling/Lowering/PolicyVmEvaluationTests.cs` — `Person(string Name, int Age)`
- `Poly.Tests/DomainModeling/Lowering/DomainValidatedEvaluationTests.cs` — `Person(string Name, int Age)`
- `Poly.Tests/DomainModeling/Evolution/EntityMutationRoundTripTests.cs` — `Person(string Name, int Age)`
- `Poly.Tests/TestHelpers/DomainTypeMapperTests.cs` — primary example type

## Output

- `vs-README.md` — canonical entity section populated with rationale + test file list
- `vertical-slice-finish-plan.md` — Slice 1 marked ✅ Done; Person pinned
- `vs-s1-pin-canonical-entity.md` — status updated to [x]

**Next:** Slice 2 — `vs-s2-subject-helper-and-reject.md`
