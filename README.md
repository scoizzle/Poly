# Poly

Poly is a strongly-typed .NET toolkit for building and analyzing abstract syntax trees, validating models through rule composition, and compiling analyzed trees into LINQ expressions.

## Repository Layout

- `Poly/Syntax` - Core AST and analysis primitives shared across the workspace
- `Poly/Interpretation` - Semantic analysis passes and LINQ expression generation
- `Poly/Introspection` - Provider-based type definition abstraction over CLR and custom systems
- `Poly/Validation` - Rule model that builds AST predicates and compiles them to `Predicate<T>`
- `Poly/Data/Modeling` - Domain modeling types, transactional mutation commands, and domain analyzers
- `Poly/Text` - Parsing and matching utilities (`StringView`, match expressions, numeric parsers)

## Quick Start

### 1) Build an AST in `Poly.Syntax`

```csharp
using Poly.Syntax.Nodes;

// (10 + 20) * 2
Node ast = new Multiply(
    new Add(new Constant(10), new Constant(20)),
    new Constant(2));
```

### 2) Analyze with semantic passes in `Poly.Interpretation`

```csharp
using Poly.Syntax.Analysis;
using Poly.Interpretation.Analysis.Semantics;

var analyzer = new AnalyzerBuilder()
    .UseTypeResolver()
    .UseMemberResolver()
    .UseVariableScopeValidator()
    .UseThisReferenceContext()
    .UseControlFlowAnalysis()
    .UseConstantFolding()
    .Build();

var analysis = analyzer.Analyze(ast);
```

### 3) Generate LINQ expressions

```csharp
using Poly.Interpretation.LinqExpressions;

var generator = new LinqExpressionGenerator(analysis);
var compilation = generator.Compile(ast);
```

### 4) Build and run validation rules

```csharp
using Poly.Validation;
using Poly.Validation.Rules;

var rules = new Rule[] {
    new ComparisonRule("Age", ComparisonOperator.GreaterThanOrEqual, "MinimumAge")
};

var ruleSet = new RuleSet<Person>(rules);
var isValid = ruleSet.Test(person);
```

## Build and Test

```bash
# Build
dotnet build Poly.Benchmarks/Poly.Benchmarks.csproj

# Run tests (TUnit)
dotnet run --project Poly.Tests/Poly.Tests.csproj
```

## Notes

- This repository targets `net10.0` with nullable enabled.
- `Poly.Benchmarks/FluentApiExample.cs` is intentionally commented out and should not be used as API reference.
