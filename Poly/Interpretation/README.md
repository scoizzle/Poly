# Poly Interpretation System

A fluent, strongly-typed interpretation and expression building system for .NET that compiles to System.Linq.Expressions for optimal runtime performance.

## Overview

The Interpretation system provides a domain-specific language (DSL) for building expression trees through composable, type-safe operators. It bridges the gap between high-level intent and low-level Expression Tree construction, offering:

- **Type-safe composition**: All operations verify type compatibility at build time
- **Fluent API**: Natural method chaining for readable expression construction
- **Expression Tree compilation**: Compiles to optimized IL via System.Linq.Expressions
- **Lexical scoping**: Block-based variable scoping with proper shadowing
- **Automatic type promotion**: Numeric operations follow C# promotion rules

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

### Middleware-Based Interpretation (New)

The middleware interpreter provides a composable, single-pass pipeline for semantic analysis and code generation. This is particularly useful for:
- **Custom Domain Transformers**: Short-circuit standard processing with domain-specific logic
- **Semantic Analysis**: Resolve and cache type information during traversal
- **AST Enrichment**: Annotate nodes with resolved members and type information
- **Code Generation**: Terminal middleware delegates to `ITransformer<TResult>` for flexible output

#### Pipeline Architecture

```
Input AST Node
    ↓
[Middleware 1: SemanticAnalysis]
    (Type resolution, member caching)
    ↓
[Middleware 2: CustomTransformers]
    (Registry-based domain-specific handlers)
    ↓
[Middleware 3: TerminalTransform]
    (Delegates to ITransformer<TResult>)
    ↓
Output Result<TResult>
```

#### Key Components

**TransformationDelegate<TResult>**: Context-first pipeline signature
```csharp
public delegate Task<TResult> TransformationDelegate<TResult>(
    InterpretationContext context,
    Node node,
    TransformationDelegate<TResult> next);
```

**ITransformationMiddleware<TResult>**: Middleware contract
```csharp
public interface ITransformationMiddleware<TResult>
{
    TransformationDelegate<TResult> Build(TransformationDelegate<TResult> next);
}
```

**Semantic Caching**: Type information flows through context without mutating nodes
```csharp
// In middleware
context.SetResolvedType(node, resolvedType);
context.SetResolvedMember(node, resolvedMember);

// In terminal processor or elsewhere
var type = context.GetResolvedType(node);
var member = context.GetResolvedMember(node);
```

**Custom Transformer Registry**: Priority-based, domain-specific handlers
```csharp
var registry = new CustomTransformerRegistry<TResult>();
registry.Register(
    nodeMatcher: n => n is BinaryOp { Operator: "+" },
    transformer: (context, node) => /* custom handling */);

var middleware = new CustomTransformMiddleware<TResult>(registry);
```

#### Quick Start: Middleware Pipeline

```csharp
var context = new InterpretationContext();
var typeProvider = new ClrTypeDefinitionProvider();
context.TypeProvider = typeProvider;

var builder = new InterpreterBuilder<string>(context);
builder
    .Use(new SemanticAnalysisMiddleware())
    .Use(new CustomTransformMiddleware<string>(new CustomTransformerRegistry<string>()))
    .Use(new TerminalTransformMiddleware<string>(customTransformer));

var interpreter = builder.Build();

var ast = new BinaryOp(
    new Literal<int>(10),
    "+",
    new Literal<int>(20));

var result = await interpreter.Transform(ast);
// Result contains semantic info in context for inspection
```

#### Three-Phase Implementation

See [MIDDLEWARE_INTERPRETER_IMPLEMENTATION.md](./MIDDLEWARE_INTERPRETER_IMPLEMENTATION.md) for comprehensive architecture details.

**Phase 1: Core Infrastructure**
- `TransformationDelegate<TResult>` - Context-first pipeline signature
- `ITransformationMiddleware<TResult>` - Middleware contract
- `InterpreterBuilder<TResult>` - Fluent pipeline configuration
- `Interpreter<TResult>` - Orchestrates middleware chain execution
- `InterpretationContext` enhancements - TypeProvider, Variables, ScopeStack, Properties dictionary

**Phase 2: Semantic Analysis Middleware**
- `SemanticAnalysisMiddleware` - Resolves types/members and caches in context
- `SemanticExtensions` - Helper methods for accessing/storing semantic info
- `SemanticInfo` record - Immutable semantic information structure

**Phase 3: Custom & Terminal Transformers**
- `CustomTransformerRegistry<TResult>` - Priority-based handler registry
- `CustomTransformMiddleware<TResult>` - Applies registry transformers
- `TerminalTransformMiddleware<TResult>` - Delegates to `ITransformer<TResult>`

#### Integration with Existing System

The middleware interpreter:
- ✅ Integrates with existing `InterpretationContext`
- ✅ Respects `ITypeDefinitionProvider` for type resolution
- ✅ Delegates terminal processing to standard `ITransformer<TResult>`
- ✅ Does not modify the fluent Value API
- ✅ Allows gradual adoption alongside existing code
- ✅ Provides escape hatches for domain-specific logic via custom transformers

## Current Features

✅ Comprehensive operator set (arithmetic, comparison, boolean, conditional)  
✅ Fluent API for natural composition  
✅ Automatic numeric type promotion in arithmetic  
✅ Block expressions with lexical scoping  
✅ Member access (properties, fields, methods)  
✅ Array and indexer access  
✅ Method invocation  
✅ Type casting with overflow checking  
✅ Null-coalescing operator  
✅ Assignment operations  
✅ Parameter and variable management  

## Middleware vs. Fluent API: When to Use Each

### Use the Fluent Value API When:
- Building expression trees for compilation and execution
- Working with runtime values and parameters
- Need lightweight, familiar LINQ expression semantics
- Building validators, predicates, or calculated fields
- Compiling to IL for high-performance repeated execution
- Working within simple, single-concern transformation logic

**Example**: Building a validation rule
```csharp
var age = context.AddParameter<int>("age");
var validator = age.GreaterThanOrEqual(Value.Wrap(18))
                   .And(age.LessThanOrEqual(Value.Wrap(120)));
var compiled = CompileToFunc<int, bool>(context, validator, age);
```

### Use the Middleware Interpreter When:
- Need multi-stage semantic analysis and enrichment
- Want to support custom, domain-specific transformations
- Building language tools (analyzers, code generators, translators)
- Need to inspect/cache type information during traversal
- Processing ASTs with complex node hierarchies
- Want composable, reusable transformation middleware
- Need to integrate multiple transformation concerns (analysis → custom logic → generation)

**Example**: Building an AST transformer with semantic caching
```csharp
var builder = new InterpreterBuilder<string>(context);
builder
    .Use(new SemanticAnalysisMiddleware())           // Resolve types
    .Use(new CustomTransformMiddleware<string>(reg)) // Domain logic
    .Use(new TerminalTransformMiddleware<string>(codeGen)); // Generate

var interpreter = builder.Build();
var result = await interpreter.Transform(astNode);
```

### Side-by-Side Comparison

| Aspect | Fluent API | Middleware |
|--------|-----------|------------|
| **Entry Point** | `Value.Wrap()`, `Parameter`, `Variable` | `InterpreterBuilder<T>` |
| **Composition** | Method chaining, fluent builders | Middleware pipeline |
| **Type Safety** | Compile-time, Expression Tree validation | Runtime via context semantic cache |
| **Performance** | Compiles to IL (fastest at execution) | Middleware overhead (but single-pass) |
| **Semantic Info** | Limited (GetTypeDefinition on values) | Rich (cached in InterpretationContext) |
| **Extensibility** | Add operators to Value class | Add middleware, custom transformers |
| **Node Structure** | Operator instances (mutable builders) | Immutable record hierarchy |
| **Use Case** | Execution, evaluation | Analysis, transformation, generation |
| **Output** | Expression<T>, compiled delegates | Custom (via ITransformer<TResult>) |

## Future Plans

### High Priority Enhancements

#### 1. Variable Type Safety Validation
**Goal:** Prevent runtime type errors from variable reassignment.

- Store declared type in `Variable` at construction
- Validate type compatibility on reassignment
- Use numeric promotion rules for compatible assignments
- Provide clear error messages for incompatible types

**Impact:** Eliminates entire class of type-safety bugs.

#### 2. Overload Resolution for Methods/Indexers
**Goal:** Support method/indexer overloading with proper resolution.

- Implement C#-style overload resolution with type distance scoring
- Exact match: 0, Numeric promotion: 1, Implicit conversion: 2
- Handle tie-breaking with specificity rules
- Support generic method instantiation

**Impact:** Enables realistic method invocation scenarios.

#### 3. Numeric Promotion in Comparison Operators
**Goal:** Allow mixed-type comparisons (int vs double, etc.).

- Apply `NumericTypePromotion` to all comparison operators
- Promote both operands to common type before comparison
- Consistent behavior with arithmetic operators

**Impact:** Natural comparisons work like C# (x > 5.5 where x is int).

#### 4. Circular Reference Detection
**Goal:** Prevent stack overflow from circular variable references.

- Track visiting variables in `InterpretationContext`
- Detect cycles during `GetTypeDefinition()` / `BuildExpression()`
- Provide clear error with reference chain path
- Use thread-local or context-scoped tracking

**Impact:** Robust error handling, prevents crashes.

#### 5. Member Resolution Disambiguation
**Goal:** Properly handle types with fields, properties, and methods of the same name.

- Prioritize member types: Properties > Fields > Methods
- Document disambiguation behavior
- Support explicit member type selection if needed

**Impact:** Predictable behavior with complex types.

#### 6. Assignment Target Validation
**Goal:** Validate assignment targets at build time.

- Only allow assignment to `Parameter` or mutable `Variable`
- Reject assignments to `Constant`, `Operator`, or uninitialized variables
- Clear error messages for invalid targets

**Impact:** Catch errors early with clear diagnostics.

### Medium Priority Enhancements

#### 7. Multi-Dimensional Array Support
**Goal:** Support multi-dimensional and jagged arrays.

- Accept multiple indices: `arr.Index(i, j)` → `arr[i, j]`
- Use `Expression.ArrayIndex` with rank validation
- Handle both rectangular (`int[,]`) and jagged (`int[][]`) arrays

**Impact:** Full array support for .NET scenarios.

#### 8. Type Definition Lookup Fallbacks
**Goal:** Graceful handling of type resolution failures.

- Fallback chain: registered providers → CLR reflection → error
- Handle generic types, nested types, dynamic assemblies
- Detailed error messages listing where type was searched

**Impact:** Better error messages, robust type resolution.

#### 9. Scope Exception Handling
**Goal:** Properly clean up scope state on exceptions.

- Wrap scope operations in try/catch/finally
- Ensure PopScope() runs even on error
- Consider context reset/recovery methods

**Impact:** Context remains usable after build errors.

### Low Priority / Optimization

#### 10. Expression Caching
**Goal:** Avoid rebuilding identical subexpressions.

- Optional cache in `InterpretationContext`
- Key by (Value identity, context hash)
- Profile first to verify benefit
- Clear cache as needed for memory management

**Impact:** Performance optimization for complex trees (if needed).

### Additional Future Features

- **Bitwise operators**: And, Or, Xor, ShiftLeft, ShiftRight for integral types
- **Type checks**: TypeIs, TypeAs for safe runtime type testing
- **Increment/Decrement**: Pre/post increment and decrement operators
- **Compound assignments**: +=, -=, *=, /=, etc.
- **Loop constructs**: For, While, ForEach with break/continue
- **Lambda expressions**: Nested lambdas and closures
- **Exception handling**: Try/Catch/Finally blocks
- **Collection operations**: NewArray, NewObject, collection initializers
- **LINQ integration**: Select, Where, OrderBy as expression node operators
- **Async support**: Async/await expression building

## Testing

The system has comprehensive test coverage:

- **313 tests** across all operators and features
- Unit tests for each operator type
- Integration tests for complex expression scenarios
- Scope management and variable isolation tests
- Type promotion and compatibility tests
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

## Contributing

### Adding Operators (Fluent API)

When adding new operators or features to the fluent API:

1. Inherit from appropriate base class (`Operator`, `BooleanOperator`, etc.)
2. Implement `GetTypeDefinition()` for type checking
3. Implement `BuildExpression()` for code generation
4. Add fluent methods to `Value` class for composition
5. Write comprehensive tests covering normal and edge cases
6. Document behavior in XML comments
7. Update this README with new capabilities

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
