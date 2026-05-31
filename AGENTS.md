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

## Overview & Architecture

**Goal:** Neurosymbolic platform — models codify discovered algorithms and heuristics as composable macros in a symbolic IR, validated by a tree-walker interpreter, compiled to native backends. Architecture described in `docs/decisions/2026-05-31-neurosymbolic-platform-vision.md`. TFM: `net10.0`, nullable enabled, zero external dependencies in core.

**Before working in this area:** Review `docs/decisions/` (especially decisions related to overall architecture, module boundaries, and the neurosymbolic platform vision).

- `Poly/` — core DSL: Syntax, Interpretation, Synthesis (macros), Introspection, Validation, Data/Modeling, Text.
- `Poly.Benchmarks/` — example entry point. (FluentApiExample.cs is fully commented out — do not treat it as a reference.)
- `Poly.Tests/` — unit tests using **TUnit** (not xUnit/NUnit).

**Module boundaries (enforced, one-way):**
- `Interpretation` → `Introspection`
- `Validation` → `Interpretation`
- `Synthesis` → `Syntax`, `Interpretation` (tree walker)
- `Introspection` must not depend on `Interpretation`.
- No module may depend on `Synthesis` except `DomainModeling` (evolution loop).
- Exception: CLR implementations under `Poly/Introspection/CommonLanguageRuntime` add concrete types without introducing reverse dependencies.

## Interpretation

**Before working in this area:** Review `docs/decisions/` for any architecture or analysis-related decisions.

### Structure
- `Syntax/`
  - `Nodes/` — AST node types (pure records)
  - `Node.cs`, `NodeExtensions.cs` — base + fluent construction helpers
  - `Analysis/` — `AnalysisContext`, `AnalyzerBuilder`, `Analyzer`, diagnostics & metadata store
- `Interpretation/`
  - `Analysis/` — semantic passes (Semantics, ConstantFolding, ControlFlow)
  - `LinqExpressions/` — `LinqExpressionGenerator`, `INodeCompiler`
  - `Mermaid/` — AST visualization

### Typical Pipeline
```csharp
var analyzer = new AnalyzerBuilder()
    .UseTypeResolver().UseMemberResolver().UseVariableScopeValidator()
    .Build();

var result = analyzer.Analyze(node);
var expr = new LinqExpressionGenerator(result).Compile(node);
```

`AnalysisContext` holds type definitions, node metadata, and diagnostics. There is no `Operators/` directory.

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

**Before working here:** Check `docs/decisions/` for relevant build/test or quality decisions.

- **Build:** `dotnet build Poly.Benchmarks/Poly.Benchmarks.csproj`
- **Test:** `dotnet run --project Poly.Tests/Poly.Tests.csproj`
- Work is **not complete** while the build is failing. Iterate until green (or explicitly blocked by the user).
- Add tests alongside feature changes.

**Test style:** `async [Test]` methods using `await Assert.That(result).IsEqualTo(expected)`.  
Naming convention: `Method_Condition_ExpectedResult`.

Test helpers in `Poly.Tests/TestHelpers/` (e.g. `BuildExpression`, `CompileLambda<T>`) are **test-only** — never part of the core library.

## Async / Await

**Before changes here:** Review `docs/decisions/` for any async or concurrency decisions.

Async support is **minimal by design**. `Task<T>` is merely the return type of the compiled delegate — `LinqExpressionGenerator` does not need full async/await expression tree support.

For simulation purposes, `Await` nodes extract results synchronously via `GetAwaiter().GetResult()`.

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
| Analysis passes             | `Interpretation/Analysis/` |
| AST node types              | `Syntax/Nodes/` |
| Contract interface generation | `Data/Modeling/CodeGeneration/DomainLoweringGenerator.cs` (`BuildEntityContractInterface`, `BuildStageContractInterfaces`) |
| Validation rules            | `Validation/Rules/` (register in `Validation/Rule.cs`) |
| Data-model constraints      | `Data/Modeling/Validation/` |
| Shared helpers              | `Extensions/` |

## Key Architectural Decisions

**This is a living requirement.**

Before performing analysis or making changes to **any** section:

- Consult the decisions in `docs/decisions/` that correspond to that area (see `docs/decisions/README.md`).
- In particular, review `docs/decisions/2026-core-engineering-principles.md` (the foundational "why we do things this way" decisions).

Major decisions (such as the 2026 immutable core + evolution layer work, and the neurosymbolic platform vision) are documented there. When you make a significant cross-cutting choice, add or update the corresponding decision record and reference it from here and from the relevant section above.

AGENTS.md contains the *operational* rules. `docs/decisions/` contains the *rationale and history*. Both are required reading.

## Coding Style

**Before stylistic or structural edits:** Confirm against `docs/decisions/` where relevant.

- Make **minimal changes**; match existing fluent API naming and chaining patterns.
- `Expr` = `System.Linq.Expressions.Expression` (global alias — see `Poly/GlobalUsings.cs`).
- No inline comments unless the logic is genuinely non-obvious.
- No `#region` / `#endregion` directives in new code.