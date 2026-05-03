# Validation System

`Poly.Validation` composes validation logic as rule objects, converts those rules into AST nodes, runs semantic analysis, then compiles to an executable `Predicate<T>`.

## Core Types

- `Rule` - abstract base type for all validation rules
- `RuleSet<T>` - compiles a set of `Rule` instances into a callable predicate
- `RuleBuildingContext` - carries the current value scope while building rule AST

## RuleSet<T>

`RuleSet<T>` is created directly from rules.

```csharp
using Poly.Validation;
using Poly.Validation.Rules;

var rules = new Rule[] {
    new ComparisonRule("Start", ComparisonOperator.LessThanOrEqual, "End"),
    new ComparisonRule("Total", ComparisonOperator.GreaterThanOrEqual, "Subtotal")
};

var ruleSet = new RuleSet<OrderWindow>(rules);

bool isValid = ruleSet.Test(instance);
```

### Exposed Members

- `CombinedRules` - `AndRule` containing all supplied rules
- `RuleSetInterpretation` - AST (`Node`) generated from rules
- `NodeTree` - LINQ expression tree representation
- `Predicate` - compiled `Predicate<T>` delegate
- `Test(T instance)` - executes validation

## Available Rule Types

Located in `Poly/Validation/Rules`:

- `AndRule`
- `OrRule`
- `NotRule`
- `ComparisonRule`
- `ConditionalRule`
- `PropertyDependencyRule`
- `MutualExclusionRule`
- `ComputedValueRule`
- `PropertyConstraintRule`

## Authoring Custom Rules

Derive from `Rule` and implement `BuildInterpretationTree`.

```csharp
using Poly.Syntax;

public sealed class AlwaysTrueRule : Rule {
    public override Node BuildInterpretationTree(RuleBuildingContext context) {
        return new Constant(true);
    }
}
```

## Serialization

`Rule` uses polymorphic JSON serialization via `[JsonPolymorphic]` and `[JsonDerivedType]` declarations on `Rule.cs`.

When adding a new rule subtype:

1. Add the rule class under `Poly/Validation/Rules`.
2. Register the subtype discriminator in `Rule.cs`.

## Execution Pipeline

`RuleSet<T>` performs these steps:

1. Build a combined rule tree (`AndRule`).
2. Build AST (`Node`) through `BuildInterpretationTree`.
3. Analyze AST using `AnalyzerBuilder` with semantic passes.
4. Compile analyzed AST to LINQ expression.
5. Compile to `Predicate<T>`.
