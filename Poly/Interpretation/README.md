# Poly Interpretation

`Poly.Interpretation` provides semantic analysis passes and LINQ expression generation for the AST types in `Poly.Syntax`.

## Overview

Interpretation has two main responsibilities:

1. Analyze AST nodes and attach semantic metadata (resolved types, members, control flow facts, diagnostics).
2. Compile analyzed trees into `System.Linq.Expressions` via `LinqExpressionGenerator`.

## Analysis Pipeline

`AnalyzerBuilder` lives in `Poly.Syntax.Analysis`. Interpretation contributes extension methods that register passes.

```csharp
using Poly.Syntax.Analysis;
using Poly.Interpretation.Analysis.Semantics;
using Poly.Interpretation.Analysis.ControlFlow;
using Poly.Interpretation.Analysis.ConstantFolding;

var analyzer = new AnalyzerBuilder()
    .UseTypeResolver()
    .UseMemberResolver()
    .UseVariableScopeValidator()
    .UseThisReferenceContext()
    .UseControlFlowAnalysis()
    .UseConstantFolding()
    .Build();

var result = analyzer.Analyze(ast);
if (result.HasErrors) {
    foreach (var diagnostic in result.Diagnostics) {
        Console.WriteLine($"[{diagnostic.Severity}] {diagnostic.Message}");
    }
}
```

## Available Pass Extensions

- `UseTypeResolver()`
- `UseMemberResolver()`
- `UseVariableScopeValidator()`
- `UseThisReferenceContext()`
- `UseControlFlowAnalysis()`
- `UseConstantFolding()`

## Generation

```csharp
using Poly.Interpretation.LinqExpressions;

var generator = new LinqExpressionGenerator(result);
var compilation = generator.Compile(ast);
```

## Common AST Types Used with Interpretation

- Core: `Constant`, `Parameter`, `Variable`, `Block`
- Member/invocation: `Member`, `Invoke`, `IndexAccess`, `New`
- Operators: `Add`, `Subtract`, `Multiply`, `Divide`, `Modulo`, `Equal`, `GreaterThan`, `And`, `Or`, `Not`
- Control flow: `Conditional`, `IfStatement`, `WhileLoop`, `ForLoop`, `ReturnStatement`, `TryCatchFinally`
- Type operations: `TypeCast`, `TypeIs`, `TypeAs`, `TypeReference`

## Minimal Example

```csharp
using Poly.Syntax.Nodes;
using Poly.Syntax.Analysis;
using Poly.Interpretation.Analysis.Semantics;
using Poly.Interpretation.LinqExpressions;

var ast = new Add(new Constant(10), new Constant(20));

var analysis = new AnalyzerBuilder()
    .UseTypeResolver()
    .Build()
    .Analyze(ast);

var generator = new LinqExpressionGenerator(analysis);
var compilation = generator.Compile(ast);
```
