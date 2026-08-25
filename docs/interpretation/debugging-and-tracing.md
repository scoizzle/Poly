# Debugging and Tracing

This document describes the debugging and tracing facilities available in the
Poly Interpretation system.

## Overview

The Poly VM provides four levels of debugging instrumentation, from high-level
symbolic stepping to low-level µop tracing:

| Level | Mechanism | Granularity | Overhead When Idle |
|-------|-----------|-------------|-------------------|
| 1 | `VmDebugger` | AST node (statement) | Zero (volatile bool check) |
| 2 | `DebugHook` | AST node (statement) | Single null check |
| 3 | `DebugInterrupt` | µop | Single null check |
| 4 | `VmTrace` | µop with text | ~1 ns when null |

## Level 1: VmDebugger (Recommended for Interactive Use)

`VmDebugger` provides step-over and continue on a background thread, designed for
MCP services, neurosymbolic loops, and interactive debugging sessions.

### Basic Usage

```csharp
using var dbg = new VmDebugger(program);

// Start execution, pause at first statement
DebugResult r1 = dbg.Start();
Console.WriteLine($"At: {r1.Node.Kind}");

// Step through the program
while (!dbg.IsCompleted) {
    var r = dbg.StepOver();
    Console.WriteLine($"  -> {r.Node.Kind}, locals: {r.Locals.Count}");

    if (r.IsSuspend) {
        Console.WriteLine("Suspended at breakpoint");
        break;
    }
}

// Or run to completion
dbg.Continue();
```

### Advanced Inspection

```csharp
var r = dbg.StepOver();

// Current AST node
Node node = r.Node;

// Local variables (name + value)
foreach (var (name, value) in r.Locals) {
    Console.WriteLine($"  {name} = {value}");
}

// Underlying VM state (heap, stack, PC)
VmState state = dbg.State;
var heapObj = state.Heap.Get(handle);

// Check if program finished
if (r.IsCompleted) { /* done */ }
```

### Architecture

The debugger uses a producer-consumer pattern with `AutoResetEvent`:

```
Main Thread              Background Thread
───────────              ────────────────
Start()    ───────────▶  begin execution
                         hook fires, blocks
           ◀───────────  returns DebugResult
StepOver() ───────────▶  releases hook
                         next hook fires, blocks
           ◀───────────  returns DebugResult
Continue() ───────────▶  clears step flag
                         hooks pass through
                         delegate completes
```

When not stepping, the hook checks a `volatile bool` and returns immediately —
zero synchronization overhead.

## Level 2: DebugHook

`DebugHook` is a callback invoked before each AST node boundary in
`CompilationMode.Normal`. It provides the current node, a span of local
variables, and the heap reference.

```csharp
state.DebugHook = (node, locals, heap) => {
    Console.WriteLine($"At {node.Kind}: {locals.Length} locals");
    if (node is Constant c) Console.WriteLine($"  Value: {c.Value}");
};
```

### Locals Span

The `ReadOnlySpan<long>` provides direct access to the current frame's local
variables. The span is built at compile time from the frame model — no runtime
iteration needed. Variable-to-slot mapping is in `VmProgram.DebugInfo`.

### When to Use

- Custom debugger implementations
- Monitoring/profiling hooks
- Conditional breakpoints based on AST node properties
- When `VmDebugger`'s stepping granularity is too coarse

## Level 3: DebugInterrupt

`DebugInterrupt` is invoked before **every µop** in `CompilationMode.Normal`.
It receives the full `VmState`, allowing complete inspection and modification.

```csharp
state.DebugInterrupt = s => {
    if (s.ProgramCounter == 42) {
        Console.WriteLine($"Hit PC 42, SP={s.Stack.StackPointer}");
        s.Stack.Push(0L); // Modify execution
    }
};
```

### Performance Impact

Each µop checks `state.DebugInterrupt != null`. The callback itself is user code.
In `NoDebug` mode, the check is eliminated entirely.

### When to Use

- Low-level µop breakpoints
- Conditional breakpoints based on VM state
- Quick-and-dirty debugging of specific µop sequences

## Level 4: VmTrace (µop-level Logging)

When `state.Trace` is set to a `TextWriter`, the compiled delegate emits a trace
line before each µop:

```
   0 load_arg       text="Load arg(0)"    depth=0  fb=0
   1 load_arg       text="Load arg(1)"    depth=1  fb=0
   2 add            text="Add(Int64)"     depth=0  fb=0
   3 return         text="Return"         depth=1  fb=0
```

### Enabling

```csharp
state.Trace = Console.Error; // stderr
// or
state.Trace = new StringWriter(); // capture to string
```

### Trace Format

| Column | Width | Description |
|--------|-------|-------------|
| PC | 4 | µop index |
| Opcode | variable | Short opcode name |
| `text=` | variable | Source description from lowering |
| `depth=` | 2 | Ring register depth (eval stack depth) |
| `fb=` | 2 | Frame base |

### CommentOp Markers

The emitter can insert comment markers in the trace:

```csharp
// In the emitter:
ctx.CommentOp("; loop header");
// Generates trace: "   ; loop header"
```

These generate zero actual code — only the trace comment.

### Test Trace Writer

In tests, use `TestTraceWriter`:

```csharp
state.Trace = new TestTraceWriter(); // routes to Console.Error
```

Visible in TUnit via `--show-stderr`:

```bash
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --show-stderr
```

## Configuring Compilation Mode

```csharp
// With debug hooks (Normal mode — default)
VmProgram program = Interpreter.Compile(node, CompilationMode.Normal);

// Maximum speed (NoDebug mode — no hooks at all)
VmProgram program = Interpreter.Compile(node, CompilationMode.NoDebug);
```

The mode is set at compile time and cannot be changed at runtime. Choose:
- `Normal` for development, debugging, and testing
- `NoDebug` for production and benchmarks

## Suspend and Resume

The VM supports suspend/resume for breakpoints, await, and yield:

```csharp
// Execute and check for suspension
using var exec = Interpreter.Execute(program);
if (exec.IsSuspended)
{
    // State is fully preserved
    var state = exec.State;
    Console.WriteLine($"Suspended at node: {state.CurrentAstNode?.Kind}");

    // Resume with new arguments
    using var resumed = exec.Resume(newArgs);
    var result = resumed.GetValue<int>();
}
```

### Suspend Mechanics

1. The compiled delegate sets `state.Status = InterpreterStatus.Suspended`
2. The delegate captures `state.FramePos` and `state.ProgramCounter`
3. Control returns to the caller via the normal return path
4. On resume, `Interpreter.Resume` sets status to `Resuming`
5. The preamble re-reads `FramePos` and dispatches to the saved PC

### Best Practices

- Always dispose `ExecutionResult` (it disposes the pooled stack)
- On resume, the original `ExecutionResult` transfers ownership of `VmState`
  to the new result — don't use the original result after calling `Resume()`
- Check `IsSuspended` before calling `Resume()`
- Use `CurrentAstNode` or `CurrentNodeId` for symbolic suspend location
