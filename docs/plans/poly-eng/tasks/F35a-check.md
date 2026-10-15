# TASK F35a-check - Allow a date property minus a date property

Status: planned
Branch `slice/F35a-check`. PR (none yet). Lane swarm.
Source: [`evolve-2026-10-14.md` **F35**](../../../probes/findings/evolve-2026-10-14.md). This card is the analysis half. Lowering is [F35a-lower](F35a-lower.md) and starts after this merges.

## Rules
Branch from current origin/master. One PR. Leave it open. Scot merges. Write the failing test first. One `--treenode-filter`. Stop and report when this card says stop. Add a Log row on each push.

## Scope
`Finished - Started` on two `DateTime` properties is reported as `arithmetic operand is not numeric`. Folds run at parse time and only when the right side is a `Duration` (`DateOperationFold`). They must stay that way: a fold cannot claim every `PropertyAccess - PropertyAccess`, or `Count - Limit` breaks.

Checked on master `06844e39`:
- `case Subtract` calls `CheckArithmetic` (`ExpressionTypeAnalyzer.cs:373-374`).
- `CheckArithmetic` (`:407-428`) allows numeric/numeric, text/text on add, date/number, and date/duration. Two dates hit the error at `:425-426`.
- `InferType` has no `Subtract` arm (`:639-652`), so the result is Unknown. `Compatible` accepts Unknown (`:720`). Silencing the Subtract report is enough for the assign to `Duration` to succeed. Do not teach `InferType` that the result is `Duration`.

## Files
- `Poly/DomainModeling/Analysis/ExpressionTypeAnalyzer.cs` — the `case Subtract sub:` arm only.
- `Poly.Tests/DomainModeling/Analysis/DateDifferenceDurationTests.cs` — new. Copy `Parse` from `ExpressionTypeAnalysisTests.cs`.

## The edit
In `case Subtract sub:`: when both sides are `PropertyAccess`, both `InferType` results are `TypeCategory.Date`, and both type names are the same family, `WalkExpression` each side and return. Otherwise call `CheckArithmetic` as today.

Families: `Date` with `DateOnly`, and `DateTime` with `Timestamp`. Different families still report. `CheckArithmetic` itself stays shared with Add, so `Finished + Started` still reports.

## Shape matrix
| Input | Before | After | Test |
|---|---|---|---|
| `assign Elapsed to Finished - Started`, both `DateTime`, `Elapsed: Duration` | error `arithmetic operand is not numeric` | zero errors | `DateTime_Minus_DateTime_Assigns` |
| both sides `Date` | error | zero errors | `Date_Minus_Date_Assigns` |
| `Finished` `DateTime`, `Started` `Date` | error | error | `MixedFamily_StillErrors` |
| `Finished + 1 Days` | succeeds | succeeds | `PlusDuration_StillFolds` |
| `Count - Limit`, both `Number` | succeeds | succeeds | `Number_Minus_Number_Stays` |

```poly
domain Span
uses temporal
Job: entity {
  Started: DateTime
  Finished: DateTime
  Elapsed: Duration
  Close: action { assign Elapsed to Finished - Started }
}
```

## Done when
Analysis of that domain has zero errors. The other four rows hold. No VM run in this card.

## SHIP if / NOT SHIP if
SHIP when the diff is the Subtract arm plus `DateDifferenceDurationTests`.
NOT SHIP when `DateOperationFold`, `DateAddDaysLowering`, `InferType`, or `CheckArithmetic`'s shared predicate changed.

## Tests
`dotnet run --project Poly.Tests/Poly.Tests.csproj -p:NuGetAudit=false -- --treenode-filter '/*/*/DateDifferenceDurationTests/*'`

## Leave
`DateOperationFold`. `TemporalLowering` / `DateAddDaysLowering` (the next card). `InferType`. `CheckArithmetic`'s predicate. `DurationCheck`. Bare `90 Minutes`. Date subtraction in the VM.

## Stop
Stop if the left side of the fixture is `Now` rather than a property. Stop if making the test pass requires editing `InferType` or the fold.

## Hand-edit
One early return on the Subtract arm. Scot can delete it and the numeric error returns.

## Needs Scot
none

## Log
| Date | Who | Mill | SHA | Verdict / event | Findings |
|------|-----|------|-----|-----------------|----------|
| 2026-10-15 | queue | plan | — | planned | none |
| 2026-10-15 | swarm | F35a-check | 4488d5e1 | shipped, PR open | Subtract arm early-returns same-family date-minus-date (reuses MixedDateGranularity); DateDifferenceDurationTests 5/5; full suite 3846/3846 |
