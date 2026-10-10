# TASK C4c - Constraints on set; enum value check
Status: merged #141 `cc5cd55a` 2026-10-09. Branch slice/c4c-plan-ahead. Lane B. Review 2. Implement mill: Grok (review on OpenCode).

## Scope
Depends on C2b (#136 `10e6570a`) and E1 (#133 `71fef08f`); both are on master. C4b (#139, unique-on-create) is in review and is NOT a dependency of this slice. Decision 11 (every mutation enforces every invariant) and decision 12 (enum checks compile as trees) are settled (plan §19, `pipeline-convergence-plan.md:1064-1065`).
The card's `DomainEntityInstance.cs:328` is wrong: `SetProperty` is `Poly/DomainModeling/Runtime/DomainEntityInstance.cs:414` (unique-only, `:419-424`); `git grep '\.SetProperty(' Poly` is empty and there are 13 test call sites (the card's "0 product / 13 test" holds). Make it `internal` — the smallest remedy the card allows — so no product path can bypass a check; `InternalsVisibleTo("Poly.Tests")` (`Poly/GlobalUsings.cs:21`) keeps the 13 callers compiling. The action `assign` path already enforces required/range/length/pattern in the shared tree (`EffectLoweringPass.cs:328-425`), so "on set" in the card means that path (T3's mutation column, plan `:99`).
Add the missing enum member check as a tree to both the create factory (`DomainToCSharpExporter.Notify.cs:331-456`, no enum arm) and the assign check (`EffectLoweringPass.cs:328-425`, no enum arm). The card's "EffectInvariantAnalyzer (DMEFF008)" is also wrong: `EffectInvariantAnalyzer` publishes `ActionInvariantMetadata`; DMEFF008 is reported by `EffectAnalyzer` (`EffectAnalyzer.cs:1295-1306`; code `DomainModelDiagnosticCodes.cs:54`).

## Files
- `Poly/DomainModeling/Runtime/DomainEntityInstance.cs` (`:414` `public`→`internal`; XML doc)
- `Poly/DomainModeling/Lowering/DomainToCSharpExporter.Notify.cs` (`BuildCreateConstraintChecks` `:331-456`: add enum arm)
- `Poly/DomainModeling/Lowering/EffectLoweringPass.cs` (`Assign` `:245-263`; `HasAssignableConstraints` `:266-270`; `AppendAssignConstraintChecks` `:328-425`: add enum arm)
- `Poly.Tests/DomainModeling/Lowering/ParityTests.cs` (parity rows for required/length/pattern/enum on set; enum-on-create row)
- `Poly.Tests/DomainModeling/Lowering/ConstraintAssignLoweringTests.cs` (simulate-side enum reject + soundness test)
- `Poly.Tests/TestHelpers/ParitySides.cs` (`Materialize` `:107-120`: parse a string arg into an enum parameter type so the printed side can carry a member)
- `Poly.Tests/DomainModeling/Lowering/EnumMemberAgreeTests.cs` (enum-on-set agree test)
- `Poly.Tests/DomainModeling/DomainEntityInstanceTests.cs`, `Poly.Tests/DomainModeling/ActionEntityReturnTests.cs` (unchanged callers; confirm they still compile)

## Shape matrix
| Input kind | Before | After | Test that proves it |
|---|---|---|---|
| `DomainEntityInstance.SetProperty` | `public`, unique-only (`DomainEntityInstance.cs:414-426`); product could mutate past range/length/pattern/required | `internal`; unique stays; no product bypass | `git grep 'public void SetProperty'` empty; `ActionEntityReturnTests.SetProperty_UniqueCollision_ThrowsBeforeMutate` |
| Assign to an enum prop from an action parameter (`assign Status to status`, `status: TruckStatus`) | no member check; runtime stores any string | `AppendAssignConstraintChecks` emits a not-any-member guard (`if (v != T.M1 && v != T.M2) fail`); shares the simulate/print tree | `ConstraintAssignLoweringTests.EnumAssign_OutOfMember_Fails`; `ParityTests.Invoke_WhenAssigningValidEnumMember_Agrees` |
| Create an entity with an enum prop (external/DEI string) | no member check in `BuildCreateConstraintChecks`; invalid string stored | same guard in the create factory | `ParityTests.Create_WhenEnumMemberValid_Agrees`; `ConstraintAssignLoweringTests.Create_WhenEnumOutOfMember_Fails` |
| Required/empty on set (`assign Name to name`, `name: Text`) | tree guard exists (`:338-351`); no parity row | unchanged tree; new parity row | `ParityTests.Invoke_WhenAssignBreaksRequired_FailsTheSameWay` |
| Length on set | tree guard exists (`:376-407`); no parity row | unchanged tree; new parity row | `ParityTests.Invoke_WhenAssignBreaksLength_FailsTheSameWay` |
| Pattern on set | tree guard exists (`:409-424`); no parity row | unchanged tree; new parity row | `ParityTests.Invoke_WhenAssignBreaksPattern_FailsTheSameWay` |
| Range on set | already enforced + parity row exists | unchanged | `ParityTests.Invoke_WhenAssignBreaksRange_FailsTheSameWay` |
| Assignment analysis accepts (no DMEFF008 Error) | not pinned against the runtime guard | runtime guard never trips for accepted args | `ConstraintAssignLoweringTests.AcceptedAssign_RuntimeGuardDoesNotTrip` |

## Done when
- `SetProperty` is not public and cannot bypass a check (unique still enforced).
- Enum member check tree exists for create and set; message identical on simulate and print (one shared tree), e.g. `"'Status' is not a valid member of PatronStatus."`.
- Parity `AssertAgree` rows pass for required, length, pattern, and a valid enum on set (range row already passes), plus an enum-on-create row. `Materialize` parses enum-typed printed parameters from their string member.
- Soundness test: whenever `EffectAnalyzer` reports no DMEFF008 Error for an assignment, invoking the same assignment at runtime does not trip the guard. (The card said `EffectInvariantAnalyzer`; correct it to `EffectAnalyzer`.)
- The 13 `SetProperty` callers compile unchanged; full suite green.

## SHIP if / NOT SHIP if
SHIP if `SetProperty` has no public form, the enum guard trips for a non-member on both create and set in simulate, and every check-kind parity row `AssertAgree`s.

NOT SHIP if the soundness test is missing, if an invalid enum is silently stored, if the enum message differs between simulate and print (means two trees, not one), if the printed `Materialize` is left unable to pass an enum member, or if the public `IDictionary` bag setter (the VM write path) is changed.

## Tests
```
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/ParityTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/ConstraintAssignLoweringTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/EnumMemberAgreeTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/AssignConstraintAnalysisTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/DomainEntityInstanceTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/ActionEntityReturnTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/DomainToCSharpExporterTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj
```

## Hand-edit
One keyword (`public`→`internal` on `SetProperty`) plus one enum guard arm in each of two check builders, both built from the same `Member(EnumType, member)` + `NotEqual` nodes; Scot can open those three spots and read the guard by hand.

## Needs Scot
none

## Log
| Date | Who | Mill | SHA | Verdict / event | Findings |
|------|-----|------|-----|-----------------|----------|
| 2026-10-09 | planner | opencode/deepseek-v4.1-flash | `592a137f` | planned | - |
| 2026-10-09 | implementer | grok/grok-4.6 | `af64777d` | pushed | tests 3486/3486; sweep: SetProperty internal; enum Member+NotEqual on create and assign |
| 2026-10-10 | reviewer | opencode/opencode-go/deepseek-v4.1-flash | `7318db0b` | NOT SHIP | https://github.com/scoizzle/Poly/pull/141#issuecomment-6092867956 (Grug NOT SHIP at 7318db0b, 4 open findings, mode full) |
| 2026-10-10 | implementer | grok/grok-4.6 | `39e4f910` | fixes pushed | R1, R2, R3; disputed: none; R4 skipped (nit >1 line); tests 3495/3495 |
| 2026-10-09 | planner | opencode-go/deepseek-v4.1-flash | `cc5cd55a` | merged #141 | squash-merged to master |
