# TASK F35a-lower - Lower date-minus-date to Subtract

Status: planned. Depends: F35a-check merged to master.
Branch `slice/F35a-lower`. PR (none yet). Lane swarm.
Source: [`evolve-2026-10-14.md` **F35**](../../../probes/findings/evolve-2026-10-14.md).

## Rules
Branch from current origin/master after F35a-check is in it. One PR. Leave it open. Scot merges. Write the failing test first. One `--treenode-filter`. Stop and report when this card says stop. Add a Log row on each push.

## Scope
F35a-check lets `Finished - Started` through analysis. `DateAddDaysLowering` still claims that subtract and emits `AddDays` of a negated right (`TemporalLowering.cs:107-136`). The right side is a date property, so `AddDays` is the wrong call.

`DateOperation` `DiffDays` already lowers to `Subtract` (`DomainExpressionLoweringPassTests.DateOperation_DiffDays_LowersToInvoke`). `DateOperationBinder` prints only literal offsets and throws on a property offset. Leave that binder. This card edits the subtract arm of `DateAddDaysLowering` only.

## Files
- `Poly/DomainModeling/Libraries/Temporal/TemporalLowering.cs` — `DateAddDaysLowering.TryLower`, subtract arm, before the `AddDays` emit at `:133-135`.
- `Poly.Tests/DomainModeling/Analysis/DateDifferenceDurationTests.cs` — add the VM and print rows. The file exists from F35a-check. Rebase onto the master that contains it.

## The edit
On the subtract arm, after the left side is a date-family `PropertyAccess`:

1. The right side is a `PropertyAccess`.
2. `propertyTypeResolver` on the right name returns a type in the same family as the left (`FamilyFromTypeName`: `DateOnly` with `DateOnly`, `DateTime` with `DateTime`).
3. Emit `new Invoke(new Member(route(left), "Subtract"), [route(right)])` and return true.

Otherwise keep the existing `AddDays` path, including `Finished - 7` and `Finished + 1 Days`. If the left side is not a `PropertyAccess`, return false, as today. `Now - Started` is out of this card.

## Shape matrix
| Input | Before | After | Test |
|---|---|---|---|
| Close assigns `Elapsed` to `Finished - Started`, Started `UtcNow`, Finished one hour later | wrong or throws | `Elapsed` equals that one-hour `TimeSpan` | `Close_Elapsed_IsTheDifference` |
| generated C# for that assign | `AddDays` | contains `.Subtract(` | `Close_PrintsSubtract` |
| `Finished + 1 Days` | `AddDays` | still `AddDays` | `PlusDay_StillAddDays` |

Use the Span domain from F35a-check. Copy `Evolve`, create, and `InvokeAction` from `UniversityDogfoodTests.cs`. Set `Started` and `Finished` on the instance before Close. `GetProperty<object>("Elapsed")` is a `TimeSpan`.

## Done when
The three rows pass. F35a-check's analysis rows still pass.

## SHIP if / NOT SHIP if
SHIP when the diff is the subtract early-return plus the new test methods.
NOT SHIP when `ExpressionTypeAnalyzer`, `DateOperationFold`, or `DateOperationBinder` changed.

## Tests
`dotnet run --project Poly.Tests/Poly.Tests.csproj -p:NuGetAudit=false -- --treenode-filter '/*/*/DateDifferenceDurationTests/*'`

If `EmitGoldenTests` fails and the only change is `.Subtract(` where a date property is subtracted from a date property, regenerate with `POLY_UPDATE_GOLDEN=1` on `'/*/*/EmitGoldenTests/*'`, then rerun without the variable. Any other golden change: stop. Do not hand-edit a `*.golden`.

## Leave
`ExpressionTypeAnalyzer` (F35a-check owns it). `DateOperationFold`. `TemporalExpressionPrintBinders` / `DateOperationBinder`. The Add arm of `DateAddDaysLowering`. `DurationCheck`.

## Stop
Stop if analysis of the Span domain still has errors. F35a-check is not in the branch. Stop if the left side is `Now`. Stop if the two families differ and the new arm still emits `Subtract`.

## Hand-edit
One early return inside `TryLower` before `AddDays`. Scot can delete it and date-minus-date goes back to `AddDays`.

## Needs Scot
none

## Log
| Date | Who | Mill | SHA | Verdict / event | Findings |
|------|-----|------|-----|-----------------|----------|
| 2026-10-15 | queue | plan | — | planned, after F35a-check | none |
