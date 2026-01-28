# Poly Interpretation System

A fluent, strongly-typed analysis and code generation system for .NET that compiles AST expressions to System.Linq.Expressions for optimal runtime performance.

## Overview

The Interpretation system provides a domain-specific language (DSL) for building and analyzing expression trees through composable, type-safe operators. It follows a two-phase architecture:

1. **Analysis Phase**: Semantic analysis passes validate and annotate the AST
2. **Generation Phase**: Code generators transform analyzed AST into executable artifacts

### Key Features

- **Type-safe composition**: All operations verify type compatibility during analysis
- **Fluent API**: Natural method chaining for readable expression construction
- **Expression Tree compilation**: Compiles to optimized IL via System.Linq.Expressions
- **Lexical scoping**: Block-based variable scoping with proper shadowing
- **Automatic type promotion**: Numeric operations follow C# promotion rules
- **Diagnostic reporting**: Errors, warnings, and hints collected during analysis

## Architecture

### Analysis System

The analysis system validates and annotates AST nodes with semantic information:

```csharp
var analyzer = new AnalyzerBuilder()
    .UseTypeResolver()       // Resolves types for all nodes
    .UseMemberResolver()     // Resolves properties, methods, indexers
    .UseVariableScopeValidator()  // Validates variable declarations and references
    .Build();

var result = analyzer.Analyze(astNode);

// Check for diagnostics
if (result.Context.Diagnostics.Any())
{
    foreach (var diagnostic in result.Context.Diagnostics)
    {
        Console.WriteLine($"{diagnostic.Severity}: {diagnostic.Message}");
    }
}
```

**Analysis Passes:**
- **TypeResolver**: Infers and validates types for constants, variables, operations, member access
- **MemberResolver**: Resolves property/method/indexer access with type checking
- **ScopeValidator**: Tracks variable lifetime, detects undeclared variables and shadowing

### Code Generation System

The `LinqExpressionGenerator` transforms analyzed AST into System.Linq.Expressions:

```csharp
var generator = new LinqExpressionGenerator(analysisResult);

// Compile to Expression
Expression expr = generator.Compile(astNode);

// Or compile directly to delegate
var param = new Parameter("x", TypeReference.To<int>());
Func<int, int> compiled = (Func<int, int>)generator.CompileAsDelegate(astNode, param);

int result = compiled(42);
```

## Core Concepts

### Values
All expression nodes inherit from `Value`, which represents typed data or operations that produce typed results:

- **`Literal<T>`**: Constant values known at interpretation time
- **`Parameter`**: Lambda parameters (inputs to compiled expressions)
- **`Variable`**: Named references in lexical scopes
- **`Operator`**: Operations that transform or combine values

### Operators

Operators are composable building blocks organized by category:

#### Arithmetic
- `Add`, `Subtract`, `Multiply`, `Divide`, `Modulo`
- `UnaryMinus` (negation)
- Automatic numeric type promotion (int + double → double)

#### Comparison
- `GreaterThan`, `GreaterThanOrEqual`, `LessThan`, `LessThanOrEqual`

#### Equality
- `Equal`, `NotEqual`

#### Boolean Logic
- `And`, `Or`, `Not`
- Short-circuit evaluation

#### Conditional
- `Conditional` (ternary: `condition ? ifTrue : ifFalse`)
- `Coalesce` (null-coalescing: `value ?? fallback`)

#### Type Operations
- `TypeCast`: Explicit type conversion with optional overflow checking

#### Control Flow
- `Block`: Statement sequences with local variables and scoping
- `Assignment`: Value assignment

#### Member Access
- `MemberAccess`: Property/field/method access by name
- `IndexAccess`: Array and indexer access
- `InvocationOperator`: Method invocation with arguments

### Interpretation Context

`InterpretationContext` manages:
- **Type definitions**: Resolves CLR types to `ITypeDefinition` abstractions
- **Parameters**: Registers lambda parameters
- **Variable scopes**: Stack-based lexical scoping with push/pop
- **Type providers**: Extensible type resolution system

## Quick Start

### Basic Arithmetic

```csharp
var context = new InterpretationContext();
var x = context.AddParameter<int>("x");

// Build: x * 2 + 5
var expr = x.Multiply(Value.Wrap(2)).Add(Value.Wrap(5));

// Compile to lambda
var expression = expr.BuildExpression(context);
var lambda = Expression.Lambda<Func<int, int>>(expression, x.BuildExpression(context));
var compiled = lambda.Compile();

Console.WriteLine(compiled(10)); // Output: 25
```

### Conditional Logic

```csharp
var context = new InterpretationContext();
var x = context.AddParameter<int>("x");

// Build: x > 100 ? x * 2 : x + 10
var condition = x.GreaterThan(Value.Wrap(100));
var ifTrue = x.Multiply(Value.Wrap(2));
var ifFalse = x.Add(Value.Wrap(10));
var expr = condition.Conditional(ifTrue, ifFalse);

var compiled = CompileToFunc<int, int>(context, expr, x);
Console.WriteLine(compiled(150)); // Output: 300
Console.WriteLine(compiled(50));  // Output: 60
```

### Complex Expressions

```csharp
var context = new InterpretationContext();
var x = context.AddParameter<int>("x");
var y = context.AddParameter<int>("y");

// Build: (x + y) > 50 && (x * y) < 1000
var sum = x.Add(y);
var product = x.Multiply(y);
var condition1 = sum.GreaterThan(Value.Wrap(50));
var condition2 = product.LessThan(Value.Wrap(1000));
var expr = condition1.And(condition2);

var compiled = CompileToFunc<int, int, bool>(context, expr, x, y);
Console.WriteLine(compiled(30, 30)); // true (60 > 50 && 900 < 1000)
Console.WriteLine(compiled(10, 10)); // false (20 < 50)
```

### Blocks and Scoping

```csharp
var context = new InterpretationContext();
var x = context.AddParameter<int>("x");

// Block automatically manages scope
var block = new Block(
    x.Add(Value.Wrap(5)),
    x.Multiply(Value.Wrap(2)),
    x.Subtract(Value.Wrap(3))  // Last expression is the return value
);

var compiled = CompileToFunc<int, int>(context, block, x);
Console.WriteLine(compiled(10)); // Output: 7
```

### Member and Index Access

```csharp
var context = new InterpretationContext();
var str = context.AddParameter<string>("str");

// str.Length + 1
var length = str.GetMember("Length");
var result = length.Add(Value.Wrap(1));

var compiled = CompileToFunc<string, int>(context, result, str);
Console.WriteLine(compiled("hello")); // Output: 6

// Array/list indexing
var list = context.AddParameter<int[]>("arr");
var firstElement = list.Index(Value.Wrap(0));
```

## Fluent API Reference

### All Available Methods on Value

**Member Access:**
- `GetMember(string memberName)` - Access property/field by name
- `Index(params Value[] indices)` - Array/indexer access
- `Invoke(string methodName, params Value[] arguments)` - Method invocation

**Arithmetic:**
- `Add(Value other)`, `Subtract(Value other)`, `Multiply(Value other)`, `Divide(Value other)`, `Modulo(Value other)`, `Negate()`

**Comparison:**
- `GreaterThan(Value other)`, `GreaterThanOrEqual(Value other)`, `LessThan(Value other)`, `LessThanOrEqual(Value other)`

**Equality:**
- `Equal(Value other)`, `NotEqual(Value other)`

**Boolean:**
- `And(Value other)`, `Or(Value other)`, `Not()`

**Conditional:**
- `Conditional(Value ifTrue, Value ifFalse)` - Ternary operator
- `Coalesce(Value fallback)` - Null-coalescing

**Type Operations:**
- `CastTo(ITypeDefinition targetType, bool isChecked = false)` - Type cast

**Assignment:**
- `Assign(Value value)` - Assignment

### Design Patterns

**Expression Builder Pattern:**
```csharp
public static Value BuildDiscountCalculation(Parameter totalPrice, Parameter customerTier) {
    var goldDiscount = totalPrice.Multiply(Value.Wrap(0.80)); // 20% off
    var silverDiscount = totalPrice.Multiply(Value.Wrap(0.90)); // 10% off
    
    return customerTier.Equal(Value.Wrap("Gold"))
        .Conditional(goldDiscount,
            customerTier.Equal(Value.Wrap("Silver"))
                .Conditional(silverDiscount, totalPrice));
}
```

**Validation Rules:**
```csharp
public static Value BuildAgeValidation(Parameter age) {
    // age >= 18 && age <= 120
    return age.GreaterThanOrEqual(Value.Wrap(18))
              .And(age.LessThanOrEqual(Value.Wrap(120)));
}
```

### Operator Mapping

| Fluent Method | Operator Class | Expression Tree |
|--------------|----------------|-----------------|
| `Add` | `Operators.Arithmetic.Add` | `Expression.Add` |
| `Subtract` | `Operators.Arithmetic.Subtract` | `Expression.Subtract` |
| `Multiply` | `Operators.Arithmetic.Multiply` | `Expression.Multiply` |
| `Divide` | `Operators.Arithmetic.Divide` | `Expression.Divide` |
| `Modulo` | `Operators.Arithmetic.Modulo` | `Expression.Modulo` |
| `Negate` | `Operators.Arithmetic.UnaryMinus` | `Expression.Negate` |
| `GreaterThan` | `Operators.Comparison.GreaterThan` | `Expression.GreaterThan` |
| `LessThan` | `Operators.Comparison.LessThan` | `Expression.LessThan` |
| `Equal` | `Operators.Equality.Equal` | `Expression.Equal` |
| `NotEqual` | `Operators.Equality.NotEqual` | `Expression.NotEqual` |
| `And` | `Operators.Boolean.And` | `Expression.AndAlso` |
| `Or` | `Operators.Boolean.Or` | `Expression.OrElse` |
| `Not` | `Operators.Boolean.Not` | `Expression.Not` |
| `Conditional` | `Operators.Conditional` | `Expression.Condition` |
| `Coalesce` | `Operators.Coalesce` | `Expression.Coalesce` |
| `CastTo` | `Operators.TypeCast` | `Expression.Convert` |
| `GetMember` | `Operators.MemberAccess` | `Expression.Property/Field` |
| `Index` | `Operators.IndexAccess` | `Expression.ArrayIndex` |
| `Invoke` | `Operators.InvocationOperator` | `Expression.Call` |
| `Assign` | `Operators.Assignment` | `Expression.Assign` |

### Benefits of the Fluent API

1. **Readability**: Natural method chaining mirrors mathematical and logical notation
2. **Type Safety**: Compile-time type checking for all operations
3. **Composability**: Expressions build incrementally without nested constructors
4. **Discoverability**: IDE autocomplete reveals available operations
5. **Maintainability**: Fluent chains are easier to modify than nested operator trees

**Migration from direct construction:**
```csharp
// Before
var expr = new Add(new Multiply(x, new Literal<int>(2)), new Literal<int>(5));

// After (fluent)
var expr = x.Multiply(Value.Wrap(2)).Add(Value.Wrap(5));
```

## Architecture

### Two-Layer Interpretation System

The Interpretation module provides two complementary approaches:

1. **Fluent Value API** (Classic) - Lightweight, type-safe expression building
2. **Middleware Interpreter** (Modern) - Composable, semantic-aware AST transformation pipeline

Both approaches integrate seamlessly with Poly's introspection and validation layers.

### Type System Integration

The system integrates with Poly's introspection layer through `ITypeDefinition`:

```
InterpretationContext
    ↓
TypeDefinitionProviderCollection
    ↓
ClrTypeDefinitionRegistry → ClrTypeDefinition → ClrTypeMember
```

This abstraction layer enables:
- Type resolution across different type systems
- Extensible type providers for custom types
- Unified handling of CLR types, data model types, and custom definitions

### Expression Building Flow (Fluent API)

1. **Parse/Compose**: Build operator tree using fluent API
2. **Type Check**: `GetTypeDefinition()` validates types without side effects
3. **Build**: `BuildExpression()` generates Expression Tree nodes
4. **Compile**: Expression.Lambda compiles to optimized IL
5. **Execute**: Compiled lambda executes at native speed

### Scope Management

Variable scopes form a parent-child chain:

```
GlobalScope (contains parameters)
    ↓
Block Scope 1
    ↓
Block Scope 2 (nested)
```

- Variables declared in inner scopes shadow outer ones
- `GetVariable()` searches current → parent → ... → global
- `PushScope()` / `PopScope()` manage the scope stack
- Blocks automatically manage their scope lifecycle

## Helper Methods

For easier testing and usage, use the helper extension methods:

```csharp
using Poly.Tests.TestHelpers;

// BuildExpression - analyzes and generates Expression
var expr = astNode.BuildExpression();
var lambda = Expression.Lambda<Func<int>>(expr);
int result = lambda.Compile()();

// BuildExpressionWithParameters - pre-registers parameter types
var param = new Parameter("x");
var expr = astNode.BuildExpressionWithParameters((param, typeof(int)));
var lambda = Expression.Lambda<Func<int, int>>(expr, /* parameter expressions */);

// CompileLambda - builds and compiles in one step
var compiled = astNode.CompileLambda<Func<int, int>>((param, typeof(int)));
int result = compiled(42);
```

## Testing

The system has comprehensive test coverage:

- **383 tests** across all operators and features
- Unit tests for each operator type and analysis pass
- Integration tests for complex expression scenarios
- Scope management and variable isolation tests
- Type promotion and compatibility tests
- Diagnostic collection and error reporting tests
- Edge case and error condition coverage

Run tests:
```bash
cd Poly.Tests
dotnet test
```

## Performance

Expression trees compile to optimized IL, offering:
- **Native execution speed** after compilation
- **One-time compilation cost** per expression
- **Reusable compiled delegates** for repeated evaluation
- **Zero reflection overhead** at execution time

For repeated evaluations, always compile once and reuse the delegate:
```csharp
var compiled = lambda.Compile();  // Once
for (int i = 0; i < 1000000; i++) {
    var result = compiled(i);  // Fast
}
```

## Future Enhancements

### High Priority
- **Circular reference detection**: Prevent stack overflow from variable cycles
- **Enhanced diagnostics**: Better error messages with source location tracking
- **Incremental analysis**: LSP integration for real-time validation

### Medium Priority  
- **Control flow analysis**: Reachability and definite assignment checking
- **Constant folding**: Compile-time evaluation of constant expressions
- **Multi-dimensional arrays**: Full support for rectangular and jagged arrays

### Future Features
- **Bitwise operators**: And, Or, Xor, ShiftLeft, ShiftRight
- **Type checks**: TypeIs, TypeAs for runtime type testing
- **Loop constructs**: For, While, ForEach with break/continue
- **Exception handling**: Try/Catch/Finally blocks
- **Async support**: Async/await expression building

## Contributing

When adding new operators or features:

1. Define AST node class inheriting from `Node`
2. Implement `Children` property for traversal
3. Add analysis logic to appropriate `INodeAnalyzer` pass
4. Add code generation logic to `LinqExpressionGenerator`
5. Write comprehensive tests covering normal and edge cases
6. Update this README with new capabilities

### Extending the Middleware Interpreter

To add custom transformation logic:

1. **Create a middleware**: Implement `ITransformationMiddleware<TResult>` with `Build()` method
   ```csharp
   public class MyAnalysisMiddleware : ITransformationMiddleware<string>
   {
       public TransformationDelegate<string> Build(TransformationDelegate<string> next)
       {
           return async (context, node, _) => {
               // Pre-processing: analyze node
               var type = context.TypeProvider.GetTypeDefinition(node.GetType());
               
               // Call next middleware
               var result = await next(context, node, next);
               
               // Post-processing if needed
               return result;
           };
       }
   }
   ```

2. **Register custom transformers**: Use `CustomTransformerRegistry<TResult>`
   ```csharp
   var registry = new CustomTransformerRegistry<TResult>();
   registry.Register(
       nodeMatcher: n => n is SpecialNode { Property: "value" },
       transformer: async (context, node) => { /* custom logic */ },
       priority: 10); // Higher priority = earlier execution
   ```

3. **Add to pipeline**: Wire middleware into `InterpreterBuilder<TResult>`
   ```csharp
   builder.Use(new MyAnalysisMiddleware());
   ```

4. **Test thoroughly**: Create integration tests demonstrating the middleware behavior
5. **Document**: Explain the middleware's purpose and semantic guarantees
6. **Reference**: Update this README with examples of your middleware

## Examples

See [FluentApiExample.cs](../../Poly.Benchmarks/FluentApiExample.cs) for runnable demonstrations of all features.

## References

- [System.Linq.Expressions Documentation](https://docs.microsoft.com/en-us/dotnet/api/system.linq.expressions)
- [Expression Trees in C#](https://docs.microsoft.com/en-us/dotnet/csharp/programming-guide/concepts/expression-trees/)
- [Middleware Interpreter Implementation](./MIDDLEWARE_INTERPRETER_IMPLEMENTATION.md) - Detailed architecture and three-phase implementation plan
