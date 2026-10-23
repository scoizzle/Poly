# Poly.Interpretation

Generic **language VM** for programs expressed as `Poly.Ast` Syntax trees. DomainModeling is a client that lowers into this language.

**Platform map:** [`docs/CORE.md`](../../docs/CORE.md) — boundaries, node replacement, direct AST→VM (read before inventing parallel paths).

Analyze → `Interpreter.Compile` (fail-closed on analysis errors) → `AbiEmitter` → `VmState`. Per [VM as canonical semantics](../../docs/decisions/2026-06-08-vm-as-canonical-semantics.md), this path is the **authoritative behavior**.

There is **one interpreter**. Its second opinion comes from outside this repository: `CSharpWriter` prints the same Syntax tree, Roslyn compiles that text, and the compiled method must produce what the VM produced (`Poly.Tests/Interpretation/AstParity*.cs`). The writer is the meaning-source checked by that compiler, not a second execution engine of ours — C# emit is a projection of the same trees.

**C# language surface** (`CSharp/`): Syntax trees print through one `Poly.Grammar` pattern table — `CSharpWriter` emits declarations and statements from the table's fixed skeletons, and `CSharpReader` matches the same patterns and folds them back to AST nodes. A type name is a receiver, not a value: a qualified name in call-receiver position binds through analysis (`DottedTypeNames`), and a short name a `using` directive imports binds as the reader folds it, so `BitOperations.PopCount(x)` under `using System.Numerics;` reaches the same static call as `System.Numerics.BitOperations.PopCount(x)`. A name the text also spells as a value keeps its value meaning, so a local, field or parameter named like a namespace or type wins; alias usings do not parse and are unsupported. Roslyn is the external oracle in the tests: every golden file must parse with zero syntax diagnostics, round-trip idempotently, and agree with Roslyn's declaration/invocation/lambda counts. `CSharpGenerator` is a thin compatibility façade over `CSharpWriter`.

Stored `Invoke(Variable)` / `Invoke(Parameter)` closures **late-bind**. Analysis (`LambdaCaptureMetadata`, `StoredLambdaMetadata`) records free bindings and which variable holds a lambda. A `Lambda` node’s resolved type is the function value it produces (`Func`/`Action` from parameters + yield), a heap ref; `Invoke` takes the yield/return kind. `Assignment` / `Switch` / `Try` / valued `Return` yield their result kind; `If` / `Using` / loops / jumps / valueless `Return` are void. A void root does not unwrap leftover stack. Illegal `Invoke` targets fail at analysis. Incompatible assigns to one `Variable` (kind or category, including if/else) fail at analysis. Captured `Variable`s share a heap `long[1]` cell. `Variable` is a binding only (declare on `Block.Variables` or foreach); `Assignment` is the only write. C# declare-only is `var x = default(T)` (analysis, else first-assignment RHS, else `object`); `var x = e` is printer fusion of a direct first assignment, not a third AST form.

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
2. **Compile** — `AbiEmitter` lowers the analyzed AST directly to a compiled `Action<VmState>` delegate (`VmProgram`). No intermediate primitive flattening.
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
  AbiEmitter ──►  VmProgram
  (direct AST-to-ABI      (delegate, StepNodes, DebugInfo)
   lowering, no
   primitive expansion)
       │
       ▼
  Interpreter.Execute  ──►  ExecutionResult / InterpretResult
```

The cached standard pipeline lives in `Interpreter.cs`. Pass order and metadata contracts are documented in [`Analysis/README.md`](Analysis/README.md).

Direct AST lowering is the sole compilation path. No primitive flattening or expansion step is used — the `AbiEmitter` walks the analyzed AST directly and emits `System.Linq.Expressions` trees targeting the VM ABI (`VmState`, ring registers, 2-value frame model, heap).

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
| [`Mermaid/`](Mermaid/) | Mermaid diagrams from AST | Visualization only; labels from `NodeFacts`, children from `Node.Children` |

Root files: `Interpreter.cs` (pipeline + execute), `ExecutionResult.cs`, `InterpreterResult.cs`, `NodeFacts.cs`.

`NodeFacts.cs` is the one table of which producer honours which concrete `Node` kind — `Vm`, `Writer`, `WriterExpanded` (the print re-reads as an equivalent executable kind, not as the same kind), `Reader`, `Mermaid` — and the message a deliberate VM rejection carries. It is data, with no behaviour; `Poly.Tests/Interpretation/NodeFactsTotalityTests.cs` checks every row against a corpus of the C# this suite already carries, so a new kind without a row, a producer that loses an arm, or a rejection whose message drifts is a failing test that names the kind. Adding a node kind means adding its row.

---

## Standard analysis pipeline

`Interpreter._analyzer` is 14 passes. **`Use*` registration is the run order** — asserted by `StandardAnalyzer_PassNames_MatchInterpreterPipeline` and listed in [`Analysis/README.md`](Analysis/README.md). Do not invent a separate insert schedule; `Build` is registration order.

`Interpreter.Compile` is the one compile door: it fails closed on every `DiagnosticSeverity.Error`. `CompileChecked` is an alias. Use `Analyze` to inspect diagnostics without emitting. `Await` and `ParameterReference` compile-reject. `Comment` is a statement no-op and compile-rejects as a value (never dummy `0`).

---

## Direct AST Lowering

The sole compilation path is `AbiEmitter` which walks the analyzed AST and emits
`System.Linq.Expressions` trees targeting the VM ABI (`VmState`, ring registers for temporaries,
2-value frame model for user variables and call linkage, heap for objects). No primitive
flattening or expansion step exists — the AST is the canonical lowering target.

See [`Vm/README.md`](Vm/README.md) for the ABI model and emitter details.

### Frame ABI (`VmCall`)

The call frame uses a 2-word header followed by argument slots, defined once in
[`Vm/VmCall.cs`](Vm/VmCall.cs):

```
[previousFP] [savedSP] [arg0] [arg1] ...
 ^-- frame header (2 words)      ^-- FramePos
```

- **`previousFP`**: frame pointer of the caller.
- **`savedSP`**: stack pointer just before the header was pushed.
- **Argument/local counts** are known at compile time — not stored on the stack.

`VmCall.Invoke` pushes the header and arguments, sets `FramePos` to the first
argument slot, runs the callee, and reads the result from `slots[FramePos]`.

### Ring Registers

The emitter uses a ring register discipline during compilation: values flow through
`_r0.._rN` local variables allocated inline during the AST walk. Unlike the old
primitive path, there is no global pre-pass (`RingAllocator`) — ring assignment
happens during the AST walk. A `RingRegion` (`ctx.OpenRing()`) folds a sequence of
compiled operands into consecutive slots, so callers do not hand-manage
`RingDepth`. The result is equivalent: the CLR JIT can enregister ring locals.

### Compilation Modes

| Mode | Debug Hooks | PC Tracking | Loop-tick sandbox | Use Case |
|------|-------------|-------------|-------------------|----------|
| `Normal` (default) | Enabled | Enabled | Enabled | Development, debugging, testing |
| `NoDebug` | Disabled | Disabled | Omitted | Production, benchmarks, maximum speed |

---

## Value Representation

The VM uses a uniform `long` representation on the evaluation stack.
[`ValueRepresentationClassifier`](Vm/Abi/ValueRepresentation.cs) classifies each
effective node during emit by its resolved CLR type:

| Kind | Description | Example |
|------|-------------|---------|
| `StackScalar` | Inline word: a number, a boolean (0 = false, non-zero = true; the CLR type is `bool`), or an IEEE bit pattern | `2 + 3`, `a == b` |
| `HeapRef` | Heap handle; the slot holds an index into `VmState.Heap` | `new Person()` |
| `Void` | Statement/produces no value | `if (x) { }` |
| `Unknown` | No resolved type (an unresolved expression) | — |

The `RootValueKind` on `VmProgram` tells `InterpreterResult` how to correctly
marshal the program's top-level result — whether to dereference a heap handle
or return the raw scalar.

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

- **C#** — `CSharpGenerator`; codegen and pretty-printing. Its printed text is the differential the test suite holds against Roslyn.
- **Mermaid** — `MermaidAstGenerator`; docs and debugging. It reads `NodeFacts` for a kind's label and `Node.Children` for its children, so it re-derives no dispatch of its own.

New language semantics should land in analysis → direct lowering → `AbiEmitter` first.

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
2. Implement `PassName` and `Analyze()`. Use `context.SetMetadata()` and `context.ReportDiagnostic()` for outputs. Schedule is the `Use*` / `AddAnalyzer` registration line (no `After` / `Before` / `Dependencies`).
3. Add an extension method on `AnalyzerBuilder` in the same file.
4. Register it in `Interpreter.cs` (in the `AnalyzerBuilder` chain) and update the pass table in this README and `Analysis/README.md`.
5. Add tests in `Poly.Tests/Interpretation/`.

### Adding a New Primitive

Primitives are defined in `Poly/Syntax/Primitives/Primitives.cs`. Add the record
with a `StackEffect` override, then add the `case` arm in `AbiEmitter`
to emit the LINQ Expression. Add emit tests in `Poly.Tests/Interpretation/`.

---

## Testing and docs

| Resource | Use |
|----------|-----|
| `Poly.Tests/Interpretation/` | VM correctness, direct lowering, integration tests |
| `LanguageVmTests` | One `Interpreter.Compile` + execute or compile-reject per executable `CompileNodeInner` kind |
| `LanguageSurfaceTests` | Inventory: Executable / CompileReject / AnalysisOnly for every `Poly.Ast` `Node` |
| [`docs/plans/archive/interpretation/`](../../docs/plans/archive/interpretation/README.md) | **Archived** pre-direct-ABI plans (do not execute) |
| [`docs/plans/archive/v2-to-v3/master-roadmap.md`](../../docs/plans/archive/v2-to-v3/master-roadmap.md) | Milestone history (archived) |
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
- **Types:** `TypeCast`, `TypeIs`, `TypeAs`, `TypeOf`
- **Shrink (compile-reject):** `Await`, `ParameterReference`, type names as values (`NamedTypeReference` / `TypeReference` / `ClrTypeReference` / `PrimitiveTypeReference` / `TypeDefinitionReference`)
- **Comment:** statement no-op; not a VM value

Full taxonomy: `Poly/Ast/Nodes/`. Inventory: `LanguageSurfaceTests`.

---

## Before changing this module

1. Read relevant entries in [`docs/decisions/`](../../docs/decisions/) (especially VM, primitives IR, domain lowering).
2. If you change pass order in `Interpreter.cs`, update [`Analysis/README.md`](Analysis/README.md).
3. If you add a primitive, follow [`Vm/README.md`](Vm/README.md) “Adding a New Primitive” and extend `Poly/Syntax/Primitives/`.
4. Keep `AGENTS.md` Interpretation section aligned when pipeline or boundaries shift.