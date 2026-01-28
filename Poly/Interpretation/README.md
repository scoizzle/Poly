# Poly Interpretation System

A fluent, strongly-typed AST analysis and code generation system for .NET that compiles expression trees to System.Linq.Expressions for optimal runtime performance.

## Overview

The Interpretation system provides:

- **AST Foundation**: Composable node types for building abstract syntax trees
- **Semantic Analysis**: Type inference, member resolution, and scope validation
- **Code Generation**: Transform analyzed AST to System.Linq.Expressions
- **Type Safety**: Compile-time and semantic verification of all operations
- **Diagnostic Reporting**: Collect errors, warnings, and hints during analysis

## Architecture

### Two-Phase Design

```
AST Construction
    ↓
[Analysis Phase]
    - TypeResolver: Infer and validate types
    - MemberResolver: Resolve property/method access
    - ScopeValidator: Track variables and detect errors
    ↓
AnalysisResult (with metadata)
    ↓
[Generation Phase]
    - LinqExpressionGenerator: Transform to Expression<T>
    ↓
Compiled Delegate (optimized IL)
```

### Analysis Phase

The analysis system validates and annotates AST nodes with semantic information:

```csharp
var analyzer = new AnalyzerBuilder()
    .UseTypeResolver()              // Resolves types for all nodes
    .UseMemberResolver()            // Resolves properties, methods, indexers
    .UseVariableScopeValidator()    // Validates variable declarations
    .Build();

var result = analyzer.Analyze(astNode);

// Check for errors
if (result.Context.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
{
    foreach (var diag in result.Context.Diagnostics)
    {
        Console.WriteLine($"[{diag.Severity}] {diag.Message}");
    }
    return;
}
```

**Available Passes:**
- **`TypeResolver`**: Infers types for constants, variables, operations, member/method access, blocks
- **`MemberResolver`**: Resolves properties, methods, and indexers; validates access
- **`ScopeValidator`**: Validates variable declarations, detects undeclared variables and shadowing

### Generation Phase

The `LinqExpressionGenerator` transforms analyzed AST into System.Linq.Expressions:

```csharp
var generator = new LinqExpressionGenerator(analysisResult);

// Compile to Expression tree
Expression expr = generator.Compile(astNode);

// Or create a compiled delegate directly
var param = new Parameter("x", TypeReference.To<int>());
var compiled = generator.CompileAsDelegate(astNode, param) 
    as Func<int, int>;

int result = compiled(42);
```

## AST Node Types

### Core Nodes

**`Constant`**: Literal values
```csharp
new Constant(42)
new Constant("hello")
new Constant(3.14)
```

**`Parameter`**: Lambda parameters with optional type hints
```csharp
new Parameter("x")
new Parameter("name", TypeReference.To<string>())
```

**`Variable`**: Named references in block scopes
```csharp
var x = new Variable("counter");
var assign = new Assignment(x, new Constant(0));
var increment = new Assignment(x, new Add(x, new Constant(1)));
```

**`Block`**: Statement sequences with local scope
```csharp
var x = new Variable("temp");
var block = new Block(
    [new Assignment(x, new Constant(10)), new Multiply(x, new Constant(2))],
    [x]  // Variables in scope
);
```

### Operators

**Arithmetic**: `Add`, `Subtract`, `Multiply`, `Divide`, `Modulo`, `UnaryMinus`

```csharp
new Add(new Constant(10), new Constant(20))
new Multiply(param, new Constant(2))
new UnaryMinus(param)
```

**Comparison**: `GreaterThan`, `GreaterThanOrEqual`, `LessThan`, `LessThanOrEqual`

```csharp
new GreaterThan(age, new Constant(18))
new LessThanOrEqual(price, new Constant(100.0))
```

**Equality**: `Equal`, `NotEqual`

```csharp
new Equal(status, new Constant("active"))
new NotEqual(count, new Constant(0))
```

**Boolean**: `And`, `Or`, `Not`

```csharp
new And(isAdult, hasLicense)
new Or(isVip, isPlatinum)
new Not(isExpired)
```

**Conditional**: `Conditional`, `Coalesce`

```csharp
// age > 18 ? "adult" : "minor"
new Conditional(
    new GreaterThan(age, new Constant(18)),
    new Constant("adult"),
    new Constant("minor")
)

// name ?? "Unknown"
new Coalesce(name, new Constant("Unknown"))
```

**Member Access**: `MemberAccess`, `IndexAccess`, `MethodInvocation`

```csharp
new MemberAccess(person, "Name")
new IndexAccess(array, new Constant(0))
new MethodInvocation(text, "ToUpper")
```

**Type Operations**: `TypeCast`, `TypeReference`

```csharp
new TypeCast(value, TypeReference.To<string>())
```

**Control Flow**: `Assignment`

```csharp
new Assignment(variable, new Constant(42))
```

## Quick Start

### Basic Arithmetic Expression

```csharp
using Poly.Interpretation.AbstractSyntaxTree;
using Poly.Interpretation.Analysis;
using Poly.Interpretation.LinqExpressions;
using System.Linq.Expressions;

// Build AST: (10 + 20) * 2
var add = new Add(new Constant(10), new Constant(20));
var multiply = new Multiply(add, new Constant(2));

// Analyze
var analyzer = new AnalyzerBuilder()
    .UseTypeResolver()
    .Build();
var result = analyzer.Analyze(multiply);

// Generate
var generator = new LinqExpressionGenerator(result);
var expr = generator.Compile(multiply);

// Compile and execute
var lambda = Expression.Lambda<Func<int>>(expr);
int value = lambda.Compile()();  // 60
```

### Using Parameters

```csharp
// AST: x * 2 + 10
var x = new Parameter("x", TypeReference.To<int>());
var expr = new Add(
    new Multiply(x, new Constant(2)),
    new Constant(10)
);

// Analyze
var analyzer = new AnalyzerBuilder()
    .UseTypeResolver()
    .Build();
var result = analyzer.Analyze(expr);

// Generate delegate
var generator = new LinqExpressionGenerator(result);
var compiled = (Func<int, int>)generator.CompileAsDelegate(expr, x);

int value = compiled(5);  // 20
```

### Conditional Logic

```csharp
// age > 18 ? "adult" : "minor"
var age = new Parameter("age", TypeReference.To<int>());
var condition = new GreaterThan(age, new Constant(18));
var ast = new Conditional(
    condition,
    new Constant("adult"),
    new Constant("minor")
);

var analyzer = new AnalyzerBuilder()
    .UseTypeResolver()
    .Build();
var result = analyzer.Analyze(ast);

var generator = new LinqExpressionGenerator(result);
var compiled = (Func<int, string>)generator.CompileAsDelegate(ast, age);

Console.WriteLine(compiled(25));  // "adult"
Console.WriteLine(compiled(10));  // "minor"
```

### Block with Variables

```csharp
// { var x = 10; x * 2 }
var x = new Variable("x");
var assignment = new Assignment(x, new Constant(10));
var multiply = new Multiply(x, new Constant(2));
var block = new Block([assignment, multiply], [x]);

var analyzer = new AnalyzerBuilder()
    .UseTypeResolver()
    .UseVariableScopeValidator()
    .Build();
var result = analyzer.Analyze(block);

var generator = new LinqExpressionGenerator(result);
var expr = generator.Compile(block);

var lambda = Expression.Lambda<Func<int>>(expr);
int value = lambda.Compile()();  // 20
```

### Member Access

```csharp
// text.Length
var text = new Parameter("text", TypeReference.To<string>());
var length = new MemberAccess(text, "Length");

var analyzer = new AnalyzerBuilder()
    .UseTypeResolver()
    .UseMemberResolver()
    .Build();
var result = analyzer.Analyze(length);

var generator = new LinqExpressionGenerator(result);
var compiled = (Func<string, int>)generator.CompileAsDelegate(length, text);

Console.WriteLine(compiled("hello"));  // 5
```

## Test Helpers

For convenience in tests, use the helper extension methods from `Poly.Tests.TestHelpers`:

```csharp
// Wrap constant values
var five = Wrap(5);
var pi = Wrap(3.14);

// BuildExpression - analyze and generate in one step
var expr = new Add(Wrap(10), Wrap(20)).BuildExpression();
var result = Expression.Lambda<Func<int>>(expr).Compile()();  // 30

// BuildExpressionWithParameters - pre-register parameter types
var x = new Parameter("x");
var expr = new Multiply(x, Wrap(2))
    .BuildExpressionWithParameters((x, typeof(int)));

// CompileLambda - build, generate, and compile in one step
var compiled = new Add(Wrap(10), Wrap(20))
    .CompileLambda<Func<int>>();
var result = compiled();  // 30
```

## Diagnostics

The analyzer collects diagnostics during analysis:

```csharp
var result = analyzer.Analyze(ast);

// Check for errors
if (result.Context.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
{
    foreach (var diag in result.Context.Diagnostics)
    {
        Console.WriteLine($"[{diag.Severity}] {diag.Message}");
    }
}
```

**Diagnostic Severities:**
- `Error`: Critical issues preventing compilation
- `Warning`: Potential problems (e.g., variable shadowing)
- `Information`: Informational messages
- `Hint`: Suggestions for improvements

## Scope and Variables

Variables have lexical scope within blocks:

```csharp
// Valid: variable declared in block scope
var x = new Variable("x");
var block = new Block([new Assignment(x, new Constant(10))], [x]);

// Invalid: variable used without declaration
var y = new Variable("y");
var ref = y;  // Error: y not in scope
```

**Scope Rules:**
- Variables must be declared in the block's variable list
- Variables are only visible within their declaring block
- Inner blocks can shadow outer variables
- References must match declarations (by name)

## AST Traversal

All nodes implement `IEnumerable<Node>` via the `Children` property:

```csharp
public static void VisitAllNodes(Node node, Action<Node> visit)
{
    visit(node);
    foreach (var child in node.Children)
    {
        VisitAllNodes(child, visit);
    }
}
```

## Performance

Expression trees compile to optimized IL via `Expression.Lambda<T>().Compile()`:

- **Native execution speed** after compilation
- **One-time compilation cost** amortized over many calls
- **Zero reflection overhead** at execution time
- **Cache compiled delegates** for repeated use

```csharp
var compiled = lambda.Compile();  // Once
for (int i = 0; i < 1_000_000; i++)
{
    var result = compiled(i);  // Native speed
}
```

## Testing

383 tests validate:
- Type resolution for all operators
- Member resolution for properties and methods
- Variable scope tracking and shadowing
- Numeric type promotion
- Complex nested expressions
- Block expressions with variables
- Diagnostic reporting

Run tests:
```bash
dotnet test Poly.Tests/Poly.Tests.csproj
```

## Future Enhancements

- **Control flow analysis**: Reachability and definite assignment
- **Constant folding**: Compile-time evaluation of constants
- **Optimization passes**: Dead code elimination, expression simplification
- **Additional generators**: IL emission, source code generation
- **Incremental analysis**: LSP integration for real-time validation
