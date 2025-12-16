# Fluent Interpretation API

The Poly Interpretation system now includes a comprehensive fluent API for building complex interpretable expressions. This API enables natural composition of operators through method chaining on `Value` instances.

## Overview

The fluent API provides factory methods on the `Value` class that create operator instances. This enables readable, composable expression building without directly constructing operator objects.

## Quick Start

```csharp
using Poly.Interpretation;
using System.Linq.Expressions;

var context = new InterpretationContext();
var x = context.AddParameter<int>("x");

// Fluent: x * 2 + 5
var expr = x.Multiply(Value.Wrap(2)).Add(Value.Wrap(5));

// Compile and execute
var expression = expr.BuildExpression(context);
var lambda = Expression.Lambda<Func<int, int>>(expression, x.BuildExpression(context));
var compiled = lambda.Compile();

Console.WriteLine(compiled(10)); // Output: 25
```

## Available Operations

### Member Access

- **`GetMember(string memberName)`** - Access property or field
  ```csharp
  var length = str.GetMember("Length");
  ```

- **`Index(params Value[] indices)`** - Access indexer
  ```csharp
  var firstItem = list.Index(Value.Wrap(0));
  ```

- **`Invoke(params Value[] arguments)`** - Call method
  ```csharp
  var result = method.Invoke(Value.Wrap(arg1), Value.Wrap(arg2));
  ```

### Arithmetic Operations

- **`Add(Value other)`** - Addition (+)
- **`Subtract(Value other)`** - Subtraction (-)
- **`Multiply(Value other)`** - Multiplication (*)
- **`Divide(Value other)`** - Division (/)
- **`Modulo(Value other)`** - Modulo (%)
- **`Negate()`** - Unary negation (-)

```csharp
// (x + 10) * 2 - 5
var result = x.Add(Value.Wrap(10))
              .Multiply(Value.Wrap(2))
              .Subtract(Value.Wrap(5));
```

**Type Promotion:** Arithmetic operators automatically promote operands to a common type following C# rules (decimal > double > float > ulong > long > uint > int).

```csharp
var intValue = Value.Wrap(10);
var doubleValue = Value.Wrap(3.5);
var result = intValue.Add(doubleValue); // Result is double (13.5)
```

### Comparison Operations

- **`GreaterThan(Value other)`** - Greater than (>)
- **`GreaterThanOrEqual(Value other)`** - Greater than or equal (>=)
- **`LessThan(Value other)`** - Less than (<)
- **`LessThanOrEqual(Value other)`** - Less than or equal (<=)

```csharp
// x > 100
var condition = x.GreaterThan(Value.Wrap(100));
```

### Equality Operations

- **`Equal(Value other)`** - Equality (==)
- **`NotEqual(Value other)`** - Inequality (!=)

```csharp
// x == 42
var isEqual = x.Equal(Value.Wrap(42));
```

### Boolean Operations

- **`And(Value other)`** - Logical AND (&&)
- **`Or(Value other)`** - Logical OR (||)
- **`Not()`** - Logical NOT (!)

```csharp
// x > 10 && x < 100
var condition = x.GreaterThan(Value.Wrap(10))
                 .And(x.LessThan(Value.Wrap(100)));
```

### Conditional Operations

- **`Conditional(Value ifTrue, Value ifFalse)`** - Ternary operator (?:)
  ```csharp
  // x > 100 ? x * 2 : x + 10
  var result = x.GreaterThan(Value.Wrap(100))
                .Conditional(
                    x.Multiply(Value.Wrap(2)),
                    x.Add(Value.Wrap(10))
                );
  ```

- **`Coalesce(Value fallback)`** - Null-coalescing operator (??)
  ```csharp
  // x ?? 42
  var result = x.Coalesce(Value.Wrap(42));
  ```

### Type Operations

- **`CastTo(ITypeDefinition targetType)`** - Explicit type cast
  ```csharp
  var doubleType = context.GetTypeDefinition<double>()!;
  // (double)x + 0.5
  var result = x.CastTo(doubleType).Add(Value.Wrap(0.5));
  ```

- **`CastTo(ITypeDefinition targetType, bool checked)`** - Cast with overflow checking
  ```csharp
  // Checked cast throws on overflow
  var result = largeValue.CastTo(intType, checked: true);
  ```

### Assignment

- **`Assign(Value value)`** - Assignment (=)
  ```csharp
  var assignment = variable.Assign(Value.Wrap(42));
  ```

## Complex Examples

### Multi-Condition Logic

```csharp
var context = new InterpretationContext();
var x = context.AddParameter<int>("x");
var y = context.AddParameter<int>("y");

// (x + y) > 50 && (x * y) < 1000
var sum = x.Add(y);
var product = x.Multiply(y);
var condition = sum.GreaterThan(Value.Wrap(50))
                   .And(product.LessThan(Value.Wrap(1000)));
```

### Nested Conditionals

```csharp
// x > 100 ? (x > 200 ? "high" : "medium") : "low"
var high = Value.Wrap("high");
var medium = Value.Wrap("medium");
var low = Value.Wrap("low");

var result = x.GreaterThan(Value.Wrap(100))
              .Conditional(
                  x.GreaterThan(Value.Wrap(200))
                   .Conditional(high, medium),
                  low
              );
```

### Member and Index Chaining

```csharp
var list = context.AddParameter<List<int>>("list");

// list[0] + list.Count
var firstElement = list.Index(Value.Wrap(0));
var count = list.GetMember("Count");
var result = firstElement.Add(count);
```

### Type Conversion Pipeline

```csharp
var intValue = context.AddParameter<int>("value");
var doubleType = context.GetTypeDefinition<double>()!;

// ((double)value + 0.5) * 2.0
var result = intValue.CastTo(doubleType)
                     .Add(Value.Wrap(0.5))
                     .Multiply(Value.Wrap(2.0));
```

## Design Patterns

### Expression Builder Pattern

```csharp
public static Value BuildDiscountCalculation(Parameter totalPrice, Parameter customerTier) {
    var goldDiscount = totalPrice.Multiply(Value.Wrap(0.80)); // 20% off
    var silverDiscount = totalPrice.Multiply(Value.Wrap(0.90)); // 10% off
    var regularPrice = totalPrice;
    
    return customerTier.Equal(Value.Wrap("Gold"))
        .Conditional(
            goldDiscount,
            customerTier.Equal(Value.Wrap("Silver"))
                .Conditional(silverDiscount, regularPrice)
        );
}
```

### Validation Rules

```csharp
public static Value BuildAgeValidation(Parameter age) {
    // age >= 18 && age <= 120
    return age.GreaterThanOrEqual(Value.Wrap(18))
              .And(age.LessThanOrEqual(Value.Wrap(120)));
}
```

### Null Safety

```csharp
public static Value SafeStringLength(Parameter str) {
    // str?.Length ?? 0
    var length = str.GetMember("Length");
    return length.Coalesce(Value.Wrap(0));
}
```

## Operator Implementations

All fluent methods map to strongly-typed operator classes:

| Fluent Method | Operator Class | Expression Tree |
|--------------|----------------|-----------------|
| `Add` | `Poly.Interpretation.Operators.Arithmetic.Add` | `Expression.Add` |
| `Subtract` | `Poly.Interpretation.Operators.Arithmetic.Subtract` | `Expression.Subtract` |
| `Multiply` | `Poly.Interpretation.Operators.Arithmetic.Multiply` | `Expression.Multiply` |
| `Divide` | `Poly.Interpretation.Operators.Arithmetic.Divide` | `Expression.Divide` |
| `Modulo` | `Poly.Interpretation.Operators.Arithmetic.Modulo` | `Expression.Modulo` |
| `Negate` | `Poly.Interpretation.Operators.Arithmetic.UnaryMinus` | `Expression.Negate` |
| `GreaterThan` | `Poly.Interpretation.Operators.Comparison.GreaterThan` | `Expression.GreaterThan` |
| `LessThan` | `Poly.Interpretation.Operators.Comparison.LessThan` | `Expression.LessThan` |
| `Equal` | `Poly.Interpretation.Operators.Comparison.Equal` | `Expression.Equal` |
| `NotEqual` | `Poly.Interpretation.Operators.Comparison.NotEqual` | `Expression.NotEqual` |
| `And` | `Poly.Interpretation.Operators.BooleanLogic.And` | `Expression.AndAlso` |
| `Or` | `Poly.Interpretation.Operators.BooleanLogic.Or` | `Expression.OrElse` |
| `Not` | `Poly.Interpretation.Operators.BooleanLogic.Not` | `Expression.Not` |
| `Conditional` | `Poly.Interpretation.Operators.Conditional` | `Expression.Condition` |
| `Coalesce` | `Poly.Interpretation.Operators.Coalesce` | `Expression.Coalesce` |
| `CastTo` | `Poly.Interpretation.Operators.TypeCast` | `Expression.Convert` |
| `GetMember` | `Poly.Interpretation.Operators.MemberAccess` | `Expression.Property/Field` |
| `Index` | `Poly.Interpretation.Operators.IndexAccess` | `Expression.Property` (indexer) |
| `Invoke` | `Poly.Interpretation.Operators.Invocation` | `Expression.Call` |
| `Assign` | `Poly.Interpretation.Operators.Assignment` | `Expression.Assign` |

## Testing

The fluent API is comprehensively tested with 11 test cases in [FluentValueApiTests.cs](../Poly.Tests/Interpretation/FluentValueApiTests.cs):

- Arithmetic chaining
- Comparison operations
- Boolean logic
- Conditional expressions
- Null coalescing
- Type casting
- Negation
- Member access
- Index access
- NOT operations
- Complex multi-operator expressions

All 308 tests in the suite pass, including 59 new tests added for operators and fluent API functionality.

## Examples

See [FluentApiExample.cs](../Poly.Benchmarks/FluentApiExample.cs) for runnable demonstrations of all fluent API features.

## Benefits

1. **Readability**: Natural method chaining mirrors mathematical and logical notation
2. **Type Safety**: Compile-time type checking for all operations
3. **Composability**: Expressions build incrementally without nested constructors
4. **Discoverability**: IDE autocomplete reveals available operations
5. **Maintainability**: Fluent chains are easier to modify than nested operator trees

## Migration Guide

**Before (direct operator construction):**
```csharp
var expr = new Add(
    new Multiply(x, new Constant(2)),
    new Constant(5)
);
```

**After (fluent API):**
```csharp
var expr = x.Multiply(Value.Wrap(2)).Add(Value.Wrap(5));
```

The fluent API is fully compatible with existing code — both styles can be mixed in the same project.
