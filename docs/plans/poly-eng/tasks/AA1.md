# TASK AA1 - Execute `Await` by blocking, and the printed ⊆ simulatable audit

Status: in review
Branch `docs/async-await-interpreter`. PR #193. Lane async. Review see PR.
Plan card: [`docs/decisions/2026-10-16-async-interpreter-execution.md`](../../../decisions/2026-10-16-async-interpreter-execution.md) Slice 1.

## Scope
`await` gets one meaning: block on the operand (`operand.GetAwaiter().GetResult()`) and yield its
result, or throw its exception. No `ResumeWith`, no await allow-list. `docs/CORE.md` §3.3 and
`docs/decisions/vm-gap-analysis.md:82` move in the same change. A print-form audit enforces the law.

Checked on the branch at `88c5044c`:
- `EmitAwait` replaces both `Await => RejectCompile` arms (`DirectVmAbiEmitter.cs:239`, `:608`).
- `AwaitPattern.TryBind` resolves `GetAwaiter` / `IsCompleted` / `OnCompleted` / `GetResult` from the
  type provider; `Task` / `ValueTask` shapes take the `Ref` helpers.
- The `Await` type is the awaiter's `GetResult()` result; `TypeAndMemberResolutionPass.cs:124`.
- `ValueRepresentationPass` classifies `Await` instead of `Unknown`.

## Files
- `Poly/Interpretation/Analysis/Semantics/AwaitPattern.cs` (new)
- `Poly/Interpretation/Analysis/Semantics/TypeAndMemberResolutionPass.cs`
- `Poly/Interpretation/Analysis/Semantics/ValueRepresentationPass.cs`
- `Poly/Interpretation/LinqExpressions/LinqExpressionGenerator.ControlFlow.cs`
- `Poly/Interpretation/Vm/DirectVmAbiEmitter.cs`, `.Expressions.cs`, `.Statements.cs`
- `Poly.Tests/Interpretation/LanguageVmTests.cs`, `LanguageSurfaceTests.cs`
- `Poly.Tests/Interpretation/PrintedSubsetVmTests.cs` (new)
- `docs/CORE.md` §3.3; `docs/decisions/vm-gap-analysis.md:82`
- `docs/decisions/2026-10-15-async-await-in-the-interpreter.md`, `2026-10-16-async-interpreter-execution.md`

## Shape matrix
| Input kind | Before | After | Test that proves it |
|---|---|---|---|
| `Await` of a completed `Task<T>` | compile reject | yields `T` | `Await_CompletedTask_ReturnsResult` |
| `Await` of a faulted task | compile reject | throws the original exception | `Await_FaultedTask_ThrowsOriginalException` |
| `Await` of a non-generic `Task` / `ValueTask` | compile reject | void | `Await_NonGenericTask_IsVoid` |
| `Await` of a custom awaitable, incomplete | compile reject | waits on the continuation, returns `GetResult()` | `Await_IncompleteCustomAwaitable_WaitsForResult` |
| `Await` of a non-awaitable operand | compile reject | shape reject, with a reason | `Await_NonAwaitable_CompileRejected` |
| `Await` inside `try/catch`, inside a stored lambda | compile reject | executes | `Await_InsideTryCatch_*`, `Await_InsideStoredLambda_Executes` |
| A kind printed live and compile-rejected | not detected | the audit fails | `PrintedSubsetVmTests.LiveCompileReject_Fails` |

## Done when
Every row passes. `LanguageSurfaceTests.Kinds[Await]` is `Executable`. `PrintedSubsetVmTests`
classifies every `CompileReject` kind and none is `Live`. CORE §3.3 and the gap line describe
blocking `Await`.

## SHIP if / NOT SHIP if
SHIP when the full suite is green and the diff is the await arm, the type rule, the representation
arm, the checker binding, the CORE/gap doc lines, the ADRs, and the tests.
NOT SHIP when the engine becomes async, when a state machine or scheduler appears, when `Await`
lowers to `SuspendNode`, or when the audit is scoped so narrowly that a live-printed reject passes.

## Tests
`dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/LanguageVmTests/*'`
and `'/*/*/PrintedSubsetVmTests/*'`, then the full suite.

## Hand-edit
`EmitAwait` is one method; `AwaitPattern` is one file; the audit is one table plus four assertions.
Scot can read the blocking arm without following a driver.

## Needs Scot
Approved 2026-10-16: both ADRs accepted; Slice 2 (`AA1b`) admitted.

## Log
| Date | Who | Mill | SHA | Verdict / event | Findings |
|------|-----|------|-----|-----------------|----------|
| 2026-10-16 | async | spike | `88c5044c` | pushed (Slice 1) | `Await` executes by blocking; `LanguageVmTests` 15 new cases green |
| 2026-10-16 | async | OpenCode | — | audit + acceptance added | `PrintedSubsetVmTests` green; ADRs accepted; `Forms` scoped to `CompileReject` kinds (recorded in part 2) |
