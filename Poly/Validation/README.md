# Validation System - RuleSet API

The `RuleSet<T>` and `RuleSetBuilder<T>` classes provide a fluent, strongly-typed API for building and executing validation rules.

## Quick Example

```csharp
using Poly.Validation;
using Poly.Validation.Builders;

// Define a record to validate
public record Person(string Name, int Age);

// Build a rule set
RuleSet<Person> ruleSet = new RuleSetBuilder<Person>()
    .Member(p => p.Name, r => r.NotNull().MinLength(1).MaxLength(100))
    .Member(p => p.Age, r => r.Minimum(0).Maximum(150))
    .Build();

// Test instances
Person validPerson = new("Alice", 30);
Console.WriteLine(ruleSet.Test(validPerson));  // True

Person invalidPerson = new("", 200);
Console.WriteLine(ruleSet.Test(invalidPerson));  // False

// View the combined rules
Console.WriteLine(ruleSet.CombinedRules);
// Output: Name: value != null and value.Length >= 1 && value.Length <= 100 and Age: value >= 0 and value <= 150
```

## RuleSet<T>

Represents a compiled set of validation rules for a specific type.

### Properties

- **`Rule CombinedRules`** - The combined rule representing all validation rules
- **`Value RuleSetInterpretation`** - The interpretation tree representation
- **`Expression ExpressionTree`** - The LINQ expression tree representation
- **`Predicate<T> Predicate`** - The compiled predicate function

### Methods

- **`bool Test(T instance)`** - Tests whether an instance satisfies all rules
- **`string ToString()`** - Returns a string representation of the rules

## RuleSetBuilder<T>

Provides a fluent API for building a RuleSet.

### Methods

#### `Member<TProperty>(Expression<Func<T, TProperty>> propertySelector, Action<ConstraintSetBuilder<TProperty>> constraintsBuilder)`

Adds validation rules for a specific property.

```csharp
.Member(p => p.Name, r => r
    .NotNull()
    .MinLength(1)
    .MaxLength(100))
```

#### `AddRule(Rule rule)`

Adds a custom rule to the rule set.

```csharp
.AddRule(new CustomValidationRule())
```

#### `Build()`

Builds the final RuleSet with all configured rules.

```csharp
RuleSet<Person> ruleSet = builder.Build();
```

## Available Constraints

### NotNull

```csharp
.Member(p => p.Name, r => r.NotNull())
```

### Length Constraints (strings and arrays)

```csharp
.Member(p => p.Name, r => r
    .MinLength(1)
    .MaxLength(100))
```

### Numeric Constraints

```csharp
.Member(p => p.Age, r => r
    .Minimum(0)
    .Maximum(150))
```

## How It Works

1. **Build Phase**: The RuleSetBuilder collects property constraints and builds a combined `AndRule`
2. **Compilation Phase**: The RuleSet converts rules into an interpretation tree, then to LINQ expressions
3. **Execution Phase**: The compiled predicate is executed for fast validation

The system uses three representations:
- **Rules**: High-level validation logic
- **Interpretation Trees**: Intermediate representation for building expressions
- **LINQ Expressions**: Compiled to native code for maximum performance

## Performance

The compiled predicates are as fast as hand-written validation code. See `Poly.Benchmarks` for performance comparisons.

## Extending with Custom Rules

You can create custom rules by inheriting from `Rule` and implementing `BuildInterpretationTree`:

```csharp
public class CustomRule : Rule {
    public override Value BuildInterpretationTree(RuleBuildingContext context) {
        // Build your interpretation tree here
    }
}
```

Then add it using `AddRule`:

```csharp
new RuleSetBuilder<Person>()
    .AddRule(new CustomRule())
    .Build();
```
