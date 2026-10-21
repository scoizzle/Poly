# VM Execution Engine (Interpretation/Vm/)

The VM execution engine compiles analyzed AST nodes directly into LINQ Expression
delegates for fast execution via `AbiEmitter`.

## File Map

| File | Purpose |
|------|---------|
| `Abi/AbiEmitter.cs` | **Primary compiler**: walks analyzed AST, emits `Action<VmState>` delegate |
| `Abi/AbiEmitter.Context.cs` | Compilation context (ring, frames, scopes) |
| `Abi/AbiEmitter.Expressions.cs` | Arithmetic, comparison, logic, members, types |
| `Abi/AbiEmitter.Statements.cs` | Assignment, blocks, branches, loops, EH, return |
| `Abi/AbiEmitter.Invoke.cs` | Invoke, lambda bodies, instance dispatch |
| `Abi/RingCodec.cs` | The ABI codec: value classification and both-direction conversion |
| `Abi/ValueRepresentation.cs` | Emit-time slot-kind classification from the effective node's type |
| `VmState.cs` | Per-execution state: stack, heap, frame, debug interrupt, loop limits |
| `VmProgram.cs` | Compiled program record (delegate + max local depth) |
| `VmValueStack.cs` | Pooled `long[]` stack backed by `ArrayPool<long>.Shared` |
| `VmManagedObjectHeap.cs` | Object heap for reference-type values with free-list recycling |
| `CompilationMode.cs` | Debug vs. optimized mode enum |
| `Ref.cs` (in `Poly/Introspection/CommonLanguageRuntime`) | Compile-time `MemberInfo` lookups (`Ref.Method`, `Ref<T>.Property`, …). Use these; do not add `typeof(T).GetMethod(...)` |
| `VmCall.cs` | The one call-frame protocol (2-word header, frame pointer, result slot) |

## Pipeline

```
Analyzed AST → AbiEmitter.Emit() → VmProgram → Interpreter.Execute() → ExecutionResult
```

No intermediate primitive flattening: the emitter walks the analyzed AST directly
and produces LINQ Expression trees that read/write `VmState` fields. All control
flow (loops, branches, try/catch/finally) uses native CLR Expression nodes.

## Ring Register Model

The emitter uses a ring register discipline during compilation: values flow through
`_r0.._rN` local variables allocated inline. Unlike the old primitive path, there
is no global pre-pass (`RingAllocator`) — ring assignment happens during the AST
walk. The result is equivalent: the CLR JIT can enregister ring locals.

## Exception Handling

Structured EH uses native CLR `Expression.TryCatchFinally` — no side tables or
handler dispatching. The emitter directly produces `TryCatch(Try(body), Catch(...),
Finally(body))` expression trees for `TryCatchFinally` nodes.

This is a secondary path — the direct AST lowering in `AbiEmitter.cs`
handles structured EH natively via `Expression.TryCatchFinally`.

## Runtime Types

### `RingCodec`
The single definition of the VM's long-slot ABI. Classifies a value as an inline
scalar, boolean, IEEE bit pattern, or heap handle (`KindFor`, `IsInline`,
`IsIeee`), and converts in both directions — compile-time `Expression` factories
(`FromClr`, `ToClr`, `Box`) and runtime helpers (`ToRing`, `Cast`, `TypeAs`,
`UnboxNullable`). Analysis passes use `IsInline` to predict the same
classification the emitter applies.

### `VmCall`
The one call-frame protocol. A frame is a 2-word header (previous frame pointer,
saved stack pointer) then argument slots; `FramePos` is the first argument slot,
a parameter is read at `FramePos + index`, and the callee result is written to
`slots[FramePos]`. `Invoke` runs a function in a new frame and restores FP,
closure, and SP even when the body throws.

### `VmValueStack`
Pooled `long[]` stack backed by `ArrayPool<long>.Shared`. Automatically grows
by doubling. All public members are aggressively inlined by the JIT. Key
operations: `Push`, `Pop`, `Drop`, `Reserve`, `Reset`. Return to pool on `Dispose()`.

### `VmManagedObjectHeap`
Contiguous object store indexed by handles. Handle 0 is the ABI null/falsy
sentinel (never allocated). Free-list recycles handles of set-to-null objects.
Grows by doubling. Methods: `Allocate`, `Get`, `Set`, `UnsafeGet`, `UnsafeSet`, `Clear`.

### `VmDebugger`
High-level step debugger:
- `Start()` — begin execution, pause at first statement
- `StepOver()` — advance one statement boundary
- `Continue()` — run to completion
- `CurrentNode` / `CurrentLocals` — inspect the current position

Uses a background thread with blocking hooks. Zero overhead when not stepping.

## Debug Hooks

| Hook | Signature | When Called | Overhead When Null |
|------|-----------|-------------|-------------------|
| `DebugHook` | `Action<Node, ReadOnlySpan<long>, VmManagedObjectHeap>?` | Before each statement in Normal mode (`CompileStatement`) | Single null check |
| `DebugInterrupt` | `Action<VmState>?` | **Not emitted** | n/a |

`DebugHook` fires for the program root and each `Block` child. Nested `If`/`While`/`Try` bodies are statement-hooked only when those bodies are `Block`s. `NoDebug` does not emit the hook.

## Value Marshalling

`RingCodec` converts between VM representation (`long` scalars, heap handles) and
CLR types. `AbiEmitter` uses `FromClr` / `ToClr` when emitting external
method and constructor calls; the emitted runtime helpers are `ToRing` (`object`
→ ring) and `Cast` / `TypeAs` / `UnboxNullable`.

Stack scalars widen through `Convert`, booleans use `!= 0`, float and double use
IEEE bit patterns, and heap references allocate through `VmManagedObjectHeap.Allocate` or
dereference via `VmManagedObjectHeap.UnsafeGet`.

## Key Design Decisions

- **Ring-based register allocation**: Values produced by µops are stored in ring slots
  indexed by eval-stack depth, keeping local variable count bounded (~10-20) regardless
  of µop count. See `CompilationContext.ConfigureRingAllocation()`.
- **ArrayPool-backed stack**: `VmValueStack` uses `ArrayPool<long>.Shared` to minimize
  allocations during execution.
- **Compiled delegates**: The `ProgramCompiler` emits LINQ Expressions and compiles
  them to `Action<VmState>` delegates. µop dispatch is via `Switch` on `_pc` in
  Debug/Normal mode, or straight-through `GotoExpression` branches in NoDebug mode.

## VM ABI: Call Frame Layout

Defined once in `VmCall`. A frame is a 2-word header followed by argument slots:

```
Slot[callSp + 0]:  previous FP
Slot[callSp + 1]:  saved SP
Slot[callSp + 2]:  arg0            ← FramePos (FP)
Slot[callSp + 3]:  arg1
...
Slot[FP + n]:      result          (written by the callee)
```

`VmCall.Invoke` sets `FramePos = FramePointer(callSp)` (the first argument slot),
runs the callee, reads the result from `slots[FramePos]`, then restores FP, the
closure handle, and the stack pointer. A parameterless callee still reserves one
argument slot so its result has somewhere to go (`ArgumentSlots`).

Inside a compiled function body, `FramePos` is the first argument slot: a
parameter resolves at `FramePos + index` and `return` writes
`slots[FramePos]` then sets `SP = FramePos + 1`.
