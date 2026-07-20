# Codegen Sharp Edges — Bugs Found by EF Compilation

**Date:** 2026-07-20
**Trigger:** Building `demo/Poly.RestApi` — the first real-world compilation of
generated C# against a non-trivial consumer (EF Core + ASP.NET).

The unit tests (`CSharpGeneratorTests`) cover individual node types but do not
assert cross-cutting concerns like argument ordering between factory methods and
constructors, or type compatibility of arithmetic on date types. These issues
were **only** discovered when the generated output was compiled as part of a
real project.

## Bug 1: Property ordering — Create factory vs factory method

**File:** `DomainToCSharpExporter.cs` — `AddCreateNavMethod`

**Problem:** `CreateLoans(args)` iterated `targetEntity.Properties` without sorting.
The `Create` factory sorted by `Name` (`OrderBy(p => p.Name)`). Result: positional
argument mismatch.

```
CreateLoans(string status, DateTime checkedOutAt, …)     // unsorted
Loan.Create(DateTime checkedOutAt, …, string status, …)   // sorted by Name
                  ↑  wrong type!
```

**Fix:** Added `.OrderBy(p => p.Name)` in both `AddCreateNavMethod` and the
`CreateEntityInRelationship` handler in `EffectLoweringPass`.

**Lesson:** Any code that enumerates entity properties to build constructor args
**must** sort by property name to match the `Create` factory signature. Search
the codebase for `targetEntity.Properties` and `.Properties.OrderBy` to ensure
consistency.

## Bug 2: Defaulted properties appended to factory args

**File:** `DomainToCSharpExporter.cs` — `AddCreateNavMethod`

**Problem:** After the back-reference auto-wire, the method appended properties
with `DefaultValueConstraint` to `createArgs`. But the `Create` factory does
**not** include defaulted properties as parameters — it sets them directly in the
constructor body from the default expression. The extra arguments caused overload
resolution failure.

```
Fine.Create(amount, paid, reason, patron, DateTime.UtcNow)  // 5 args — WRONG
Fine.Create(amount, paid, reason, patron)                    // 4 args — correct
```

**Fix:** Removed the second loop entirely. Default-constrained properties were
already correctly excluded from `ctorParams` during the property loop (the
`continue` statement).

**Lesson:** Entity properties are split into two categories at construction:
1. **Constructor params** — properties WITHOUT `DefaultValueConstraint`
2. **Default-set in body** — properties WITH `DefaultValueConstraint`

Both the `Create` factory and any code that calls it must agree on which
properties belong to which category.

## Bug 3: DateTime + long is invalid C#

**File:** `EffectLoweringPass.cs` — `Assign` handler

**Problem:** `DueDate + 14L` generated `this.DueDate = this.DueDate + 14L`, which
is invalid in C# (`DateTime + long` has no overload).

```csharp
// Generated:
this.DueDate = this.DueDate + 14L;  // CS0019
```

**Fix:** In the `Assign` effect handler, detect when the target property is a
date/time domain type and the value is an `Add` expression, then emit
`target.AddDays(value)` instead of `target + value`.

```csharp
this.DueDate = this.DueDate.AddDays(14L);  // correct
```

**Lesson:** Domain-level arithmetic (`Number + Number`) maps to different CLR
operators depending on the property's domain type. `DateTime` and `Date` need
`.AddDays()`, not `+`. If `Duration`/`TimeSpan` is added, `+ DateTime` is valid
C# and should use the operator.

## Bug 4: Text defaults were null, not empty string

**File:** `EffectLoweringPass.cs` — `DefaultForDomainType`
**File:** `DomainToCSharpExporter.cs` — `DefaultValueForTypeRef`

**Problem:** Unspecified `Text` properties in factory calls received `null`, which
triggers CS8625 (null literal to non-nullable reference type) and causes EF
validation failure at runtime.

```csharp
// Generated:
Loan.Create(DateTime.MinValue, DateTime.MinValue, DateTime.MinValue, null, 0L, …);
//                                                                  ^^^^ CS8625
```

**Fix:** Return `""` instead of `null` for `Text`/`String` domain types.

```csharp
// Before:
"Text" or "String" => new Constant(null),

// After:
"Text" or "String" => new Constant(""),
```

**Lesson:** In a `#nullable enable` context, `null` is not a valid default for
non-nullable string properties. The domain type `Text` maps to `string` (non-nullable),
so defaults should be `""`. Consider whether `Text?` should map to `string?` for
optional text fields (tracked separately).

## Bug 5: EF parameterless constructor

**File:** `DomainToCSharpExporter.cs` — constructor generation

**Problem:** EF Core cannot bind camelCase constructor parameters (`isbn`) to
PascalCase properties (`ISBN`). While EF can use constructor binding for simple
cases, the mismatch between DomainToCamelCase'd param names and PascalCase
property names breaks materialization.

```csharp
// Generated (doesn't work with EF):
private Book(string author, Genre genre, string isbn, long pages, string title)
{
    this.Author = author;
    this.ISBN = isbn;    // EF can't bind 'isbn' → 'ISBN'
}
```

**Fix:** Emit a private parameterless constructor before the parameterized one.
EF Core uses this for materialization, and private property setters handle the
rest. The unit tests catch this because they never exercised EF.

```csharp
private Book() { /* EF materialization */ }
```

The parameterless constructor triggers CS8618 (non-nullable properties uninitialized),
suppressed at the project level with `<NoWarn>$(NoWarn);CS8618</NoWarn>`.

**Lesson:** EF Core works best with either:
1. A parameterless constructor + property setters
2. A constructor whose parameter names **exactly** match property names (case-sensitive)

Since we prefer camelCase param names (`isbn` vs `ISBN`), option 1 is the right
choice. Keep this decision in mind if adding `required` modifier or record types.

## Bug 6: Required constraint on value types

**File:** `DomainToCSharpExporter.cs` — `BuildCreateConstraintChecks`

**Problem:** The `RequiredConstraint` handler emitted `amount == null` for `Number`
properties, which is always `false` for value types (generates CS0219 unreachable
code warning at best, or confusing no-op at worst).

```csharp
if (amount == null)  // always false for long — pointless
{
    return DomainResult<Fine>.Failure("'Amount' is required.");
}
```

**Fix:** Skip `RequiredConstraint` checks for value types. Only `Text`/`String`
and entity reference types (`Book`, `Patron`, etc.) are nullable and benefit
from runtime required validation.

**Lesson:** The domain type determines runtime semantics. `Required` on a `Number`
property means "must be provided in the constructor args" (compile-time), not
"must be non-null at runtime". The unit tests only checked that the constraint
parses and round-trips, not that the generated validation code compiles.

## Regression prevention

| Check | What it catches | Added? |
|-------|----------------|--------|
| `dotnet build` on generated output | Compilation errors from generated code | Should run in CI |
| Property iteration matches `OrderBy` | Arg order mismatches | Manual audit |
| Value-type defaults are type-correct | CS8625, CS0219 | Needs test |
| EF materialization works | Constructor binding | Needs integration test |

## Code locations summary

| Concern | File | Methods |
|---------|------|---------|
| Factory arg ordering | `DomainToCSharpExporter.cs` | `AddCreateNavMethod` (line ~654) |
| Factory arg ordering | `EffectLoweringPass.cs` | `CreateEntityInRelationship` (line ~287), `BuildConstructorArgs` (line ~369) |
| DateTime arithmetic | `EffectLoweringPass.cs` | `Assign` (line ~60) |
| Default values | `EffectLoweringPass.cs` | `DefaultForDomainType` (line ~410) |
| Default values | `DomainToCSharpExporter.cs` | `DefaultValueForTypeRef` (line ~760) |
| Parameterless ctor | `DomainToCSharpExporter.cs` | `BuildTypeDefsForEntity` constructor section (line ~508) |
| Constraint null-check | `DomainToCSharpExporter.cs` | `BuildCreateConstraintChecks` (line ~800) |
