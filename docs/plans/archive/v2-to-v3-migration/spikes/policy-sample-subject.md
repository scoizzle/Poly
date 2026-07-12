# Policy Evaluation — Sample Subject from Property Bag

**Date:** 2026-07-10  
**Status:** Spike complete  
**Source:** `Poly.Tests/DomainModeling/Lowering/PolicySampleSubjectSpikeTests.cs`

## Question

How should MCP (and tests) build a CLR subject for `PolicyEvaluator` from a
property name → value bag, so `evaluate_policy` can call the VM without
inventing domain opcodes?

## Results

| Approach | VM works? | Notes |
|----------|-----------|-------|
| **Anonymous type** (`new { Age = 25 }`) | ✅ Yes | Baseline — proven in existing tests. Not usable from MCP (compile-time only). |
| **`Dictionary<string, object>`** → `PolicySubject.FromDictionary()` | ✅ Yes via facade | Raw dict silently gives wrong results. Use **`PolicySubject.FromDictionary(dict)`** which emits a CLR type via Reflection.Emit with correctly-typed properties, key lookup, and default(T) fallback. |
| **`ExpandoObject`** | ❌ Wrong results | Dynamic properties invisible to CLR reflection — `Member` can't find them. No facade available. |
| **Custom sealed record** (`PersonRecord`) | ✅ Yes | Baseline — proven in existing tests. |
| **PropBag with nullable properties** (`int? Age`) | ✅ With non-null values | Non-null `int?` works. **Null `int?` throws on VM** (unboxing failure). |
| **PropBag with non-nullable properties** (`int Age`) | ✅ Yes | Simplest working bag — default `0` for absent values. |
| **PolicyEvaluator with PropBag** | ✅ Yes | `CompileVMPredicate<PropBag>` works correctly. |

## Recommendation

### Primary (proven ✅) — `PolicySubject.FromDictionary()`

Use `PolicySubject.FromDictionary(dict)` to convert any `Dictionary<string, object?>`
into a valid VM subject. The helper emits a CLR type via `Reflection.Emit` with
one property per dictionary key, each returning the value if present or `default(T)`
if absent. Types are inferred from non-null values.

```csharp
var subject = PolicySubject.FromDictionary(new Dictionary<string, object?>
{
    ["Age"] = 25,
    ["Name"] = "Alice",
    ["Status"] = "Active"
});
var result = policy.Evaluate(subject); // works on VM
```

**Key constraints:**
- Property names must match between entity definition and dictionary keys.
- Non-null values in the dictionary define the property types.
- Null values are filtered out; missing keys return `default(T)`.  
- Nullable value types (`int?`, `decimal?`) with null values **must not** be passed — filter them first.

### Secondary (proven ✅) — Pre-built sealed records

`PolicySubject.StrictBag`, `SampleAgeSubject` — lightweight alternatives when the
property set is known at compile time. Proven on both VM and LINQ paths.

### Does NOT work (invariant)

- **Raw `Dictionary<string, object>` or `ExpandoObject`** — Member resolution doesn't
  map to dictionary keys/dynamic properties. Always wrap via `FromDictionary()`.
- **Nullable value types with null** (`int?` set to null) — VM throws on unbox.

## Files

- Spike tests: `Poly.Tests/DomainModeling/Lowering/PolicySampleSubjectSpikeTests.cs`
- Subject helper: `Poly/DomainModeling/Lowering/PolicySubject.cs` (`FromDictionary`, `Validate`, `StrictBag`, etc.)
- Emit spike tests: `Poly.Tests/DomainModeling/Lowering/ReflectionEmitSubjectSpikeTests.cs` (5 tests)
- Invariant tests: `Poly.Tests/DomainModeling/Lowering/PolicySubjectInvariantTests.cs` (17 tests)

## Related

- `docs/plans/v2-to-v3/simple-agent-tasks/ws8-spike-policy-sample-subject.md` (task)
- `Poly/DomainModeling/Lowering/PolicyEvaluator.cs` (VM-primary eval)
