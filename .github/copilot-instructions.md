# Copilot Instructions for the Poly Workspace

Keep only requirements that measurably improve customer time-to-value, correctness, or operability; remove the rest. Engineer end-to-end system behavior with clear ownership boundaries, not isolated parts. Optimize for shipped capability by delivering the smallest coherent platform that proves the business model, not framework completeness. Build working code before abstraction: pattern catalogs (GoF, POSA, PoEAA, DDD) describe recurring outcomes observed in implementations, so extracting abstractions after implementation is required, while designing them first is speculation that burns irrecoverable time. Operational guardrails (ADR templates, compatibility policies, test conventions, CI config) are allowed because they are enabling constraints with identifiable first consumers that unblock implementation. Tools and infrastructure serve domain intent, and the domain serves system capability; the domain model is the key artifact, tool choices are judged by fidelity to domain expression rather than familiarity, and no tool preference may override correctness, operability, or shipped capability.

## Overview & Architecture
**Goal:** Shared abstraction layer into varying type systems for dynamic code generation and execution. Fluent, strongly-typed domain modeling for validation, serialization, and codegen. TFM: `net10.0`, nullable enabled, zero external dependencies in core.

- `Poly/` — core DSL: Interpretation, Introspection, Text, Validation.
- `Poly.Benchmarks/` — example entry point. Note: [FluentApiExample.cs](../Poly.Benchmarks/FluentApiExample.cs) is fully commented out — do not treat it as a reference.
- `Poly.Tests/` — unit tests using **TUnit** (not xUnit/NUnit).

**Module boundaries (enforced, one-way):**
- `Interpretation` → `Introspection`
- `Validation` → `Interpretation`
- `Introspection` must not depend on `Interpretation`.
- Exception: CLR implementations under [Poly/Introspection/CommonLanguageRuntime](../Poly/Introspection/CommonLanguageRuntime) add concrete types without introducing reverse dependencies.

## Interpretation

```
Interpretation/
  AbstractSyntaxTree/   ← Node types (pure records); extension methods in NodeExtensions.cs
  Analysis/             ← AnalysisContext, AnalyzerBuilder, semantic passes (Semantics/, ConstantFolding/, ControlFlow/)
  LinqExpressions/      ← LinqExpressionGenerator, INodeCompiler
  Mermaid/              ← AST visualization
```

Typical pipeline:
```csharp
var analyzer = new AnalyzerBuilder()
    .UseTypeResolver().UseMemberResolver().UseVariableScopeValidator()
    .Build();
var result = analyzer.Analyze(node);
var expr = new LinqExpressionGenerator(result).Compile(node);
```

`AnalysisContext` (not `InterpretationContext`) holds type definitions, node metadata, and diagnostics. There is no `Operators/` directory.

## Validation

Entry point is `RuleSetBuilder<T>`:
```csharp
var ruleSet = new RuleSetBuilder<Person>()
    .Member(p => p.Name, c => c.NotNull().MinLength(1).MaxLength(100))
    .Member(p => p.Age,  c => c.Minimum(0).Maximum(150))
    .AddRule(new CustomRule())
    .Build();
```

**Rule vs Constraint:** `Rule` is the abstract base; `Constraint : Rule` adds `ApplicableCategories` and `Scope`. JSON polymorphism is declared inline via `[JsonPolymorphic]`/`[JsonDerivedType]` attributes on `Rule.cs` and `Constraint.cs` — register new subtypes in both files. There is no separate polymorphic resolver class and no `DomainModeling/` directory.

**Pitfall:** `NumericConstraintSetBuilderExtensions` merges `Minimum()` and `Maximum()` into a single `RangeConstraint` in-place — calling them on separate builders does not produce isolated constraints.

## Build & Test
- Build: `dotnet build Poly.Benchmarks/Poly.Benchmarks.csproj`
- Test: `dotnet run --project Poly.Tests/Poly.Tests.csproj`
- Work is not complete while the build is failing; keep iterating until build failures are resolved or explicitly blocked by the user.
- Add tests alongside feature changes.

Test style: async `[Test]` methods with `await Assert.That(result).IsEqualTo(expected)`. Naming: `Method_Condition_ExpectedResult`. Helpers in `Poly.Tests/TestHelpers/` (e.g. `BuildExpression()`, `CompileLambda<T>()`) are test-only — not part of the core library.

## Placement Rules
| What | Where |
|---|---|
| Shared abstractions | `Introspection` |
| Analysis passes | `Interpretation/Analysis/` |
| AST node types | `Interpretation/AbstractSyntaxTree/` |
| Constraints | `Validation/Constraints/` — register in `Constraint.cs` |
| Rules | `Validation/Rules/` — register in `Rule.cs`; attach via `RuleSetBuilder<T>.AddRule()` |
| Shared helpers | `Extensions/` |

## Coding Style
- Minimal changes; match existing fluent API naming and chaining patterns.
- `Expr` = `System.Linq.Expressions.Expression` (global alias — see `Poly/GlobalUsings.cs`).
- No inline comments unless logic is non-obvious.
- No `#region`/`#endregion` directives in new code.