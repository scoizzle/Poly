# TASK AA1b - An `IsAsync` AST method returns a completed `Task`

Status: merged #206 `3b29937b` 2026-10-17
Branch `slice/async-methods`. PR #206. Lane async. Review see PR.
Plan card: [`docs/decisions/2026-10-16-async-interpreter-execution.md`](../../../decisions/2026-10-16-async-interpreter-execution.md) Slice 2.

## Scope
An AST method with `IsAsync` true whose body yields `T` produces a completed `Task<T>` at its
return; `async Task` produces `Task.CompletedTask`. The VM honors `IsAsync` at method-return
lowering, so a caller's blocking `Await` composes with a sim async method. A body containing
`Await` in a method that is not `IsAsync`, or whose return type is not `Task` / `Task<T>`, is
rejected, as C# rejects it. A non-`IsAsync` `Task<T>`-returning method is left alone.

## Files
- `Poly/Interpretation/Vm/DirectVmAbiEmitter.Invoke.cs` — the `IsAsync` wrap at method-return
  lowering, and the reject for an `Await` body in a method that is not `IsAsync`-with-`Task`
  (a VM compile reject, before the body compiles). The resolved return type comes from
  `AstMethodDefinition.MemberTypeDefinition.GetRuntimeType()`; `CompileFunctionBody` gains the
  async facts (its lambda caller stays unwrapped).
- `Poly/Interpretation/Vm/DirectVmAbiEmitter.Statements.cs` — `EmitReturn` wraps the yielded value.
- `Poly/Interpretation/Vm/DirectVmAbiEmitter.AbiCtx.cs` — `AsyncReturn`.
- `Poly.Tests/Interpretation/AsyncMethodVmTests.cs` (new).
- `docs/CORE.md` §3.3.

## Shape matrix
| Input kind | Before | After | Test that proves it |
|---|---|---|---|
| `await` of an AST `async Task<long>` method | body's value was returned raw | yields the body's `long` | `Await_AstAsyncMethod_ReturnsResult` |
| `await` of two chained AST async methods | not composed | yields the inner result | `Await_TwoChainedAsyncMethods` |
| `await` of an AST `async Task` method | not composed | completes as void | `Await_AstAsyncTask_IsVoid` |
| `await` of an AST `async Task<double>` method | n/a | un-bits the ring double | `Await_AstAsyncMethod_Double_UnbitsTheRingValue` |
| `await` of an AST `IsAsync: false` `Task<T>` method | left alone | not wrapped | `NonAsyncTaskReturningMethod_IsNotWrapped` |
| a non-`IsAsync` `long` method | unchanged | unchanged | `NonAsyncMethod_IsNotWrapped` |
| a `Task<T>` body that yields a task, or falls through | wrapped the task handle as `T` | rejected | `AsyncTaskOfLong_FallThroughWithoutValue_IsRejected` |
| `Await` in a non-`IsAsync` body | accepted | rejected | `AwaitInNonAsyncMethod_IsRejected` |

## Done when
Every row passes, `IsAsync` is read by the VM and not only the printer, and the full suite is green.

## SHIP if / NOT SHIP if
SHIP when the wrap lives at method-return lowering, the control case (non-`IsAsync`) is unchanged,
and the reject rule matches C#.
NOT SHIP when the wrap is a print-only marker, when a second interpreter appears, or when a wrong
`T` is inferred silently.

## Tests
`dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/AsyncMethodVmTests/*'`,
then the full suite.

## Hand-edit
The wrap is one branch at the body flush plus one analysis rule. Scot can delete the branch and an
`IsAsync` method stops producing a task.

## Needs Scot
Admitted 2026-10-16 (scope answer: Slice 1 + Slice 2). Stop if the body result type for a generic
`Task<T>` method needs a decision the ADR does not make.

## Log
| Date | Who | Mill | SHA | Verdict / event | Findings |
|------|-----|------|-----|-----------------|----------|
| 2026-10-16 | queue | plan | — | planned | none |
| 2026-10-17 | async | OpenCode | — | pushed | `IsAsync` wrap at method-return lowering; the `Await`-outside-async shape is a VM compile reject; `AsyncMethodVmTests` 5/5, full suite 3939/3939 |
| 2026-10-17 | async | OpenCode | `3b29937b` | SHIP | merged #206; review found and fixed: the body reject now rethrows instead of degrading to `InvokeNamed`; `Task<T>` bodies that yield a task or fall through are rejected; the double/float ring un-bitting passes the yielding node. `AsyncMethodVmTests` 8/8, full suite 3942/3942 |
