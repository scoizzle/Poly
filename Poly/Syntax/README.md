# Poly Syntax

`Poly.Syntax` contains the shared abstract syntax tree (AST) model and reusable analysis infrastructure used by `Interpretation`, `Validation`, and `Data/Modeling` analyzers.

## Core Types

- `Node` (`Node.cs`): base record for all AST nodes
- `NodeId` (`NodeId.cs`): stable identifier for metadata/diagnostics/incremental analysis
- `NodeExtensions` (`NodeExtensions.cs`): fluent helpers for constructing AST expressions

## Node Library

`Syntax/Nodes` contains expression and statement node types, including:

- Arithmetic: `Add`, `Subtract`, `Multiply`, `Divide`, `Modulo`, `UnaryMinus`
- Comparison: `GreaterThan`, `GreaterThanOrEqual`, `LessThan`, `LessThanOrEqual`, `Equal`, `NotEqual`
- Logical: `And`, `Or`, `Not`, `Coalesce`
- Access/invocation: `Member`, `IndexAccess`, `Invoke`, `New`
- Flow/control: `Block`, `Assignment`, `IfStatement`, `ForLoop`, `WhileLoop`, `TryCatchFinally`, `SwitchStatement`
- Type system nodes: `TypeReference`, `TypeDefinitionReference`, `TypeCast`, `TypeAs`, `TypeIs`

## Analysis Framework

`Syntax/Analysis` defines a generic analyzer pipeline:

- `AnalyzerBuilder`: register analyzers and type definition providers
- `Analyzer`: executes passes over the tree
- `AnalysisContext`: per-run metadata and diagnostics store
- `AnalysisResult`: immutable result snapshot with diagnostics and metadata accessors
- `INodeAnalyzer`: pass interface
- `NodeMetadataStore`: typed node metadata storage

## Minimal Example

```csharp
using Poly.Syntax.Nodes;
using Poly.Syntax.Analysis;

Node ast = new Add(new Constant(1), new Constant(2));

var analyzer = new AnalyzerBuilder().Build();
var result = analyzer.Analyze(ast);
```

## Notes

- `Node.Children` drives traversal for analyzers.
- `NodeId` stability is critical for incremental analysis behavior.
- Higher-level semantic passes are added by modules such as `Poly.Interpretation`.