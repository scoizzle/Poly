# VM Execution Engine (Interpretation/Vm/)

The VM execution engine takes expanded primitive sequences and compiles them into
LINQ Expression delegates for fast execution.

## File Map

| File | Purpose |
|------|---------|
| `Vm.cs` | Entry point: `Vm.Execute()`, result interpretation, status management |
| `VmState.cs` | Per-execution state: stack, heap, registers, breakpoints, trace, loop limits |
| `VmProgram.cs` | Compiled program record (delegate + source ranges + function table) |
| `ProgramCompiler.cs` | `PrimitiveNode[]` → compiled `Action<VmState>` delegate via LINQ Expressions |
| `CompilationContext.cs` | Ring-based µop value allocation, label management, local variable caching |
| `ValueStack.cs` | Pooled `long[]` stack backed by `ArrayPool<long>.Shared` |
| `Heap.cs` | Object heap for reference-type values with free-list recycling |
| `PrimitiveLinker.cs` | Label→PC resolution for Goto/CondGoto primitives |
| `CallSiteCompiler.cs` | Compiles external CLR method calls to `CallSiteDelegate` |
| `Closure.cs` | Closure representation (function index + captured values) |
| `FunctionEntry.cs` | Function metadata (start PC, arg slots, local count) |
| `VmTrace.cs` | µop-level tracing infrastructure (gated by `state.Trace != null`) |
| `Ref.cs` | Safe reflection helpers using expression-tree-based MemberInfo lookups |
| `SourceRange.cs` | Mapping from PC ranges to AST `Node` references |

## Pipeline

```
PrimitiveNode[] → PrimitiveLinker.Link() → ProgramCompiler.CompilePrimitives()
    → VmProgram → Vm.Execute() → ExecutionResult
```

## Adding a New Primitive

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
