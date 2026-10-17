# ADR: Async interpreter execution (part 2 of the async/await spike)

**Date:** 2026-10-16
**Status:** Proposed
**Author:** design spike
**Deciders:** product owner

**Home:** `docs/decisions/2026-10-16-async-interpreter-execution.md`

**Part 1:** [`2026-10-15-async-await-in-the-interpreter.md`](2026-10-15-async-await-in-the-interpreter.md). Part 2 **revises** part 1's handling of a `Task`-typed `Await` operand (part 1 §"What simulatable means at an await", §"The driver", PR 2). It keeps part 1's law (printed ⊆ simulatable, scope (a)) and its resume spine for `SuspendNode`. It removes the await-driven suspend, `ResumeWith`, and the `await` allow-lists.

`docs/decisions/` records why. `docs/plans/` records execution. This spike implements nothing. It does not open a plan and does not admit work to [`docs/plans/poly-eng/board.md`](../plans/poly-eng/board.md). Follow-ups below stay evidence until a human adds them to the board.

**Parents:** [`2026-06-08-vm-as-canonical-semantics.md`](2026-06-08-vm-as-canonical-semantics.md), [`2026-06-08-domain-lowering-boundary.md`](2026-06-08-domain-lowering-boundary.md), [`2026-09-04-frozen-core-pipeline.md`](2026-09-04-frozen-core-pipeline.md), [`2026-09-05-lowered-module-is-domain-meaning.md`](2026-09-05-lowered-module-is-domain-meaning.md).

**Also binds:** [`docs/CORE.md`](../CORE.md) §3.3 and §4.

Evidence is the base commit `b2da5b13` (the head of the PR 193 branch `docs/async-await-interpreter`), which this branch builds on. The probe is a throwaway console project under the session temp directory that referenced `Poly/Poly.csproj` at that commit. It is not in the tree.

---

## Overview

Part 2 asks whether the interpreter's execution should be async itself, so an `Await` is handled internally and is a normal execution route.

**Finding: the engine should not become async — and does not need to.** Expression trees cannot contain `await`, so an async engine would be a second interpreter. But `await` does not need an async engine. It can be given a faithful, normal, synchronous execution route:

```csharp
// Await(operand)  where operand is Task<T>
operand.GetAwaiter().GetResult()
```

That expression parks the statement until the task completes, then yields its result — or throws the task's original exception. That **is** what `await` means: "stop here until the task I hold is done, then give me its result, or blow up with its exception." In a single-threaded VM that owns its thread and installs no `SynchronizationContext`, "park this statement" and "block this thread" are the same thing as far as the program can observe.

So `await` gets one meaning, the LINQ checker already compiles that meaning (`LinqExpressionGenerator.ControlFlow.cs:364-367`), and the whole await side of part 1 collapses:

- No `DriverBoundary`, no `ResumeWith`, no `VmState.ResumeValue`.
- No `await` shape allow-list. There is no suspend node inside `await`, so `await` inside `try` / `using`, inside a stored lambda, and inside a nested call all work — the shapes part 1 deferred.
- The resume spine stays, but only for `SuspendNode`. Breakpoints are unchanged.
- `await` is a **module-level** construct, not a host-only one. The AST already defines methods with bodies and an `IsAsync` flag, and the VM already runs those bodies. Part 1 treated `await` as living in `Program.cs` and had to build a driver boundary for the host shape; the AST's own async methods are the thing that was lost (see §"Async method definitions in the AST").

---

## What `await` means, and what `GetResult` does

| Task state | C# `await` | `GetAwaiter().GetResult()` |
|------------|------------|----------------------------|
| Completed successfully | Yields the result | Returns the result |
| Completed with an exception | Throws the **first** exception, unwrapped | Throws the first exception via `ExceptionDispatchInfo` (also unwrapped) |
| Canceled | Throws `TaskCanceledException` | Throws `TaskCanceledException` |
| Incomplete | Method suspends; resumes later with the result or the fault | Blocks the thread until the same outcome |
| — | — | `.Result` / `.Wait()` are **not** used: they wrap in `AggregateException` |

The result and fault rows match exactly. The incomplete row differs only in mechanism, and only in a way the program cannot see: in the VM there is no other work to run while parked, so "suspend and be resumed" and "block until done" produce the same next statement, the same value, and the same exception. The `await` result is `T` for `Task<T>`, `void` for `Task`, and the awaiter's result for any other awaitable.

This is the "already completed" behavior C# documents, generalized: when nothing else can run, awaiting a task is waiting for it.

---

## Why blocking is faithful in this VM

- **One thread, no other work.** The compiled delegate owns its thread for the whole run. Suspending would hand control to a driver that has nothing else to do but resume. Blocking is the same schedule with less machinery.
- **No synchronization context.** The VM installs none. A task whose continuations run on `TaskScheduler.Default` (the pool) cannot deadlock against the blocked thread. This is the one guardrail (risks below).
- **Order is preserved, and nothing races.** Blocking is *stricter* than suspending: the statement runs, then the next statement runs after completion. There is no interleaving to order, so no scheduler and no `WhenAll` / `WhenAny` meaning is needed.
- **Faults match.** `GetAwaiter().GetResult()` rethrows the original exception, as `await` does. Using `.Result` or `.Wait()` would introduce `AggregateException` and diverge from `await`; the design names `GetAwaiter().GetResult()` specifically.
- **The common case is free.** An already-completed task returns immediately, which is the shape of a synchronous host job.

---

## The engine is still not made async

The delegate contract is `Action<VmState>` (`VmProgram.cs:27-38`), produced by `Lambda<Action<VmState>>(...).Compile()` (`DirectVmAbiEmitter.cs:173-174`). `System.Linq.Expressions` has no `await` node, and an async lambda cannot be converted to an expression tree. The probe confirms it: `Expression<Func<Task<int>>> e = async () => await Task.FromResult(1);` fails with **`error CS1989: Async lambda expressions cannot be converted to expression trees`**. There is no opcode loop to interleave an `await` between steps.

So "the engine is async" would require an async state machine or an async tree-walker — a second interpreter, which the frozen core and `docs/CORE.md:158` forbid. The blocking realization avoids that entirely: the engine stays synchronous and the `Await` node becomes an ordinary compiled expression.

| Probe program | Observation |
|---------------|-------------|
| `Await(Constant(1L))` | `InvalidOperationException: VM compile rejected: Await is not executable on the VM.` |
| `Invoke(Member(host, Completed))`, `Completed()` returns `Task<long>` | Result kind `Value`, object `Task\`1`, `.Result` = `99`. The VM already invokes a `Task`-returning method and puts the real `Task` on the heap. |
| `Await(that same Invoke)` | Compile rejected, same message. |
| Raw expression tree `operand.GetAwaiter().GetResult()` on a yielding async method returning `Task<long>` | `42`. |
| `typeof(VmProgram).GetProperty("Delegate").PropertyType` | `System.Action\`1[Poly.Interpretation.Vm.VmState]`. |
| `Expression.Call(taskOfLong, "GetAwaiter", Type.EmptyTypes)` (string overload) | Throws: "More than one method 'GetAwaiter' … is compatible." `Task<T>` also exposes the inherited `Task.GetAwaiter`. |
| Two `VmState`s over one program | `same Heap? False`, `same Stack? False`; `handle 1` allocated in A reads `hello-from-A` in A and `<null>` in B. |

---

## The blocking arm

`DirectVmAbiEmitter.CompileNodeInner` and `CompileValue` replace both `Await => RejectCompile(...)` arms (`DirectVmAbiEmitter.cs:239`, `:608`) with `EmitAwait`.

```text
EmitAwait(await, ctx):
  operand = CompileValue(await.Operand, ctx)          // the Task / Task<T> / awaitable
  operandType = resolved CLR type of await.Operand    // from analysis
  if operandType is not awaitable: RejectCompile("Await operand <type> has no GetAwaiter.")
  getAwaiter = operandType.GetMethod("GetAwaiter")    // bound MethodInfo, not the string overload
  awaiter    = Call(operand, getAwaiter)
  getResult  = awaiter.Type.GetMethod("GetResult")    // void for Task, T for Task<T>
  result     = Call(awaiter, getResult)               // spill to ring; void spills nothing
```

Details:

- **Bind a specific `MethodInfo`.** The `Expression.Call(instance, "GetAwaiter", Type.EmptyTypes)` string overload is ambiguous for `Task<T>` (probe) because `Task<T>` also inherits `Task.GetAwaiter`. `EmitAwait` resolves `GetAwaiter` on the operand's type the same way `Ref` resolves known members. The existing `LinqExpressionGenerator.CompileAwait` uses the string overload; the implementing slice should check whether that path is reachable with a generic task and fix it to bind the method too.
- **Type rule** (`TypeAndMemberResolutionPass`): `Task<T>` → `T`; `Task` → `void`; another awaitable → its awaiter's result type. An operand with no `GetAwaiter` is a **shape reject**, as C# rejects `await` of a non-awaitable. This corrects part 1's `HostCompleted` row, which treated `Await` of a `T`-returning call as the identity `T`; that is not valid C# and the node should not be produced, so the VM rejects it rather than passing it through.
- **Value representation** follows the result type (`ValueRepresentationAnalyzer.Classify` gains the `Await` arm instead of falling through to `Unknown`, `ValueRepresentationPass.cs:117`).
- **Print** is unchanged: `CSharpWriter` already emits `await` on the operand.

`LinqExpressionGenerator.CompileAwait` already compiles `GetAwaiter().GetResult()` and therefore already agrees with this design. Part 1 wanted to change the checker to follow a VM suspend table; the blocking realization instead makes the VM and the checker the same single meaning. No consumer-specific flag, no second language.

---

## What this removes from part 1

| Part 1 element | Part 2 |
|----------------|--------|
| `DriverBoundary` for `Task` / `Task<T>` | Deleted. `Await` blocks and yields the result. |
| `ResumeWith`, `VmState.ResumeValue`, `HasResumeValue`, `Reset` change | Deleted. There is no resume value for an await. |
| `AwaitMetadata.Completion` | Not needed. The type rule and the operand type are enough. |
| Allow-list row "Await of Task or Task<T>" | Deleted. It executes. |
| Allow-list rows "Await inside TryCatchFinally or UsingStatement" and "Await inside a non-inlined lambda / AST method body" | Deleted. With no suspend node inside `await`, there is no resume label to place, no `Control cannot enter a try block`, and no callee `finally` unwinding. `await` composes anywhere an expression is allowed. |
| Part 1 slice 2 (spill/reload) and slice 3 (nested re-entry), as they relate to `await` | Not needed for `await`. |
| Resume spine (`ProgramCounter`, `FramePos`, resume label) | **Kept** for `SuspendNode` and breakpoints. Part 1's spine analysis is still the plan for that. |

The law is unchanged and better satisfied: every construct the printer emits as live `await` has a defined execution, and it is a plain expression.

---

## Async method definitions in the AST

The AST can already define an async method. What is missing is the meaning of executing one. This is the seam Part 1 skipped.

| Fact | Where |
|------|-------|
| `MethodDefinitionNode` carries `IsAsync` **and** a `Body`. | `Poly/Ast/Nodes/TypeDefinitions/MethodDefinitionNode.cs:12-22` |
| The printer emits `async` from `IsAsync`, then the declared return type. | `CSharpWriter.cs:699-721`; `MethodDefinitionNode.ToString()` |
| The reader folds `async` into `IsAsync`. | `CSharpReader.cs:342-357` |
| The VM compiles and runs AST method bodies. | `CompileFunctionBody` `DirectVmAbiEmitter.Invoke.cs:498-573`; `InvokeAstFunction` `:595-624` |
| Domain operations are AST methods with bodies. | `DomainProgramProjection.ToSyntax` → `DomainToCSharpExporter.BuildTypeDefsForEntity` |
| Nothing reads `IsAsync` except the printer/reader. Only `MinimalApiGenerator.SeedAsync` sets it. | grep `IsAsync`; `MinimalApiGenerator.cs:1088` |
| An `Await` in any body is `RejectCompile`. | `DirectVmAbiEmitter.cs:239`, `:608` |

So "allowing async method definitions in the AST" is not a syntax change — the `IsAsync` flag and the body already exist, and the VM already executes bodies. It is three things:

1. **A VM meaning for `Await` in a body.** Part 2's blocking arm gives it. Once `EmitAwait` exists, a method body that awaits compiles like any other body.
2. **What an `IsAsync` method produces in the sim.** In print, `async Task<T> Foo() { return x; }` returns a `Task<T>`, and `return x` means "the result is `x`." For the sim to agree, an `IsAsync` method whose body yields `T` must produce a **completed** `Task<T>` at its return (`Task.FromResult<T>`), and `Task.CompletedTask` for `async Task`. Then `Await` is uniform: every operand is a real (host) or sim-completed `Task`, and `GetAwaiter().GetResult()` yields the result. A non-`async` method that returns a `Task<T>` it did not await is left alone — `async` is exactly what changes `return x` from "here is a task" to "here is the result."
   - This makes `IsAsync` a VM-read fact at method-return lowering, not print-only. It revises Part 1's key decision 7 ("`IsAsync` is print; the sim follows `Await` nodes").
   - Root result: running an `async Task<T>` operation as a program yields a `Task<T>`; the harness unwraps it. Running its body yields `T`.
3. **Composition.** A method that awaits another sim async method sees a completed `Task<T>`, blocks trivially, and returns its own completed `Task<T>`. A method that awaits a host `Task<T>` blocks on the real task. `await` inside `try` and nested calls work because there is no suspend node.

**Fidelity notes.**

- A throwing body: C# captures the exception into the returned, faulted `Task<T>`; the sim throws it synchronously from the call. The caller's blocking `Await` (or the root unwrap) rethrows it, so the same exception reaches the same place once awaited. The difference shows only for fire-and-forget, which a module body does not do. If exact fault-task shape is ever required, the method-return lowering can wrap the body in try/catch and produce `Task.FromException`.
- The sim allocates a `Task` per async method call. It already allocates exceptions and other CLR objects, so this is the same class of cost.
- The printer needs `IsAsync: true` and a `Task` / `Task<T>` return type for a body that contains `Await`; C# requires it. A lambda whose body contains `Await` is auto-`async` in the writer (`CSharpWriter.cs:980-996`); a method relies on the flag. Analysis should reject a body that contains `Await` in a method that is not `IsAsync` (or that does not declare a `Task` return type), the way C# does.

**Where async methods come from.** Structurally the AST accepts them today. Nothing in `DomainModeling` sets `IsAsync`, and no lowering pass builds an `Await`. A domain operation becomes async only when a later slice lets lowering mark a body async and emit `Await` (for example, a host job that returns `Task<T>`). That slice depends on this meaning; it does not create it. The host generator's `SeedAsync` stays a host artifact (`MinimalApiGenerator.cs:1080-1088`).

**Reframing Part 1.** `Await` is a module-level language construct, not a host-only node. Part 1 put awaits in `Program.cs`, scoped them out, and then had to invent a driver boundary for the host shape; that is where it got lost. The AST defining async methods with bodies — and the VM already running those bodies — means the module *is* where `await` belongs, and the VM executes it. Part 1's enforcement test `ModuleBodies_DoNotContainAllowListedAwaits` encoded the host-only assumption; it becomes "every `Await` in a module body executes or is a shape reject," with no allow-list.

---

## Costs and risks

| Severity | Risk | Mitigation |
|----------|------|------------|
| High | A captured `SynchronizationContext` on the VM thread makes `GetResult()` deadlock on a task that resumes on that context. | The VM installs no context; document that the compiled delegate must run where `SynchronizationContext.Current` is null (MCP, tests, CLI). A host job that needs a context must `ConfigureAwait(false)` on continuations it owns. |
| High | The sim now **runs** the awaited operation, including real I/O if the task does I/O. Two runs could differ if the operation is not deterministic. | This is the same class as any external call the VM already makes; determinism is injected at the `DomainInstanceHostJobs` seam. A deterministic job returns a completed `Task<T>` (`Task.FromResult`), and blocking returns its value. State the choice: the VM executes external work; it does not model a boundary. |
| Medium | A never-completing task blocks the VM (and the harness thread) forever. | Same as real C#: the method never completes. Cancellation and timeouts are the host's concern; the VM has no scheduler. MCP can run an invocation on a pooled thread with a timeout if that becomes necessary. |
| Medium | No pause-at-await for a debugger; the await does not expose a suspended state. | `SuspendNode` keeps that capability. Pause-at-await, if ever wanted, is a spine feature layered on top, not the language meaning. |
| Medium | `GetAwaiter` binding is done with the string overload and is ambiguous for `Task<T>`. | Bind the resolved `MethodInfo`; check `LinqExpressionGenerator.CompileAwait` for the same bug. |
| Low | `.Result` / `.Wait()` sneaks in and wraps faults in `AggregateException`. | Name `GetAwaiter().GetResult()` in the emitter and test that a faulted task throws the original exception type. |
| Low | `ValueTask`, `ConfigureAwait`, `WhenAll` / `WhenAny`, `await using`, `await foreach`, `CancellationToken` arrive later. | Type-rule rows / new nodes, unchanged from part 1's out-of-scope table. Blocking does not block these; it just does not add them. |

---

## Determinism, corrected

Part 1 (and an earlier draft of this document) treated a `Task` operand as a boundary whose value must be driver-supplied to keep the sim deterministic. That is too strong. The VM already executes external calls, and a *synchronous* external call can be just as non-deterministic as an asynchronous one. Determinism comes from the host-job seam returning deterministic results, not from refusing to run `Task`s. Blocking preserves program order and introduces no scheduling, so it cannot introduce a race. If an operator must be deterministic, the job returns a completed task with a fixed value; if it is genuinely non-deterministic, the sync equivalent is too.

---

## Alternatives

| | (A) Async engine (state machine / async walk) | (B) Blocking `GetAwaiter().GetResult()` | (C) Suspend + `ResumeWith` (part 1) | (D) Async façade |
|--|--|--|--|--|
| What it makes easy | The engine can pause on a real task | One meaning, no spine for `await`, composes in `try` / lambdas / nested calls | Pause/step at an await; a driver can substitute a value | `await`-shaped entry point for callers |
| What it costs | A second interpreter / parallel IR | Blocks the VM thread; runs the awaited operation | `ResumeWith`, `ResumeValue`, allow-lists, spine fix, slices 2–3 | No new meaning; moves the pump inside `Interpreter` |
| Fidelity to `await` | Closest to C# scheduling | Result and fault match; incomplete case is observationally identical in a single-threaded VM | Models the result, not the task | Depends on what it awaits |
| Frozen core | Forbidden (second interpreter) | `Await` is an ordinary compiled node | Extra state on `VmState` | Consumer surface only |
| Verdict | Reject | **Use this** | Optional later, only for pause/step or value substitution | Unnecessary |

A fifth option — an async call that spawns its own execution context wrapped in a real `Task` — is considered below and rejected for now.

---

## Alternative: a spawned execution context per async call

A different route to non-blocking `await`: an async call does not run the callee inline. It allocates a fresh execution context, re-enters the same `VmProgram` at that function with the arguments, and wraps the run in a real CLR `Task`. The caller's `await` then "works automatically," because the operand is a genuine `Task` and the blocking `GetAwaiter().GetResult()` consumes it. Each call would get its own activation, leaving the caller's frame untouched.

It is appealing in principle. The runtime does not support the simple form:

| Blocker | Evidence |
|---------|----------|
| **Memory is per context.** `VmState` constructs its own `Stack` and `Heap` (`VmState.cs:274`, `:298`); `Execute` makes a new `VmState` (`Interpreter.cs:132`). Reference values — `this` and every entity — are handles into the caller's heap. A child context from another `Execute` cannot see or mutate the caller's objects. | Probe #7: same program, two contexts → `same Heap? False`; `handle 1` allocated in A reads `hello-from-A` in A and `<null>` in B. |
| **Functions are not PC-addressable.** The root `ProgramCounter` dispatch is the `SuspendNode` resume path only. AST methods and lambdas are separate `Action<VmState>` delegates in an embedded function table, invoked by `InvokeHeapClosure` / `InvokeAstFunction` (`DirectVmAbiEmitter.Invoke.cs:406`, `:417`, `:595`). "Same `Program` + PC set to the function" is not the addressing model; `VmProgram.Functions` is not even populated by `Emit`. | `DirectVmAbiEmitter.cs:173-174` builds one root delegate; the function table is a `Constant` captured in it. |
| **A real `Task` only pays off with a scheduler, and a scheduler breaks determinism.** Thread-pool scheduling runs parent and child concurrently over a shared heap → data races and non-deterministic interleaving. The VM does not race; one `VmState` is one continuation. If instead the child runs inline when awaited, it is the blocking arm with extra allocation — no observable gain. | `2026-06-08-vm-as-canonical-semantics.md`; Part 1 §Concurrency. |
| **Independent contexts do not preserve locals.** Part 1 needed spill/reload because a delegate that returns at a suspend loses its CLR `_regN` locals. A new context does not fix that for the callee: when *its* delegate returns at a suspend, *its* `_regN` are gone too. Locals must still be spilled into the context, hoisted into `VmState`, or the body must be a real CLR async state machine (a second engine). | Part 1 §Risks; Part 1 probe (the `1 + 10` / loop counter loss). |
| **Cross-context results need rehoming.** The child's result is a handle in the child heap; returning it to the parent needs a copy or a shared heap. | Probe #7. |

The useful kernel is to **separate the activation from the memory.** Today `VmState` is both. Splitting it — a shared `Heap` / arena plus a per-call `Activation` (frame, PC, registers, status) — is what would let an async call be its own context *and* leave the caller's frame intact. That is Part 1's spine scoped per call; it is a scheduler-shaped change, and it still needs the register spill for the activation's own locals. It is the long-term door to true suspension and parallel awaits, not the first slice.

Recommendation: do not adopt the spawned-context / real-`Task` route now. Part 2's blocking `Await` gives the same result and fault semantics deterministically, with no scheduler, no context split, and no spill. Revisit the memory/activation split only when a real need for suspension or parallel awaits appears (`WhenAll` / `WhenAny`); the first step then is the split, not `Execute` with a PC.

---

## Frozen-core mapping

| Frozen rule | How this design sits on it |
|-------------|----------------------------|
| `Node` / `NodeId` symbolic primary; no parallel product IR | `Await` stays the node; the emitter compiles it. No async IR. |
| Shipped ⊆ complete generic Syntax; no second interpreter | No async engine. The blocking call is a known node compiled by the existing emitter. |
| VM executes Syntax; no domain opcodes; external work is an ordinary call | `operand.GetAwaiter().GetResult()` is an `Invoke`-class external call (`2026-06-08-domain-lowering-boundary.md`). |
| One analyze → `session.Lower` → artifact set | Unchanged. |
| New meaning: lower, analyze, or replace — not an emitter patch | The change compiles a node the emitter already switches on and rejects; the same class as `SuspendNode`. Analysis supplies the resolved operand type. |
| Seams where multiplicity is known | One statement meaning. No scheduler, no async engine for an imaginary second consumer. |
| CORE updates with a mechanism change | This document changes no mechanism, so it does not edit `docs/CORE.md`. The implementing slice updates CORE §3.3's `Await` note and the stale `docs/decisions/vm-gap-analysis.md:82` line in that change. |

---

## Open questions

1. Confirm: `Await` executes by blocking — `operand.GetAwaiter().GetResult()` — and this supersedes part 1's `DriverBoundary` / `ResumeWith` for `await`.
2. Confirm: the VM may run the awaited operation, including real I/O, with determinism supplied by the host-job seam rather than by a driver boundary.
3. Confirm: the VM runs without a captured `SynchronizationContext` (the deadlock guard).
4. Confirm: pause-at-await is not required; `SuspendNode` keeps the resume spine.
5. Confirm the type rule (`Task<T>` → `T`, `Task` → void, other awaitables via their awaiter) and that a non-awaitable operand is a shape reject (correcting part 1's `HostCompleted` identity row).
6. `ValueTask`, `ConfigureAwait`, `WhenAll` / `WhenAny`, `await using`, `await foreach`, `CancellationToken` stay out of the first slice (Part 1's table).
7. Confirm async AST methods: an `IsAsync` method whose body contains `Await` executes, its sim value is a completed `Task<T>` / `Task`, and the VM honors `IsAsync` at return. A body containing `Await` in a non-`IsAsync` method (or without a `Task` return type) is rejected.
8. Confirm `Await` is a module-level construct. Part 1's assumption that module bodies contain no `Await` — and the `ModuleBodies_DoNotContainAllowListedAwaits` test that encoded it — is replaced by "a module body's `Await` executes or is a shape reject."
9. Confirm we do not adopt the spawned-context / real-`Task` route now. If suspension or parallel awaits is wanted later, the first step is a memory/activation split (shared heap, per-call activation), not `Execute` with a PC.

---

## References

- Part 1: [`2026-10-15-async-await-in-the-interpreter.md`](2026-10-15-async-await-in-the-interpreter.md).
- `Poly/Interpretation/Vm/DirectVmAbiEmitter.cs:173-174`, `:239`, `:608`. `VmProgram.cs:27-38`.
- `Poly/Interpretation/LinqExpressions/LinqExpressionGenerator.ControlFlow.cs:364-367` existing `GetAwaiter().GetResult()`.
- `Poly/Interpretation/Analysis/Semantics/TypeAndMemberResolutionPass.cs:124`; `ValueRepresentationPass.cs:117`.
- `Poly/Interpretation/Vm/VmState.cs`, `ExecutionResult.cs`, `Interpreter.cs` (unchanged by this design except the optional CORE note).
- `docs/CORE.md:109-117`, `:158`. `docs/decisions/2026-06-08-domain-lowering-boundary.md`. `docs/decisions/vm-gap-analysis.md:82`.
- Probe: `async-probe2` (console, `Poly/Poly.csproj` reference) and `await-expr-check` (CS1989), under the session temp directory at `b2da5b13`. Not in the tree.

---

## Key decisions

1. `Await` has one meaning: block on the operand until it completes, then yield its result or throw its exception — `operand.GetAwaiter().GetResult()`.
2. `GetAwaiter().GetResult()` matches `await` on result, fault (unwrapped), and cancel; `.Result` / `.Wait()` are not used.
3. The interpreter's execution engine is **not** made async. The unit is a synchronous `Action<VmState>`; expression trees cannot await (CS1989); an async engine is a second interpreter.
4. This supersedes part 1's `DriverBoundary`, `ResumeWith`, `VmState.ResumeValue`, and the `await` allow-lists. The resume spine remains for `SuspendNode`.
5. No scheduler. One statement runs to completion, then the next.
6. The sim executes the awaited operation; determinism is injected by the host-job seam, not by a driver boundary. The VM must run without a `SynchronizationContext`.
7. `Await` of a non-awaitable operand is a shape reject; the type rule gives `Task<T>` → `T`, `Task` → void.
8. No `docs/CORE.md` edit in this document; the implementing slice updates §3.3 and the stale gap line.
9. The AST may define async methods. `MethodDefinitionNode.IsAsync` with a body containing `Await` is valid; the VM compiles and runs it like any other body. An `IsAsync` method whose body yields `T` produces a **completed** `Task<T>` at its return (and `Task.CompletedTask` for `async Task`), so callers await it uniformly. A non-`async` method returning a `Task<T>` is left alone.
10. `IsAsync` is a VM semantic fact at method-return lowering, not print-only. This revises Part 1's key decision 7. A body containing `Await` in a method that is not `IsAsync`, or that does not declare a `Task` return type, is rejected as C# rejects it.
11. `Await` is a module-level language construct, not host-only. Part 1's host-only framing is retired. Domain lowering gaining the ability to mark a body async and emit `Await` is a later slice that depends on this meaning; it does not create it.
12. A spawned execution context per async call (a fresh `VmState` + real `Task`) is rejected for now: memory is per-context, functions are not PC-addressable, a real `Task` needs a scheduler that would race, and new contexts do not preserve CLR locals. The kernel worth keeping is a shared-memory / per-activation split, and only when suspension or parallel awaits is actually needed.

---

## PR plan

Two small slices. No spine work. No board row is edited; a human admits each before an agent starts.

### Slice 1 — execute `Await` by blocking

**Title:** Execute `Await` by blocking on the operand.

**Files:**

- `Poly/Interpretation/Analysis/Semantics/TypeAndMemberResolutionPass.cs` (type rule at `:124`)
- `Poly/Interpretation/Analysis/Semantics/ValueRepresentationPass.cs` (`Await` arm)
- `Poly/Interpretation/Vm/DirectVmAbiEmitter.cs` (both `Await` arms → `EmitAwait`)
- `Poly/Interpretation/Vm/DirectVmAbiEmitter.Expressions.cs` (or a new partial) for `EmitAwait`
- `Poly/Interpretation/LinqExpressions/LinqExpressionGenerator.ControlFlow.cs` (confirm/bind `GetAwaiter`)
- `Poly.Tests/Interpretation/LanguageVmTests.cs` (`Await_CompileRejected` retargets to the shape rejects)
- `Poly.Tests/Interpretation/LanguageSurfaceTests.cs` if the inventory comment changes
- `docs/CORE.md` §3.3 `Await` note; `docs/decisions/vm-gap-analysis.md:82`

**Does:** `await` of a `Task<long>` returns its value; `await` of a faulted task throws the original exception; `await` of `Task` is void; `await` inside `try` and inside a stored lambda executes; a non-awaitable operand rejects with a reason. No suspension, no `ResumeWith`.

### Slice 2 — an `IsAsync` method returns a completed task

**Title:** Execute an AST async method: `IsAsync` methods yield a completed `Task`.

**Files:**

- `Poly/Interpretation/Vm/DirectVmAbiEmitter.Invoke.cs` (method-return lowering reads `IsAsync`)
- `Poly/Interpretation/Analysis/` validation: a body containing `Await` must be `IsAsync` with a `Task` / `Task<T>` return type
- Tests: an `await` of an AST async method composes; `await` of two chained async methods; `async Task` void; a non-`IsAsync` `Task`-returning method is not wrapped

**Does:** `IsAsync` method bodies containing `Await` execute and produce a completed `Task<T>` / `Task`; callers await them uniformly.

**Defers:** a lowering pass that marks a domain operation `IsAsync` and emits `Await` (a later slice; it depends on this meaning). `ValueTask`, `ConfigureAwait`, `WhenAll` / `WhenAny`, `await using`, `await foreach`, `CancellationToken`. Pause-at-await, if ever wanted, is a separate `SuspendNode`-spine feature.
