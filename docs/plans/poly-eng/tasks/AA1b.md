# TASK AA1b - An `IsAsync` AST method returns a completed `Task`

Status: planned
Branch `slice/async-methods`. PR (none yet). Lane async. Review see PR.
Plan card: [`docs/decisions/2026-10-16-async-interpreter-execution.md`](../../../decisions/2026-10-16-async-interpreter-execution.md) Slice 2.

## Scope
An AST method with `IsAsync` true whose body yields `T` produces a completed `Task<T>` at its
return; `async Task` produces `Task.CompletedTask`. The VM honors `IsAsync` at method-return
lowering, so a caller's blocking `Await` composes with a sim async method. A body containing
`Await` in a method that is not `IsAsync`, or whose return type is not `Task` / `Task<T>`, is
rejected, as C# rejects it. A non-`IsAsync` `Task<T>`-returning method is left alone.

## Files
- `Poly/Interpretation/Vm/DirectVmAbiEmitter.Invoke.cs` — method-return lowering reads `IsAsync`.
  The resolved return type comes from `AstMethodDefinition.MemberTypeDefinition.GetRuntimeType()`;
  `CompileFunctionBody` gains the async facts (its lambda caller stays unwrapped).
- `Poly/Interpretation/Analysis/Semantics/` — the "an `Await` body must be `IsAsync` + `Task`" rule.
- `Poly.Tests/Interpretation/AsyncMethodVmTests.cs` (new).
- `docs/CORE.md` §3.3 if the method-return mechanism changes.

## Shape matrix
| Input kind | Before | After | Test that proves it |
|---|---|---|---|
| `await` of an AST `async Task<long>` method | body has no meaning | yields the body's `long` | `Await_AstAsyncMethod_ReturnsResult` |
| `await` of two chained AST async methods | not composed | yields the inner result | `Await_TwoChainedAsyncMethods` |
| `await` of an AST `async Task` method | not composed | completes as void | `Await_AstAsyncTask_IsVoid` |
| `await` of an AST `IsAsync: false` `Task<T>` method | left alone | not wrapped | `NonAsyncTaskMethod_IsNotWrapped` |
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
