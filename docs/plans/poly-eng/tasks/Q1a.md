# TASK Q1a - Parity rows for collection rules (any/all/none/filtered count)
Status: planned. Branch slice/q1a-collection-parity-rows. Lane A. Review 1. Implement mill: Grok (review on OpenCode).
## Scope
Quantifier lowering already landed: `DomainExpressionLoweringPass` turns filtered quantifiers into foreach loops (class remarks `Poly/DomainModeling/Lowering/DomainExpressionLoweringPass.cs:24-31`; dispatch `AnyExpr`/`AllExpr`/`NoneExpr`/`CountExpr` `:474-489`; `LowerFilteredQuantifier` `:503-539`). `git grep -nE 'AnyRelated|AllRelated|CountRelated' -- Poly Poly.Mcp Poly.Tests` exits 1 (verified).
The T2 helper is live: `ParityScenario` (`Poly.Tests/TestHelpers/ParityScenario.cs:21`, `FromDsl`/`AssertAgree`; `ParitySide.Create` links by passing earlier creates as a collection value, `:83-113`). One row already exists: `ParityTests.EvaluatePolicy_HasOverdueLoans_AgreesForLinkedLoans` (`Poly.Tests/DomainModeling/Lowering/ParityTests.cs:224-241`, `any loans where Status is "Overdue"` via `PolicyDsl` `:64-76`).
The H4 Constant check the card names is already in `EveryConceptHasTreeTests.DomainExpressionConstants` (`Poly.Tests/DomainModeling/Compile/EveryConceptHasTreeTests.cs:292-299`), planted at `:154-165`, asserted at `:122`. Q1a adds no H4 code.
The card's exporter pointers are stale: `Export_HasOverdueLoans_PrintsForeachOverLoans` is `Poly.Tests/DomainModeling/Lowering/DomainToCSharpExporterTests.cs:3056` and `Patron_HasOverdueLoans_SimulateAndGeneratedCSharp_Agree` is `:3067` (card said ~3034/~3045).
So the gap is only the `all`, `none` and filtered `count` rows in `ParityScenario`; `any` is already pinned. Test-only.
## Files
- `Poly.Tests/DomainModeling/Lowering/ParityTests.cs` (new `QuantifierDsl` constant + rows)
No product file. `EveryConceptHasTreeTests.cs` is unchanged (H4 already covers the Constant check).
## Shape matrix
| Input kind | Before | After | Test that proves it |
|---|---|---|---|
| `any Rel where body` (`any loans where Status is "Overdue"`) | one `ParityScenario` row (`ParityTests.cs:224-241`) | unchanged (do not duplicate) | existing `ParityTests.EvaluatePolicy_HasOverdueLoans_AgreesForLinkedLoans` |
| `all Rel where body` (`all loans where Status is "Active"`) | simulate-only `Simulate_All_EmptyCollection_IsFalse` (`QuantifierLoopTests.cs:52-62`); no `ParityScenario` row | new row: true when every linked Loan is Active, false for mixed and for empty (no vacuous true) | new `ParityTests.EvaluatePolicy_All_AgreesForLinkedLoans` |
| `none Rel where body` (`none loans where Status is "Lost"`) | no `ParityScenario` row | new row: true for empty and all-Active, false once a linked Lost Loan exists | new `ParityTests.EvaluatePolicy_None_AgreesForLinkedLoans` |
| `count Rel where body` (`count loans where Status is "Overdue" > 0`) | no `ParityScenario` row | new row: true for mixed (overdue+active), false for empty | new `ParityTests.EvaluatePolicy_FilteredCount_AgreesForLinkedLoans` |
| A tree holding `Constant { Value: DomainExpression }` | already reported by H4 | unchanged | existing `EveryConceptHasTreeTests` (no Q1a change) |
## Done when
- `ParityTests.cs` has a `QuantifierDsl` constant and rows for `all`, `none` and filtered `count` that pass in both simulate and printed C# (`AssertAgree` returns an empty difference list); the existing `any` row still passes.
- The four forms each assert an expected value (true and false, including the empty-collection case), not just that the sides agree.
- The H4 Constant-of-`DomainExpression` check is present (it already is, `EveryConceptHasTreeTests.cs:292`); Q1a adds no new code for it.
- Full suite green; `git grep -nE 'AnyRelated|AllRelated|CountRelated' -- Poly Poly.Mcp Poly.Tests` stays empty.
## SHIP if / NOT SHIP if
SHIP if the new all/none/filtered-count rows pass in both modes and each names its expected answer, and the diff is `Poly.Tests` only.

NOT SHIP if any row is skipped, if a row only asserts that both sides compile or only compares text, if the rows weaken or delete the existing any row, or if a product file changes.
## Tests
```
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/ParityTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/EveryConceptHasTreeTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj
```
## Hand-edit
Test-only: one `QuantifierDsl` constant and three short rows that mirror `EvaluatePolicy_HasOverdueLoans_AgreesForLinkedLoans`; Scot can open and edit each row cold.
## Needs Scot
none
## Log
| Date | Who | Mill | SHA | Verdict / event | Findings |
|------|-----|------|-----|-----------------|----------|
| 2026-10-09 | planner | opencode-go/deepseek-v4.1-flash | b894d9a6 | planned | - |
