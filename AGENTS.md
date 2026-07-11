# Poly Workspace Instructions

**Before performing analysis or making changes to any section of this repository:**

1. Identify the relevant section(s) below.
2. Examine the corresponding decision documents in `docs/decisions/` (start with `docs/decisions/README.md` for the index and guidelines).
3. If a significant change would benefit from a new decision, create one following the process in `docs/decisions/README.md`.

This ensures major directional choices are respected and prevents repeated re-litigation of core decisions.

## Core Principles

These six principles are the non-negotiable foundation for all work in this repository. They are loaded directly into agent context via AGENTS.md on every session (unlike general decision files in `docs/decisions/`).

**You must evaluate every significant decision, change, or proposal against these principles.**

The full rationale and history for these principles lives in `docs/decisions/2026-core-engineering-principles.md`. Consult it when you need deeper context, but the version below is what you are expected to follow.

- **Keep only what measurably helps the customer.** Requirements must improve time-to-value, correctness, or operability. Everything else is removed.
- **Engineer end-to-end system behavior with clear ownership boundaries.** Avoid creating isolated parts whose interactions are unclear or accidental.
- **Optimize for shipped capability over completeness.** Deliver the smallest coherent platform that proves the business model. Framework completeness is not a goal.
- **Build working code before extracting abstractions.** Pattern catalogs (GoF, POSA, PoEAA, DDD, etc.) describe outcomes observed in real implementations. Abstractions should be extracted *after* working code exists — designing them first is speculation that burns irrecoverable time.
- **Operational guardrails are allowed only when they have real first consumers.** Things like placement rules, test conventions, and decision records are valuable precisely when they unblock implementation for identifiable people or agents.
- **The domain model is the key artifact.** Tools, languages, and infrastructure are judged by fidelity to domain expression — not familiarity or fashion. No tool preference may override correctness, operability, or shipped capability.

The expanded rationale, history, and examples of how these principles have been applied live in `docs/decisions/2026-core-engineering-principles.md`. Consult it when you need the deeper "why," but the six bullets above are the enforceable version.

### Naming

- **Name things for what they ARE, not what pattern they use.** Pattern names belong in design discussions and decision records, not in type and directory names. If a class compiles IR to µops, it's a `UopCompiler`, not a `UopLoweringPass` or a `UopLoweringVisitor`. If a directory holds backends, it's `Backends/`, not `Visitors/`. The name should answer "what does this thing do for the system?" — not "what GoF category does it happen to fall into?"
- **A concrete thing IS a concept, not a pattern.** `CSharpCodeGenerator` IS a code generator. `Inliner` IS an inliner. `RingAnalyzer` IS an analyzer. The concept is the type's identity; the pattern is an implementation detail that may change.

## Overview & Architecture

**Goal:** Neurosymbolic platform — models codify discovered algorithms and heuristics as composable macros in the AST (the primary symbolic/serializable IR), validated by the VM (canonical execution semantics), compiled to native backends. The AST is the model-facing symbolic form; `DirectVmAbiEmitter` performs direct AST-to-VM-ABI lowering without an intermediate primitive flattening step. Architecture described in `docs/decisions/2026-05-31-neurosymbolic-platform-vision.md` (with 2026-07-06 clarification), `docs/decisions/2026-06-08-vm-as-canonical-semantics.md`, and `docs/decisions/2026-07-04-primitives-as-canonical-ir.md`. TFM: `net10.0`, nullable enabled, zero external dependencies in core.

**Before working in this area:** Review `docs/decisions/` (especially decisions related to overall architecture, module boundaries, VM design, and the neurosymbolic platform vision). The AST is the symbolic primary; the VM ABI is the execution target.

- `Poly/` — core DSL: Syntax (AST as symbolic IR), Interpretation (VM execution), Synthesis (macros), Introspection, Validation, Data/Modeling, Text.
- `Poly.Benchmarks/` — example entry point. (FluentApiExample.cs is fully commented out — do not treat it as a reference.)
- `Poly.Tests/` — unit tests using **TUnit** (not xUnit/NUnit).

**Module boundaries (enforced, one-way):**
- `Interpretation` → `Syntax`, `Introspection`
- `Validation` → `Interpretation`
- `Synthesis` → `Syntax`, `Interpretation` (VM for macro validation)
- `DomainModeling` → `Syntax` (domain constructs lower to generic VM opcodes; no dependency on Interpretation)
- `Introspection` must not depend on `Interpretation`.
- No module may depend on `Synthesis` except `DomainModeling` (evolution loop).
- Exception: CLR implementations under `Poly/Introspection/CommonLanguageRuntime` add concrete types without introducing reverse dependencies.
- **Domain concepts lower to generic VM opcodes** (no domain-specific opcodes). See `docs/decisions/2026-06-08-domain-lowering-boundary.md`.
- **The AST is the canonical symbolic form** — no intermediate primitive IR. The `DirectVmAbiEmitter` performs direct AST-to-VM-ABI lowering. See `docs/decisions/2026-07-04-primitives-as-canonical-ir.md`.

## Direct AST Lowering

The `DirectVmAbiEmitter` (in `Poly/Interpretation/Vm/`) walks analyzed AST nodes and emits
`System.Linq.Expressions` trees targeting `VmState` directly. No intermediate primitive
flattening, ring allocator, or side-table reconstruction is needed.

## Interpretation

**Before working in this area:** Review `docs/decisions/` for any architecture or analysis-related decisions.

Interpretation contains the VM execution engine and semantic analysis passes. It does **not** define the IR — it consumes the lowered primitives (with metadata expanded at lowering time). The AST (`Syntax/Nodes`) is the primary symbolic form; Interpretation owns execution semantics on the lowered AST.

**The TreeWalkingInterpreter has been removed.** The VM is the sole canonical execution engine.
See `docs/decisions/2026-06-08-vm-as-canonical-semantics.md`.

### Structure

The Interpretation module is organized into sub-systems. See `Poly/Interpretation/README.md`
for the full directory map, and the individual READMEs in each sub-directory for details:
- `Poly/Interpretation/Vm/README.md` — VM execution engine
- `Poly/Interpretation/Analysis/README.md` — Semantic analysis passes
- `Poly/Interpretation/CSharp/README.md` — C# code generation
- `Poly/Interpretation/LinqExpressions/README.md` — LINQ expression generation (test reference)
- `Poly/Interpretation/Mermaid/README.md` — AST visualization

Syntax definitions are in `Poly/Syntax/`:
- `Poly/Syntax/Nodes/` — AST node types (pure records)
- `Poly/Syntax/Analysis/` — `AnalysisContext`, `AnalyzerBuilder`, `Analyzer`, diagnostics & metadata store

### Pipeline

```csharp
// 1. Analysis (runs all passes in order)
var analyzer = new AnalyzerBuilder()
    .UseTypeAndMemberResolver()
    .UseVariableScopeValidator()
    .UseSideEffectAnalysis()
    .UseThisReferenceContext()
    .UseJumpTargetResolution()
    .UseControlFlowAnalysis()
    .UseConstantFolding()
    .UseDefiniteAssignmentAnalysis()
    .UseLambdaReturnTypeResolution()
    .UseExceptionRegionAnalysis()
    .Build();

var analysis = analyzer.Analyze(node);

// 2. Compile direct (AST → VM ABI, no intermediate IR)
var program = Interpreter.Compile(node, analysis);

// 3. Execute
using var exec = Interpreter.Execute(program, s => s.MaxLoopIterations = 10_000);
var output = exec.Result;
```

See `Poly/Interpretation/Vm/DirectVmAbiEmitter.cs` for the emitter implementation.
the lowered AST plus attached metadata are sufficient for correct VM execution.

## Validation

**Before working in this area:** Review `docs/decisions/` for any validation or rule-related decisions.

Entry point is `RuleSet<T>` built from `IEnumerable<Rule>`:

```csharp
var rules = new Rule[] {
  new ComparisonRule("Start", ComparisonOperator.LessThanOrEqual, "End")
};

var ruleSet = new RuleSet<Person>(rules);
```

`Rule` is the abstract base. JSON polymorphism for subtypes is declared on `Validation/Rule.cs` using `[JsonPolymorphic]` + `[JsonDerivedType]`. Register new subtypes there.

## Build & Test

**🔴 V2 DELETED (2026-07-10)** — `Poly/Data/Modeling` has been removed. The single modeling stack is `Poly/DomainModeling` (V3). All V2 tests, demos, and MCP tools have been deleted alongside it.

**Before working here:** Check `docs/decisions/` for relevant build/test or quality decisions.

- **Build:** `dotnet build Poly.Benchmarks/Poly.Benchmarks.csproj`
- **Test:** `dotnet run --project Poly.Tests/Poly.Tests.csproj`
- Work is **not complete** while the build is failing. Iterate until green (or explicitly blocked by the user).
- Add tests alongside feature changes.
- For **isolated prototyping** (e.g. testing a snippet in total isolation from Poly projects), see `docs/file-based-csharp-apps.md` — the file-based apps technique lets you run single `.cs` files with `dotnet <file>.cs` without creating a project.

**Test style:** `async [Test]` methods using `await Assert.That(result).IsEqualTo(expected)`.  
Naming convention: `Method_Condition_ExpectedResult`.

Test helpers in `Poly.Tests/TestHelpers/` (e.g. `BuildExpression`, `CompileLambda<T>`) are **test-only** — never part of the core library.

## Async / Await

**Before changes here:** Review `docs/decisions/` for any async or concurrency decisions.

Async support is **minimal by design**. `Task<T>` is merely the return type of the compiled delegate — `LinqExpressionGenerator` does not need full async/await expression tree support.

For simulation purposes, `Await` nodes extract results synchronously via `GetAwaiter().GetResult()`.

**Breakpoints** use `VmState.DebugInterrupt` — a callback invoked before each µop in Debug/Normal compilation mode, giving external code full control over breakpoint policy, inspection, and single-stepping. See `docs/decisions/2026-06-08-breakpoint-architecture.md`.

**µop-level tracing:** `Lowering.cs` attaches `SourceName` to each µop via `EmitOp(ref ctx, op, source)`. At compile time `TraceBefore` generates a `VmTrace.LogUop(pc, text, sp, fb, state)` call insider each µop's expression, gated at runtime by `state.Trace != null` — ~1 ns when `state.Trace` is null (default). `CommentOp` markers (`; text`) alias section boundaries in the µop list for readability and generate zero code. Test files set `state.Trace = TestTraceWriter` which routes to `Console.Error` — visible in TUnit via `--show-stderr`.  Active in all build configurations.

**Key artifacts:**
- `Await(Node Operand)` — `Syntax/Nodes/Await.cs`
- `IsAsync` flag — `Syntax/Nodes/TypeDefinitions/MethodDefinitionNode.cs`
- C# emission — `Interpretation/CSharp/CSharpGenerator.cs`
- LINQ path — `Interpretation/LinqExpressions/LinqExpressionGenerator.cs` (`CompileAwait`)

**Future async:** Cross-entity policies and actor patterns will require real async at the lowering level (signatures become `Task<Result>`). Introduce only when the first cross-entity policy test demands it.

## Contract Interface Generation

**Before touching anything here:** Review `docs/decisions/` (especially the immutable core and lowering-related decisions). Consider whether a dedicated decision record for the actor contract model is needed or should be updated.

`DomainImplementationLoweringPass.LowerToContractInterfaces()` generates the actor contract surface.

**Rules (authoritative):**
- Naming: `I{StageName}{EntityName}`
- Inheritance: Entity base + parent stage interface when `Stage.Parent` is set. Abstract stages are kept alongside concrete children.
- Action placement: Only direct actions when a parent stage interface exists in the chain; otherwise all effective actions.

The full rationale for these exact rules lives in (or should be added to) `docs/decisions/`. The Placement Rules table below also references this area.

## Placement Rules

**Before making changes that affect structure or ownership:** Check `docs/decisions/` for relevant decisions.

| What                        | Where |
|-----------------------------|-------|
| Shared abstractions         | `Introspection` |
| Analysis passes             | `Interpretation/Analysis/` — see `Poly/Interpretation/Analysis/README.md` |
| AST node types              | `Syntax/Nodes/` |
| VM execution engine         | `Interpretation/Vm/` — see `Poly/Interpretation/Vm/README.md` |
| Canonical lowering          | `Interpretation/Vm/` — see `Poly/Interpretation/Vm/DirectVmAbiEmitter.cs` |
| Validation rules            | `Validation/Rules/` (register in `Validation/Rule.cs`) |
| Data-model constraints      | `Data/Modeling/Validation/` |
| Shared helpers              | `Extensions/` |

## Key Architectural Decisions

**This is a living requirement.**

Before performing analysis or making changes to **any** section:

- Consult the decisions in `docs/decisions/` that correspond to that area (see `docs/decisions/README.md`).
- In particular, review `docs/decisions/2026-core-engineering-principles.md` (the foundational "why we do things this way" decisions).

Major decisions (such as the 2026 immutable core + evolution layer work, the neurosymbolic platform vision with 2026-07-06 clarification, direct AST-to-VM-ABI lowering, and VM as canonical semantics) are documented there. The 2026-07-06 docs cleanup pass further solidified: AST as primary symbolic/serializable IR; direct lowering as the execution path; no information loss on lowering. When you make a significant cross-cutting choice, add or update the corresponding decision record and reference it from here and from the relevant section above.

AGENTS.md contains the *operational* rules. `docs/decisions/` contains the *rationale and history*. Both are required reading.

## Coding Style

**Before stylistic or structural edits:** Confirm against `docs/decisions/` where relevant.

- Make **minimal changes**; match existing fluent API naming and chaining patterns.
- `Expr` = `System.Linq.Expressions.Expression` (file-local alias in test files; `Poly/GlobalUsings.cs` uses `global using Expression = System.Linq.Expressions.Expression`).
- No inline comments unless the logic is genuinely non-obvious.
- No `#region` / `#endregion` directives in new code.