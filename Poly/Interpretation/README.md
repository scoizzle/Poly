# Poly.Interpretation

Generic **language VM** for programs expressed as `Poly.Ast` Syntax trees. DomainModeling is a client that lowers into this language.

**Platform map:** [`docs/CORE.md`](../../docs/CORE.md) — boundaries, node replacement, direct AST→VM (read before inventing parallel paths).

Analyze → `Interpreter.Compile` (fail-closed on analysis errors) → `DirectVmAbiEmitter` → `VmState`. Per [VM as canonical semantics](../../docs/decisions/2026-06-08-vm-as-canonical-semantics.md), this path is the **authoritative behavior**.

The LINQ expression path (`BuildExpression` / `LinqExpressionGenerator`) is a **programmatic semantic checker** for that VM: the same Syntax tree must evaluate to the same result, and the expression tree is inspectable when debugging the custom VM. It is not a second language and not a replacement for `Interpreter.Compile`. C# emit is a projection of the same trees.

Stored `Invoke(Variable)` / `Invoke(Parameter)` closures **late-bind**. A captured `Variable` is a heap `long[1]` cell shared with the enclosing frame, so writes after the lambda is stored are visible at invoke (and writes inside the closure are visible outside). Immediate `Invoke(Lambda)` compiles the body in the caller frame against the same cells. Parameters cannot be assigned, so a captured `Parameter` is copied into a cell at closure creation.

---

## Module boundaries

| Direction | Module | Relationship |
|-----------|--------|--------------|
| In | `Poly.Syntax` | AST node types, `Analyzer` / `AnalysisContext` |
| In | `Poly.Introspection` | CLR type definitions, member resolution helpers |
| Out | `Poly.Validation` | May depend on Interpretation for rule evaluation |
| Out | `Poly.Synthesis` | Uses VM to validate macros |
| No | `Poly.Synthesis` | Interpretation must not depend on Synthesis |

Domain constructs lower to generic Syntax nodes — no domain opcodes or domain types in this module ([domain-lowering boundary](../../docs/decisions/2026-06-08-domain-lowering-boundary.md)).

---

## What this module does

1. **Analyze** — Run ordered `INodeAnalyzer` passes; produce `AnalysisResult` (metadata + diagnostics).
2. **Compile** — `DirectVmAbiEmitter` lowers the analyzed AST directly to a compiled `Action<VmState>` delegate (`VmProgram`). No intermediate primitive flattening.
3. **Execute** — Run the delegate on `VmState`; `InterpretResult` applies value-representation rules at the API boundary.

---

## Canonical pipeline

```
  AST (Syntax/Nodes)
       │
       ▼
  AnalyzerBuilder  ──►  AnalysisResult
  (14 passes)            metadata + diagnostics
       │
       ▼
  DirectVmAbiEmitter ──►  VmProgram
  (direct AST-to-ABI      (delegate, StepNodes, DebugInfo)
   lowering, no
   primitive expansion)
       │
       ▼
  Interpreter.Execute  ──►  ExecutionResult / InterpretResult
```

The cached standard pipeline lives in `Interpreter.cs`. Pass order and metadata contracts are documented in [`Analysis/README.md`](Analysis/README.md).

Direct AST lowering is the sole compilation path. No primitive flattening or expansion step is used — the `DirectVmAbiEmitter` walks the analyzed AST directly and emits `System.Linq.Expressions` trees targeting the VM ABI (`VmState`, ring registers, 2-value frame model, heap).

---

## Entry point: `Interpreter`

`Interpreter` is the supported façade for analyze → compile → execute. It owns a singleton `Analyzer` with the full pass list.

```csharp
using Poly.Interpretation;
using Poly.Interpretation.Vm;

var node = /* Syntax.Nodes.Expression or Block */;

// Analyze only
AnalysisResult analysis = Interpreter.Analyze(node);

// Analyze + compile (reuses cached analyzer)
VmProgram program = Interpreter.Compile(node);

// Or compile from a prior analysis (no re-analysis)
VmProgram program2 = Interpreter.Compile(node, analysis);

// One-shot: analyze, compile, execute; returns raw long on stack
long raw = Interpreter.Execute(node);

// Full control: configure VmState (trace, heap, args) before run
using ExecutionResult exec = Interpreter.Execute(program, state => {
    state.MaxLoopIterations = 100_000_000;
    state.Trace = myTraceWriter;  // optional µop trace
});
int value = exec.GetValue<int>();   // uses RootValueKind — heap vs scalar
long handle = exec.RawValue;
```

`VmProgram` carries optional `RootValueKind` (from value-representation analysis) and `CallSites` (portable call catalog). Lambda bodies compile to separate delegates in `VmProgram.Functions`.

---

## Directory map

| Directory | Role | Detail |
|-----------|------|--------|
| [`Analysis/`](Analysis/) | Semantic analysis passes | [`Analysis/README.md`](Analysis/README.md) — pass registry, ordering, diagnostics |
| [`Vm/`](Vm/) | Compile primitives → delegate; runtime state | [`Vm/README.md`](Vm/README.md) — `ProgramCompiler`, stack/heap ABI |
| [`CSharp/`](CSharp/) | C# source emission from AST | Secondary backend; not canonical semantics |
| [`LinqExpressions/`](LinqExpressions/) | LINQ expression trees from AST | Semantic parity + inspectable execution for the VM (not a second engine) |
| [`Mermaid/`](Mermaid/) | Mermaid diagrams from AST | Visualization only |

Root files: `Interpreter.cs` (pipeline + execute), `ExecutionResult.cs`, `InterpreterResult.cs`.

---

## Standard analysis pipeline

`Interpreter._analyzer` is 14 passes. **Built order** (after `AnalyzerBuilder` topological insert) is asserted by `StandardAnalyzer_PassNames_MatchInterpreterPipeline` and listed in [`Analysis/README.md`](Analysis/README.md). Do not copy a `Use*` registration list here — insert order ≠ registration order.

`Interpreter.Compile` fails closed on every `DiagnosticSeverity.Error`. Use `Analyze` to inspect diagnostics without emitting.

---

## Direct AST Lowering

The sole compilation path is `DirectVmAbiEmitter` which walks the analyzed AST and emits
`System.Linq.Expressions` trees targeting the VM ABI (`VmState`, ring registers for temporaries,
2-value frame model for user variables and call linkage, heap for objects). No primitive
flattening or expansion step exists — the AST is the canonical lowering target.

See [`Vm/README.md`](Vm/README.md) for the ABI model and emitter details.

### Frame ABI (CallStack)

The call stack uses a linked frame model with a 2-word header:

```
[...argN-1..arg0] [previousFP] [savedSP] [local0..localM-1]
                   ^-- frame header (2 words)                ^-- SP
```

- **`previousFP`**: Frame pointer of the caller (-1 for root frame).
- **`savedSP`**: Stack pointer just before the header was pushed.
- **Argument/local counts** are known at compile time — not stored on the stack.

`CallStack.AllocateFrame()` pushes the header and reserves local space.
`CallStack.DeallocateFrame()` restores the stack pointer and pops the frame.
The `CallStackFrame` record provides typed span accessors (`GetLocals`, `GetArguments`)
and indexers (`GetLocal`, `GetArgument`) for debug hooks and resume.

### Ring Registers

The emitter uses a ring register discipline during compilation: values flow through
`_r0.._rN` local variables allocated inline during the AST walk. Unlike the old
primitive path, there is no global pre-pass (`RingAllocator`) — ring assignment
happens during the AST walk. The result is equivalent: the CLR JIT can enregister
ring locals.

### Compilation Modes

| Mode | Debug Hooks | PC Tracking | Loop-tick sandbox | Use Case |
|------|-------------|-------------|-------------------|----------|
| `Normal` (default) | Enabled | Enabled | Enabled | Development, debugging, testing |
| `NoDebug` | Disabled | Disabled | Omitted | Production, benchmarks, maximum speed |

---

## Value Representation

The VM uses a uniform `long` representation on the evaluation stack, but the
[`ValueRepresentationAnalyzer`](Analysis/Semantics/ValueRepresentationPass.cs)
classifies every expression by how its value should be interpreted:

| Kind | Description | Example |
|------|-------------|---------|
| `StackScalar` | Numeric value stored directly (int, long, float, etc.) | `2 + 3` |
| `Bool` | Boolean (1 = true, 0 = false) | `a == b` |
| `HeapRef` | Heap handle; the slot holds an index into `VmState.Heap` | `new Person()` |
| `Void` | Statement/produces no value | `if (x) { }` |
| `Unknown` | Could not be determined statically | Runtime dispatch |

The `RootValueKind` on `VmProgram` tells `InterpreterResult` how to correctly
marshal the program's top-level result — whether to dereference a heap handle
or return the raw scalar.

---

## µop-level Tracing

Every µop compiled in `Normal` or `Debug` mode carries a `SourceName` label.
At compile time, `TraceBefore` inserts a `VmTrace.LogUop(pc, text, sp, fb, state)`
call inside each µop's expression — **~1 ns overhead** when `state.Trace` is null
(the default).

Enable tracing by setting `state.Trace` to any `TextWriter`:

```csharp
using var exec = Interpreter.Execute(program, state => {
    state.Trace = Console.Error;  // or StringWriter, etc.
});
```

`CommentOp("; text")` markers alias section boundaries in the µop list for
readability and generate zero code. Test files use `TestTraceWriter` which
routes to `Console.Error` — visible in TUnit via `--show-stderr`.
Active in all build configurations.

---

## Stepping and Debugging

`VmDebugger` provides high-level step-over and continue for interactive
debugging sessions. It attaches via `VmState.DebugHook` and uses a background
thread to execute the program:

```csharp
using var dbg = new VmDebugger(program);
var r1 = dbg.Start();         // pause at first statement
var r2 = dbg.StepOver();      // advance one statement
dbg.Continue();               // run to completion
```

The debugger is "always loaded, zero overhead when idle": during normal execution
the hook checks a `volatile bool` and returns immediately. `StepOver` sets a flag
so the next hook invocation blocks and signals back — the program runs at full
speed when nobody is stepping.

`VmState.DebugHook` is the live integration: Normal-mode `CompileStatement` invokes
it before each statement (root, and each `Block` child). Loop/if/try bodies are
hooked only when those bodies are themselves `Block`s. `CompilationMode.NoDebug`
omits the hook entirely. `CurrentAstNode` is written only when a hook is attached.

`VmState.DebugInterrupt` is unused by the emitter (kept on `VmState` only).

---

## Suspend / Resume

The VM supports suspend-and-resume for breakpoints and await-like scenarios:

```csharp
// First execution suspends
using var exec = Interpreter.Execute(program);
if (exec.IsSuspended)
{
    // Inspect state, modify variables, etc.
    var state = exec.State;

    // Resume with new arguments
    using var resumed = exec.Resume(args);
}
```

Key mechanics:
- `VmState.Status` transitions `Running → Suspended → Resuming → Running`
- The preamble re-reads `VmState.FramePos` and dispatches to the correct
  program counter on resume.
- `CurrentAstNode` and `CurrentNodeId` track the suspend point symbolically.

---

## Exception Handling

Structured exception handling uses native CLR `Expression.TryCatchFinally`
directly — no side tables or handler dispatching:

| AST Node | CLR Mapping |
|----------|-------------|
| `TryCatchFinally` | `Expression.TryCatch(Try(body), Catch(...), Finally(body))` |
| `ThrowStatement` | `Expression.Throw(...)` |
| `UsingStatement` | Lowered to try/finally with dispose |

The `ExceptionRegionAnalyzer` pass builds a region table (`ExceptionRegionMetadata`)
listing all protected ranges, handler types, and catch variable names. The emitter
consumes this to produce the correct nesting of `Try`/`Catch`/`Finally` expressions.

---

## Secondary backends

These read the **AST directly** and are not the conformance target for new features:

- **LINQ** — `LinqExpressionGenerator`; parity tests still exist for some scenarios.
- **C#** — `CSharpGenerator`; codegen and pretty-printing.
- **Mermaid** — `MermaidAstGenerator`; docs and debugging.

New language semantics should land in analysis → direct lowering → `DirectVmAbiEmitter` first.

---

## Working with ExecutionResult

`ExecutionResult` owns the `VmState` and exposes both the value and the state:

```csharp
using var result = Interpreter.Execute(program, state => {
    state.SetArgs(42, "hello");
});

// Typed value extraction (respects RootValueKind)
int number = result.GetValue<int>();      // 42
string text = result.GetValue<string>();  // "hello"

// Raw access
long raw = result.RawValue;

// State inspection (heap, stack, trace)
VmState state = result.State;
var heapObj = state.Heap.Get(handle);

// Resumption after suspension
if (result.IsSuspended)
{
    using var resumed = result.Resume(moreArgs);
    Console.WriteLine(resumed.GetValue<string>());
}
```

### InterpreterResult Values

`InterpreterResult` is a discriminated union via `ResultKind`:

| Kind | Meaning | Value |
|------|---------|-------|
| `Void` | Statement completed, no value | `null` |
| `Value` | Expression produced a value | The value |
| `Return` | Return signal | Optional return value |
| `Break` | Break from loop | Optional label |
| `Continue` | Continue loop iteration | Optional label |
| `Throw` | Exception thrown | The `Exception` |
| `Suspend` | Execution suspended | Optional reason string |

Use `GetValue<T>()` for typed extraction — it handles conversions from `long`
to `bool`, `int`, `short`, `byte`, and `object` automatically.

---

## Extending the Pipeline

### Adding a New Analysis Pass

1. Create a class implementing `INodeAnalyzer` in `Interpretation/Analysis/Semantics/` (or the appropriate subdirectory).
2. Implement `PassName`, `Dependencies`, and `Analyze()`. Use `context.SetMetadata()` and `context.ReportDiagnostic()` for outputs.
3. Add an extension method on `AnalyzerBuilder` in the same file.
4. Register it in `Interpreter.cs` (in the `AnalyzerBuilder` chain) and update the pass table in this README and `Analysis/README.md`.
5. Add tests in `Poly.Tests/Interpretation/`.

### Adding a New Primitive

Primitives are defined in `Poly/Syntax/Primitives/Primitives.cs`. Add the record
with a `StackEffect` override, then add the `case` arm in `DirectVmAbiEmitter`
to emit the LINQ Expression. Add emit tests in `Poly.Tests/Interpretation/`.

---

## Testing and docs

| Resource | Use |
|----------|-----|
| `Poly.Tests/Interpretation/` | VM correctness, direct lowering, integration tests |
| [`docs/plans/archive/interpretation/`](../../docs/plans/archive/interpretation/README.md) | **Archived** pre-direct-ABI plans (do not execute) |
| [`docs/plans/v2-to-v3/master-roadmap.md`](../../docs/plans/v2-to-v3/master-roadmap.md) | Active product planning (DomainModeling V2→V3) |
| [`docs/interpretation-system-architecture-review.md`](../../docs/interpretation-system-architecture-review.md) | Holistic architecture review (living doc) |
| [`docs/decisions/`](../../docs/decisions/) | ADRs: VM, primitives-as-IR, EH, serialization, sandboxing |

Build and test (from repo root):

```bash
dotnet build Poly.Benchmarks/Poly.Benchmarks.csproj
dotnet run --project Poly.Tests/Poly.Tests.csproj
```

---

## Common AST surface

Syntax types Interpretation most often compiles:

- **Core:** `Constant`, `Parameter`, `Variable`, `Block`
- **Calls:** `Member`, `Invoke` (`Lambda`, stored `Variable`/`Parameter` closures, or `Member`), `IndexAccess`, `New`, `Lambda`
- **Operators:** `Add`, `Subtract`, `Multiply`, `Divide`, `Equal`, comparisons, `And` / `Or` / `Not`, bitwise/shift
- **Control flow:** `Conditional`, `IfStatement`, loops, `Return`, `BreakStatement`, `ContinueStatement`, `GotoStatement`, `TryCatchFinally`, `UsingStatement`, `SwitchStatement`
- **Types:** `TypeCast`, `TypeIs`, `TypeAs`, `TypeReference`

Full taxonomy: `Poly/Syntax/Nodes/`.

---

## Before changing this module

1. Read relevant entries in [`docs/decisions/`](../../docs/decisions/) (especially VM, primitives IR, domain lowering).
2. If you change pass order in `Interpreter.cs`, update [`Analysis/README.md`](Analysis/README.md).
3. If you add a primitive, follow [`Vm/README.md`](Vm/README.md) “Adding a New Primitive” and extend `Poly/Syntax/Primitives/`.
4. Keep `AGENTS.md` Interpretation section aligned when pipeline or boundaries shift.