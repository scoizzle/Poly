# Poly Interpretation

`Poly.Interpretation` provides semantic analysis passes and the VM execution engine
for the AST types in `Poly.Syntax`.

## Responsibilities

1. **Analyze** AST nodes and attach semantic metadata (resolved types, members, control flow, diagnostics)
2. **Compile** analyzed trees via the VM pipeline (AST → primitives → compiled delegate)
3. **Execute** programs in the stack-based VM

## Architecture

The canonical execution path lowers AST nodes to a `PrimitiveNode` sequence via
`Node.ToPrimitives()`, then compiles them into a LINQ Expression delegate for VM execution.

```
AnalyzerBuilder → AnalysisResult
    → PrimitiveExpansionMetadata.Primitives (from ExpansionPass)
    → ProgramCompiler.CompilePrimitives() → VmProgram → Vm.Execute()
```

## Sub-directories

| Directory | Purpose |
|-----------|---------|
| `Vm/` | VM execution engine: `Vm.cs`, `VmState.cs`, `ProgramCompiler.cs`, `ValueStack`, `Heap`, `Closure`, `PrimitiveLinker` — see `Vm/README.md` |
| `Analysis/` | Semantic analysis passes: constant folding, control flow, type/member resolution, side-effect analysis — see `Analysis/README.md` |
| `CSharp/` | C# code generation from AST nodes |
| `LinqExpressions/` | LINQ Expression tree generation (secondary — testing and PolicyEvaluator) |
| `Mermaid/` | Mermaid flowchart visualization of AST structure |

## Standard Pipeline

The default analysis + compilation pipeline is assembled in `Interpreter`:

```csharp
using Poly.Interpretation;
using Poly.Interpretation.Vm;

// 1. Analyze (runs all passes in order)
var analysis = Interpreter.Analyze(node);

// 2. Compile
var program = Interpreter.Compile(node, analysis);

// 3. Execute
using var result = Vm.Execute(program);
var value = result.GetValue<long>();
```

Or in one step:
```csharp
var value = Interpreter.Execute(node); // analyze + compile + execute
```

## Available Pass Extensions

All registered via `AnalyzerBuilder` extension methods in their respective files:

| Extension | Pass | Defined In |
|-----------|------|-----------|
| `.UseTypeAndMemberResolver()` | Type resolution + member resolution | `Semantics/TypeAndMemberResolutionPass.cs` |
| `.UseVariableScopeValidator()` | Scope and variable lifetime | `Semantics/VariableLifetimePass.cs` |
| `.UseSideEffectAnalysis()` | Purity and dead-code elision | `Semantics/SideEffectAnalysisPass.cs` |
| `.UseThisReferenceContext()` | `this` resolution in member bodies | `Semantics/ThisReferenceContextPass.cs` |
| `.UseJumpTargetResolution()` | Break/continue/goto target resolution | `Semantics/JumpTargetPass.cs` |
| `.UseControlFlowAnalysis()` | CFG construction and reachability | `ControlFlow/ControlFlowAnalysisPass.cs` |
| `.UseConstantFolding()` | Constant expression evaluation | `ConstantFolding/ConstantFoldingPass.cs` |
| `.UseDefiniteAssignmentAnalysis()` | Definite assignment tracking | `Semantics/DefiniteAssignmentAnalyzer.cs` |
| `.UseLambdaReturnTypeResolution()` | Lambda return type refinement | `Semantics/LambdaReturnTypeAnalyzer.cs` |
| `.UsePrimitiveExpansion()` | AST → PrimitiveNode expansion | `ExpansionPass.cs` |

## Pipeline Ordering

Pass ordering is critical. See `Analysis/README.md` for the full ordering constraints.

## Primitive Nodes

The instruction set for the VM is defined in `Poly/Syntax/Primitives/`. See
`Poly/Syntax/Primitives/README.md` for the taxonomy and conventions.

## Common AST Types

- Core: `Constant`, `Parameter`, `Variable`, `Block`
- Member/invocation: `Member`, `Invoke`, `IndexAccess`, `New`
- Operators: `Add`, `Subtract`, `Multiply`, `Divide`, `Modulo`, `Equal`, `GreaterThan`, `And`, `Or`, `Not`, `BitwiseAnd`, `BitwiseOr`, `ShiftLeft`, `ShiftRight`
- Control flow: `Conditional`, `IfStatement`, `WhileLoop`, `ForLoop`, `Return`, `TryCatchFinally`, `SwitchStatement`
- Type operations: `TypeCast`, `TypeIs`, `TypeAs`, `TypeReference`
