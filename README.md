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

## MCP Operability Tools

The MCP server in `Poly.Mcp` exposes four operability tools for domain sessions:

- `GetDomainHealth(sessionId)`
- `ExplainInvalidDomain(sessionId)`
- `DiffDomainRevision(sessionId, fromRevision, toRevision?)`
- `ApplyMutationWithTrace(sessionId, mutationType, name, category?, parentEntityName?)`

### Request examples

```json
{ "sessionId": "demo-session" }
```

```json
{ "sessionId": "demo-session", "fromRevision": 3, "toRevision": 8 }
```

```json
{ "sessionId": "demo-session", "mutationType": "AddEntity", "name": "Ticket" }
```

### Success response example (`GetDomainHealth`)

```json
{
    "success": true,
    "message": "Domain health returned.",
    "sessionId": "demo-session",
    "revision": 12,
    "data": {
        "hasErrors": false,
        "errorCount": 0,
        "warningCount": 0,
        "totalAnalysisTime": "00:00:00.0048123",
        "incremental": false,
        "passes": [
            { "passName": "StructuralDomainAnalyzer", "elapsed": "00:00:00.0003456", "diagnosticCount": 0, "invalidatedNodeCount": 0 }
        ]
    },
    "affordances": []
}
```

### Success response example (`ExplainInvalidDomain`)

```json
{
    "success": true,
    "message": "Domain invalidity explanation returned.",
    "sessionId": "demo-session",
    "revision": 13,
    "data": {
        "errorCount": 1,
        "warningCount": 0,
        "nodes": [
            {
                "nodeId": "...",
                "nodeKind": "Primitive",
                "nodeName": "dup",
                "reasons": [
                    { "severity": "Error", "code": "DM001", "message": "Duplicate type name.", "hint": "Rename the duplicate type." }
                ]
            }
        ]
    },
    "affordances": []
}
```

### Success response example (`DiffDomainRevision`)

```json
{
    "success": true,
    "message": "Domain diff from revision 3 to 8 returned.",
    "sessionId": "demo-session",
    "revision": 8,
    "data": {
        "fromRevision": 3,
        "toRevision": 8,
        "addedCount": 2,
        "removedCount": 0,
        "changedCount": 1,
        "added": [],
        "removed": [],
        "changed": []
    },
    "affordances": []
}
```

### Success response example (`ApplyMutationWithTrace`)

```json
{
    "success": true,
    "message": "Mutation 'AddEntity' applied with trace.",
    "sessionId": "demo-session",
    "revision": 14,
    "data": {
        "succeeded": true,
        "rolledBack": false,
        "appliedStepCount": 1,
        "duration": "00:00:00.0009123",
        "errorCount": 0,
        "warningCount": 0,
        "affectedNodeIds": ["..."],
        "steps": [],
        "diagnostics": []
    },
    "affordances": []
}
```

### Failure response example (stable diagnostics)

```json
{
    "success": false,
    "message": "Unsupported mutationType 'Nope'. Supported values are SetDomainName, AddPrimitive, AddEntity.",
    "sessionId": "demo-session",
    "revision": null,
    "data": null,
    "affordances": [],
    "diagnostics": [
        "code=UNSUPPORTED_MUTATION;category=InvalidArgument;message=Unsupported mutationType 'Nope'. Supported values are SetDomainName, AddPrimitive, AddEntity.",
        "<exception details>"
    ]
}
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
