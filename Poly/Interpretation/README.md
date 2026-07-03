# Poly Interpretation

`Poly.Interpretation` provides semantic analysis passes and the VM execution engine for the AST types in `Poly.Syntax`.

## Overview

Interpretation has three main responsibilities:

1. **Analyze** AST nodes and attach semantic metadata (resolved types, members, control flow facts, diagnostics).
2. **Compile** analyzed trees via the VM pipeline (AST → primitives → compiled delegate).
3. **Execute** programs in the stack-based VM.

## Architecture

The canonical execution path uses the **primitive expansion system** (`Node.ToPrimitives`) to lower AST nodes to a sequence of `PrimitiveNode` instructions, which are then compiled into a LINQ Expression delegate and executed by the VM.

```
AnalyzerBuilder → AnalysisResult → Node.ToPrimitives() → PrimitiveNode[]
    → ProgramCompiler.CompilePrimitives() → VmProgram → Vm.Execute()
```

## Core Directories

| Directory | Purpose |
|-----------|---------|
| `Vm/` | VM execution engine: `Vm.cs`, `VmState.cs`, `ProgramCompiler.cs`, `ValueStack`, `Heap`, `Closure`, `PrimitiveLinker` |
| `Analysis/` | Semantic analysis passes: constant folding, control flow, type/member resolution, side-effect analysis |
| `CSharp/` | C# code generation from AST nodes |
| `LinqExpressions/` | LINQ Expression tree generation (secondary — testing and PolicyEvaluator) |
| `Mermaid/` | Mermaid flowchart visualization of AST structure |

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
```

## Execution

```csharp
using Poly.Interpretation.Vm;

// After analysis and primitive expansion + linking
var program = ProgramCompiler.CompilePrimitives(linkedPrimitives);
using var state = new VmState(program);
var result = Vm.Execute(state);
```

## Available Pass Extensions

- `UseTypeResolver()`
- `UseMemberResolver()`
- `UseVariableScopeValidator()`
- `UseThisReferenceContext()`
- `UseControlFlowAnalysis()`
- `UseConstantFolding()`

## Common AST Types

- Core: `Constant`, `Parameter`, `Variable`, `Block`
- Member/invocation: `Member`, `Invoke`, `IndexAccess`, `New`
- Operators: `Add`, `Subtract`, `Multiply`, `Divide`, `Modulo`, `Equal`, `GreaterThan`, `And`, `Or`, `Not`
- Control flow: `Conditional`, `IfStatement`, `WhileLoop`, `ForLoop`, `Return`, `TryCatchFinally`
- Type operations: `TypeCast`, `TypeIs`, `TypeAs`, `TypeReference`
