# Interpretation System — Developer's Guide

This directory contains in-depth documentation for the Poly Interpretation system,
complementing the module README at `Poly/Interpretation/README.md`.

## Contents

| Document | Purpose |
|----------|---------|
| `vm-abi-reference.md` | Complete VM ABI reference: frame layout, call convention, register model |
| `analysis-pass-guide.md` | Step-by-step guide to creating and registering analysis passes |
| `debugging-and-tracing.md` | Using `VmDebugger`, trace writers, and breakpoints |

## Quick Links

- Module README: [`Poly/Interpretation/README.md`](../../Poly/Interpretation/README.md)
- Analysis README: [`Poly/Interpretation/Analysis/README.md`](../../Poly/Interpretation/Analysis/README.md)
- VM README: [`Poly/Interpretation/Vm/README.md`](../../Poly/Interpretation/Vm/README.md)
- ADRs: [`docs/decisions/`](../decisions/) (especially VM, primitives-as-IR, EH)
- Architecture review: [`docs/interpretation-system-architecture-review.md`](../interpretation-system-architecture-review.md)

## Architecture Overview

```
┌──────────────────────────────────────────────────────────┐
│                    Poly.Interpretation                    │
│                                                          │
│  ┌─────────────────┐    ┌──────────────────────────┐     │
│  │  Analysis Passes │───▶│  AnalysisResult          │     │
│  │  (12 passes)     │    │  (metadata + diagnostics) │     │
│  └─────────────────┘    └──────────┬───────────────┘     │
│                                    │                     │
│  ┌─────────────────────────────────▼──────────────────┐  │
│  │           DirectVmAbiEmitter                       │  │
│  │  (AST → LINQ Expression trees → Action<VmState>)   │  │
│  └─────────────────────────────────┬──────────────────┘  │
│                                    │                     │
│  ┌─────────────────────────────────▼──────────────────┐  │
│  │                 VmProgram                           │  │
│  │  (delegate + functions + sites + debug info)        │  │
│  └─────────────────────────────────┬──────────────────┘  │
│                                    │                     │
│  ┌─────────────────────────────────▼──────────────────┐  │
│  │              Execution Result                       │  │
│  │  (owns VmState, typed + raw value access)           │  │
│  └────────────────────────────────────────────────────┘  │
└──────────────────────────────────────────────────────────┘
```

## Key Concepts

### Analysis Passes
Each pass implements `INodeAnalyzer`, walks the AST in post-order, and attaches
metadata to nodes via `AnalysisContext`. Passes declare dependencies to ensure
correct execution order via topological sort.

### Direct AST Lowering
`DirectVmAbiEmitter` is the sole compilation path. It walks the analyzed AST
directly and emits `System.Linq.Expressions` trees that read/write `VmState`
fields. No intermediate flattening or primitive expansion.

### Value Representation
The `ValueRepresentationAnalyzer` classifies every expression by how its value
should be interpreted: stack scalar (direct `long`), heap handle (dereference
via index), boolean (zero/non-zero), or void.

### Frame Model
The VM uses a linked-frame ABI with a 2-word header (previousFP + savedSP).
Argument and local counts are known at compile time. The `CallStack` runtime
provides typed accessors for debug hooks and resume.

### Debugging
- `VmDebugger`: Step-over and continue on a background thread
- `DebugHook`: Per-node callback with locals span
- `DebugInterrupt`: Per-µop callback with full state
- Microsecond-level tracing via `VmTrace.LogUop` (gated by `state.Trace != null`)

## Common Tasks

### Analyze + Compile + Execute
```csharp
var node = new Add(new Constant(2), new Constant(3));
var program = Interpreter.Compile(node);
using var result = Interpreter.Execute(program);
Console.WriteLine(result.GetValue<int>()); // 5
```

### Custom Pipeline
```csharp
var analyzer = new AnalyzerBuilder()
    .UseTypeAndMemberResolver()
    .UseVariableScopeValidator()
    .UseSideEffectAnalysis()
    .Build();

var analysis = analyzer.Analyze(node);
var program = Interpreter.Compile(node, analysis);
```

### With Tracing
```csharp
using var result = Interpreter.Execute(program, state => {
    state.Trace = Console.Error;
});
```

### Step Debugging
```csharp
using var dbg = new VmDebugger(program);
dbg.Start();              // pause at first statement
while (!dbg.IsCompleted) {
    var r = dbg.StepOver();
    Console.WriteLine($"At: {r.Node.Kind}, locals: {r.Locals.Count}");
}
```

### Resumption
```csharp
using var exec = Interpreter.Execute(program);
if (exec.IsSuspended) {
    var state = exec.State;
    state.Heap.Set(someHandle, newValue);
    using var resumed = exec.Resume();
}
```
