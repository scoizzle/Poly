# TASK IS0d - a suspend outside the root body is a compile rejection

Status: planned. Branch `slice/IS0d`. Lane C (`vm`). Issue #259.
Plan: [IS-README](../IS-README.md).

## Why

`EmitPcDispatch` is emitted exactly once, in the compilation-unit body (`Poly/Interpretation/Vm/Abi/AbiEmitter.cs:168`; definition `AbiEmitter.Context.cs:50-74`). `EmitSuspendNode` (`AbiEmitter.Statements.cs:355`) registers a resume label for **every** suspend (`:358`), writes `state.ProgramCounter`, and jumps to `ctx.ExitLabel`. `CompileFunctionBody` (`AbiEmitter.Invoke.cs:500-566`) emits no dispatch and restores no `ProgramCounter`, and the lambda bridge has no resume path at all.

So a suspend inside a compiled function body registers a label nothing switches on. On resume the root dispatches `ProgramCounter`, misses every label in the nested body, and takes `default` → `ExitLabel`: the call returns early with a half-applied body. That is a fail-open — a shape the emitter cannot continue was compiled anyway — which is exactly what `AGENTS.md` §C# surface forbids.

## Scope

Decide the supported shape, then make the other shape impossible:

**Suspend is root-only.** `EmitSuspendNode` throws a compile rejection naming the node when it is not emitting into the compilation-unit body. `CompileFunctionBody` and the lambda bridge inherit the rejection without their own check.

- Add one emission-scope fact to `Context` (e.g. `IsRootBody`, true while emitting the compilation-unit body, cleared around `CompileFunctionBody` and the lambda bridge). Do not thread a boolean through every emit signature.
- The rejection matches `RejectCompile`'s shape (`AbiEmitter.cs:313-314`): `InvalidOperationException`, message names the kind and why.
- Do **not** implement resumable nested frames. That is a feature (per-nesting `FramePos` save/restore, dispatch in nested bodies, bridge treatment), not a defect fix; if a product path needs it, it starts as its own card.

Evidence that root-only is safe today: no production code constructs `SuspendNode` — `grep -rn "new SuspendNode"` hits only `Poly.Tests`. Record that in the Log at implementation time; if it stops being true, stop and report.

## Files

- `Poly/Interpretation/Vm/Abi/AbiEmitter.Context.cs` — the scope fact.
- `Poly/Interpretation/Vm/Abi/AbiEmitter.Statements.cs` — `EmitSuspendNode`.
- `Poly/Interpretation/Vm/Abi/AbiEmitter.cs` — set/clear around the compilation-unit body.
- `Poly/Interpretation/Vm/Abi/AbiEmitter.Invoke.cs` — clear around `CompileFunctionBody`.
- `Poly/Interpretation/Vm/Abi/AbiEmitter.LambdaBridge.cs` — clear around a bridged body.
- `Poly.Tests/Interpretation/SuspendResumeVmTests.cs` — root behaviour stays green; new rejection test.

## Shape matrix

| Input | Before | After | Test that proves it |
|---|---|---|---|
| root-level `SuspendNode` | executes, resumes at the next statement | unchanged | `SuspendAtRoot_ResumesAfterTheSuspend` (existing) |
| `SuspendNode` inside an AST method body | compiles; resume returns early | `Interpreter.Compile` throws, message names the node | `SuspendInFunctionBody_IsACompileRejection` |
| `SuspendNode` inside a lambda body | compiles; silently never resumes | throws | `SuspendInLambda_IsACompileRejection` |

## Done when

- The two rejection tests fail before the change and pass after.
- Root suspend/resume is unchanged (`SuspendResumeVmTests`, `VmDebuggerTests` green).
- The producer inventory (no production `SuspendNode`) is in the Log.
- Full suite green.

## SHIP if / NOT SHIP if

SHIP: root-only is enforced at compile time and the message says which shape is unsupported.
NOT SHIP: the rejection is a runtime no-op; a nested suspend still compiles; resumable nested frames were attempted in this slice.

## Tests

```
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/SuspendResumeVmTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/VmDebuggerTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/DirectVmAbiEmitterTests/*'
```
Then the full suite.

## Hand-edit

One scope flag is set in two places, and the reject site reads like every other rejection in the file.

## Needs Scot

The issue's fork is real: root-only rejection (this card) versus resumable nested frames (a feature). Recommended and proceeding: root-only now — it removes a fail-open with no behaviour change for any program that works today. If a product path needs nested suspend, it is a new card with a repro, not a widening of this one.

## Log

| Date | Who | Mill | SHA | Verdict / event | Findings |
|------|-----|------|-----|-----------------|----------|
