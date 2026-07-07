# VM Execution Engine (Interpretation/Vm/)

The VM execution engine compiles analyzed AST nodes directly into LINQ Expression
delegates for fast execution via `DirectVmAbiEmitter`.

## File Map

| File | Purpose |
|------|---------|
| `DirectVmAbiEmitter.cs` | **Primary compiler**: walks analyzed AST, emits `Action<VmState>` delegate |
| `VmState.cs` | Per-execution state: stack, heap, registers, debug interrupt, trace, loop limits |
| `VmProgram.cs` | Compiled program record (delegate + max local depth) |
| `ValueStack.cs` | Pooled `long[]` stack backed by `ArrayPool<long>.Shared` |
| `Heap.cs` | Object heap for reference-type values with free-list recycling |
| `CompilationMode.cs` | Debug/tracing vs. optimized mode enum |
| `Closure.cs` | Closure representation (function index + captured values) |
| `FunctionEntry.cs` | Function metadata (start PC, arg slots, local count) |
| `VmTrace.cs` | µop-level tracing infrastructure (gated by `state.Trace != null`) |
| `Ref.cs` | Safe reflection helpers using expression-tree-based MemberInfo lookups |
| `VmValueMarshaller.cs` | Converts between `VmState` representation and CLR types |

## Pipeline

```
Analyzed AST → DirectVmAbiEmitter.Emit() → VmProgram → Interpreter.Execute() → ExecutionResult
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

1. Define the record in `Poly/Syntax/Primitives/Primitives.cs` with a `StackEffect` override
2. Add a `case` arm in `ProgramCompiler.CompilePrimitives()` to emit the LINQ Expression
3. Add emit tests in `Poly.Tests/Interpretation/PrimitiveExpandTests.cs`

## Key Design Decisions

- **Ring-based register allocation**: Values produced by µops are stored in ring slots
  indexed by eval-stack depth, keeping local variable count bounded (~10-20) regardless
  of µop count. See `CompilationContext.ConfigureRingAllocation()`.
- **ArrayPool-backed stack**: `ValueStack` uses `ArrayPool<long>.Shared` to minimize
  allocations during execution.
- **Compiled delegates**: The `ProgramCompiler` emits LINQ Expressions and compiles
  them to `Action<VmState>` delegates. µop dispatch is via `Switch` on `_pc` in
  Debug/Normal mode, or straight-through `GotoExpression` branches in NoDebug mode.

## VM ABI: Call Frame Layout

Call frame layout (one long slot of metadata):

Before a `Call*` the N argument slots are on the stack:
```
[...stuff...][arg0][arg1]...[argN-1]
                                    ^ SP
```

The `Call*` handler pushes one metadata long:
```
Slot[sp++] = ((returnPC << 32) | (uint)(int)savedFrameBase)
```

After call setup (0-relative to FB):
```
Slot[0]:               arg0            ← FB
Slot[1 .. ArgSlots-1]: arg1..argN-1
Slot[ArgSlots]:        metadata
Slot[ArgSlots+1]:      local0
Slot[ArgSlots+LocalCount]:  last local
Slot[ArgSlots+LocalCount+1]: first eval  ← SP
```

Return convention:
- `EmitPrimitiveCall` saves the current FrameBase and return PC into a metadata slot,
  sets up the new frame, and jumps directly to the function-body label.
- `EmitReturnOp` writes the result to `Slot[FB]`, sets `SP = FB + 1`, and jumps
  to the compiled delegate's `ExitLabel` (ends program execution).
- Frame-return (restore caller PC/FB from metadata) will be added when cross-function
  calls need to return to the caller.
- FrameBase sentinel: `-1` = "no active frame" (top-level execution).
