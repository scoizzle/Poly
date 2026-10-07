# TASK Q1b — Regenerate demo/Poly.RestApi; test the checked-in demo equals fresh output

Status: branch `cursor/q1b-demo-regeneration-4ad1` from master `bfe66beb`. Implementation `a0d9a2f4`. No PR opened by the implementer.

Lane B. Review 1. Plan: [`docs/domain-modeling/pipeline-convergence-plan.md`](../../../domain-modeling/pipeline-convergence-plan.md) **Q1b**; scope from decision V10 (decisions file). Depends on nothing.

## Scope
The checked-in `demo/Poly.RestApi` was stale and hand-touched: `Patron.HasOverdueLoans` threw `NotSupportedException("... requires store-aware evaluation ...")` (`Patron.cs:112` on master), so `POST /reinstate` could never succeed. Nothing under `Poly/` produces that message any more. No test compared the demo to fresh output. Regenerate the demo through the real compiler and add that test.

## Files
`demo/Poly.RestApi/library.poly` (new: the demo's source), the nine generated files in `demo/Poly.RestApi/` (`Book.cs`, `Patron.cs`, `Loan.cs`, `Fine.cs`, `PremiumPatron.cs`, `Poly.Types.cs`, `LibraryDbContext.cs`, `Program.cs`, `demo.http`); tests `Compile/EmitGoldenTests.cs`, `Lowering/DomainToCSharpExporterTests.cs` (`LibraryCheckoutDsl` private → internal).

## Done when
`git grep "store-aware evaluation" -- demo/` is empty; the comparison test passes; `POST /reinstate` from `demo.http` checked against the running demo (or stated as not checked).

## SHIP / NOT SHIP
SHIP if the throw is gone and the comparison test passes. NOT SHIP if the demo was edited by hand to match.

Hand-edit: yes. No generated file was edited by hand; all nine are CLI output.

## Shape
- Source: `library.poly` is `LibraryCheckoutDsl` plus `uses sqlite` and `uses http`. The exporter tests keep the constant because their session-less parse resolves `uses` through the core catalog, which has no `sqlite` or `http`. `LibraryDemo_Source_IsExporterTestsLibraryDomainWithHostDoors` fails if the two drift.
- Regenerate: `dotnet run --project src/Poly.DslCompiler -- --mode all --dbms sqlite demo/Poly.RestApi/library.poly demo/Poly.RestApi`. The same command is a comment on the comparison test.
- `LibraryDemo_DslCompilerOutput_MatchesCheckedInDemo` makes the CLI's `Compile(poly, CompileMode.All, DbmsPack.Sqlite)` call. It checks that the `*.cs` file set equals the produced `.cs` set, so a leftover file fails. It also compares every produced file byte for byte (line endings normalized).
- Observed, not changed: without `--dbms sqlite` the host `Program.cs` uses `UseInMemoryDatabase` even though the source says `uses sqlite`. The demo `.csproj` references only the EF SQLite provider, so the flag is required.

## Log

| Date | Who | Mill | SHA | Verdict / event | Findings |
|------|-----|------|-----|-----------------|----------|
| 2026-10-07 | implementer | Cursor agent | `a0d9a2f4` | branch pushed; no PR | New tests red on the stale demo (Book.cs differs from line 2), green after regeneration. EmitGoldenTests 23 passed. Full suite 3412 passed, 0 failed (master baseline 3410). Demo builds with 0 warnings. `POST /reinstate` on the running demo: 409 while Active, 200 after suspend, patron back to Active with `MaxItems` 5. |
