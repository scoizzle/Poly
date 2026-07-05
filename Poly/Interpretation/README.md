# Poly.Interpretation

Semantic analysis and VM execution for programs expressed as `Poly.Syntax.Nodes` ASTs.

The module turns a syntax tree into a runnable program: analysis passes attach metadata, expansion lowers the tree to primitive IR, and the VM compiles and runs that IR. Per [VM as canonical semantics](../../docs/decisions/2026-06-08-vm-as-canonical-semantics.md), this path is the **authoritative behavior** for the platform — not the legacy LINQ expression generator or removed tree-walker.

Primitives are the canonical IR ([ADR](../../docs/decisions/2026-07-04-primitives-as-canonical-ir.md)); Interpretation consumes them but does not define the instruction set (see `Poly/Syntax/Primitives/`).

---

## Module boundaries

| Direction | Module | Relationship |
|-----------|--------|--------------|
| In | `Poly.Syntax` | AST node types, `Analyzer` / `AnalysisContext`, primitive definitions |
| In | `Poly.Introspection` | CLR type definitions, member resolution helpers |
| Out | `Poly.Validation` | May depend on Interpretation for rule evaluation |
| Out | `Poly.Synthesis` | Uses VM to validate macros |
| No | `Poly.Synthesis` | Interpretation must not depend on Synthesis |

Domain-specific constructs lower to generic primitives and VM opcodes — no domain opcodes in this module ([domain-lowering boundary](../../docs/decisions/2026-06-08-domain-lowering-boundary.md)).

---

## What this module does

1. **Analyze** — Run ordered `INodeAnalyzer` passes; produce `AnalysisResult` (metadata + diagnostics).
2. **Expand** — `ExpansionPass` drives `Node.ToPrimitives()`; store `PrimitiveExpansionMetadata` per node.
3. **Compile** — `ProgramCompiler` lowers primitives to a compiled `Action<VmState>` delegate (`VmProgram`).
4. **Execute** — Run the delegate on `VmState`; `InterpretResult` applies value-representation rules at the API boundary.

---

## Canonical pipeline

```
  AST (Syntax/Nodes)
       │
       ▼
  AnalyzerBuilder  ──►  AnalysisResult
  (13 passes)            metadata + diagnostics
       │
       ▼
  ExpansionPass  ──►  PrimitiveExpansionMetadata
       │              (linear PrimitiveNode[])
       ▼
  ProgramCompiler  ──►  VmProgram
  + PrimitiveLinker      (delegate, Functions[], RootValueKind, CallSites)
       │
       ▼
  Interpreter.Execute  ──►  ExecutionResult / InterpretResult
```

The cached standard pipeline lives in `Interpreter.cs`. Pass order and metadata contracts are documented in [`Analysis/README.md`](Analysis/README.md).

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
| [`Analysis/`](Analysis/) | Semantic passes + expansion | [`Analysis/README.md`](Analysis/README.md) — pass registry, ordering, diagnostics |
| [`Vm/`](Vm/) | Compile primitives → delegate; runtime state | [`Vm/README.md`](Vm/README.md) — `ProgramCompiler`, stack/heap ABI |
| [`CSharp/`](CSharp/) | C# source emission from AST | Secondary backend; not canonical semantics |
| [`LinqExpressions/`](LinqExpressions/) | LINQ expression trees from AST | Test/reference path; migration to VM ongoing (see tracker INT-003) |
| [`Mermaid/`](Mermaid/) | Mermaid diagrams from AST | Visualization only |

Root files: `Interpreter.cs` (pipeline + execute), `ExecutionResult.cs`, `InterpreterResult.cs`.

---

## Standard analysis pipeline (13 passes)

Registered in `Interpreter._analyzer` in this order. **Do not reorder** without updating [`Analysis/README.md`](Analysis/README.md) and tests.

| # | Extension | Purpose |
|---|-----------|---------|
| 1 | `.UseTypeAndMemberResolver()` | Resolved types and members |
| 2 | `.UseVariableScopeValidator()` | Scopes and variable lifetime |
| 3 | `.UseSideEffectAnalysis()` | Purity, elision, assignment-use |
| 4 | `.UseThisReferenceContext()` | `this` type in member bodies |
| 5 | `.UseJumpTargetResolution()` | Break / continue / goto targets |
| 6 | `.UseControlFlowAnalysis()` | CFG, reachability, loop metadata |
| 7 | `.UseValueRepresentationAnalysis()` | Stack scalar / bool / heap ref / void |
| 8 | `.UseCallSiteCatalog()` | Module call-site table + per-node indices |
| 9 | `.UseConstantFolding()` | Constant propagation and folding |
| 10 | `.UseDefiniteAssignmentAnalysis()` | Definite assignment |
| 11 | `.UseLambdaReturnTypeResolution()` | Lambda return types |
| 12 | `.UseExceptionRegionAnalysis()` | Try/catch/using region table |
| 13 | `.UsePrimitiveExpansion()` | AST → `PrimitiveExpansionMetadata` |

Custom pipelines: build your own `Analyzer` via `AnalyzerBuilder` (same extensions). `Interpreter.Compile` expects expansion metadata on the root when using the standard expansion pass.

---

## Primitive IR

Instruction definitions live under `Poly/Syntax/Primitives/` (`PrimitiveNode`, `StackEffect`, linking, `Phi`, `CallExternal`, EH placeholders). See [`Poly/Syntax/Primitives/README.md`](../Syntax/Primitives/README.md).

Lowering is implemented on each AST node as `ToPrimitives(ExpansionContext)`; `ExpansionPass` invokes it and caches the sequence.

---

## Secondary backends

These read the **AST directly** and are not the conformance target for new features:

- **LINQ** — `LinqExpressionGenerator`; parity tests still exist for some scenarios.
- **C#** — `CSharpGenerator`; codegen and pretty-printing.
- **Mermaid** — `MermaidAstGenerator`; docs and debugging.

New language semantics should land in analysis → primitives → `ProgramCompiler` first.

---

## Testing and docs

| Resource | Use |
|----------|-----|
| `Poly.Tests/Interpretation/` | VM correctness, expansion, integration tests |
| [`docs/plans/interpretation-system-issues.md`](../../docs/plans/interpretation-system-issues.md) | Tracked gaps (INT-*, ANA-*) |
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
- **Calls:** `Member`, `Invoke`, `IndexAccess`, `New`, `Lambda`
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