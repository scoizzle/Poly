# Task 2: Static Array Type Specialization

**Goal:** Eliminate the runtime `TypeIs` check in `EmitIndexAccess` by using analysis-resolved type metadata to determine the array backing at compile time.

## Current Behavior

`EmitIndexAccess` emits a runtime type check on every array read:
```csharp
var rawObj = Heap.UnsafeGet(arrHandle);  // object? from heap
// Runtime type check:
if (rawObj is long[])    // read directly
    result = longArr[idx];
else                     // read and unbox
    result = (long)objArr[idx];
```

This costs:
- Heap dereference (`Heap.UnsafeGet`)
- `TypeIs` runtime check
- Two possible branches (one always untaken per execution)
- For nqueens (tight loop with many array accesses), this overhead dominates

## Target Behavior

When the analysis pipeline resolves the array's element type:
```csharp
// Known at compile time:
if (elementType.IsValueType)
    result = ((long[])Heap.UnsafeGet(arrHandle))[idx];  // direct long[]
else
    result = (long)((object[])Heap.UnsafeGet(arrHandle))[idx];  // direct object[]
```

No runtime type check. The JIT compiles the correct path directly.

## Implementation

### Step 1: Add element type resolution helper

In `EmitIndexAccess`, before the runtime type check:
```csharp
// Try to resolve array element type from analysis metadata
Type? elementType = null;
if (ctx.Analysis?.GetResolvedType(n) is ClrTypeDefinition clrElem)
    elementType = clrElem.RuntimeType;
```

The `GetResolvedType(IndexAccess)` returns the element type (e.g., `typeof(long)` for `long[]`).

### Step 2: Emit direct access when type is known

```csharp
if (elementType is not null) {
    var rawObj = Heap.UnsafeGet(arrHandle);
    var idx = ...
    if (elementType.IsValueType) {
        // Direct long[] access — no cast needed (long→long)
        return (long[])rawObj)[idx];
    } else {
        // Direct object[] access — unbox
        return (long)((object[])rawObj)[idx];
    }
}
// Fallback: runtime type check (existing code)
```

### Step 3: Apply same optimization to `EmitAssignment` (array element writes)

The `EmitAssignment` method also has a runtime type check for array element writes. Apply the same static type optimization there.

## Impact

| Benchmark | Current ratio | Expected ratio | Savings |
|-----------|:-----------:|:-------------:|:-------:|
| **Sieve** | 1.07× | ~1.04× | Marginal (already memory-bound) |
| **NQueens** | 7.66× | ~4-5× | Significant (tight loop, many array ops) |
| **Mandelbrot** | 2.06× | ~1.8× | Moderate (some array/branch overhead) |
| **Collatz** | 1.05× | ~1.05× | No arrays, no change |

## Files to Modify

1. `Poly/Interpretation/Vm/DirectVmAbiEmitter.cs`:
   - `EmitIndexAccess`: add static type check before runtime fallback
   - `EmitAssignment` (IndexAccess branch): add static type check for array writes

## Risks

- Low: if analysis resolves the wrong type, we'd crash with `InvalidCastException` instead of gracefully falling back. Mitigation: the analysis pipeline is well-tested and deterministic.
- None: the runtime fallback path remains as a safety net when type is unknown.

## Not Doing

- Bypassing the heap entirely for stack-allocated arrays (requires Task 1 frame changes first)
- Multi-dimensional array support (not in scope)
