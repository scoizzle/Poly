# VM/Interpretation Complexity Reduction Plan

**Status:** Implemented  
**Date:** 2026-07-06  
**Audience:** Maintainers and AI agents working on `Poly/Interpretation/`  

## Motivation

The Interpretation module has grown by accretion — features added via "one more case arm" in existing switches, one more step in `CompileCore`, one more field on `CompilationContext`. The result is duplicated logic, dead code, and files that handle too many concerns. This plan identifies six concrete reductions, ordered by payoff.

## Principles

1. **No behavioral changes** — every refactoring must pass existing tests unchanged.
2. **Ship what you can** — each phase is independently deliverable; we don't need to finish all six to get value.
3. **Delete before abstract** — if code has no callers, remove it. Only extract when duplication is live.
4. **One simulation, not three** — ring allocation is computed three times; it should be computed once.

---

## Phase 1 — Delete `CallSiteCompiler`

**Files:** `Poly/Interpretation/Vm/CallSiteCompiler.cs`  
**Dependencies in:** None (zero callers — documented as K-020)  
**Lines removed:** ~130  
**Risk:** Minimal. The code is documented as dead. If anything breaks, the test suite will catch it.

### What
- Delete `CallSiteCompiler.cs` entirely.
- Remove any remaining references (search for `CallSiteCompiler`, `CallSiteDelegate`, `ReadSpanInt`, `CallSiteCompiler.Compile`).

### Why
- Near-identical `IsStackValue`/`GetPrimitiveType` dispatch pattern duplicated from `EmitCallExternalDirect`.
- Two bug surfaces for the same marshalling logic.
- Dead code is the worst kind of complexity — it looks like it matters but doesn't.

### Testing
- Green test run after deletion proves no callers.

---

## Phase 2 — Extract `VmValueMarshaller`

**Files created:** `Poly/Interpretation/Vm/VmValueMarshaller.cs`  
**Files changed:** `ProgramCompiler.cs`, `CallSiteCompiler.cs` (if not yet deleted)  
**Lines changed:** -80 (net reduction)

### What
Extract a shared helper for the repeated `GetPrimitiveType()?.IsStackValue()` → cast/deref chain:

```csharp
internal static class VmValueMarshaller {
    /// <summary>
    /// Resolve a raw long from the VM (stack or ring) to a typed value
    /// suitable for a CLR method parameter or return value.
    /// Handles: stack scalars (cast from long), booleans (!= 0), heap refs (dereference handle).
    /// </summary>
    public static Expression MarshalToClr(Expression rawValue, Type targetType, Expression heapRawSlots);

    /// <summary>
    /// Convert a CLR return value back to a VM long (stack scalar or heap handle).
    /// Inverse of <see cref="MarshalToClr"/>.
    /// </summary>
    public static Expression MarshalFromClr(Expression clrValue, Type sourceType, Expression heap);
}
```

Call sites consolidate from 6 to 2 (one per direction).

### Why
- Eliminates repeated 4-step pattern at every parameter/return boundary.
- Single place to fix if the marshalling protocol changes.
- Testable in isolation with LINQ Expression tree inspection.

### Method signatures (detailed)

```csharp
// Read raw long → CLR value for method call
// rawValue: the long from ring/slot
// targetType: the CLR parameter type (typeof(bool), typeof(string), etc.)
// heapRawSlots: Expression for state.Heap.RawSlots
public static Expression MarshalToClr(
    Expression rawValue,
    Type targetType,
    Expression heapRawSlots)
{
    var pt = targetType.GetPrimitiveType();
    if (pt is not null && pt.Value.IsStackValue())
        return targetType == typeof(bool)
            ? NotEqual(rawValue, Constant(0L))
            : Convert(rawValue, targetType);
    // Heap reference: dereference handle
    var handle = Convert(rawValue, typeof(int));
    return Convert(ArrayAccess(heapRawSlots, handle), targetType);
}

// Write CLR value → VM long (stack scalar or heap handle)
// clrValue: the CLR expression
// sourceType: the CLR return/field type
// heap: Expression for state.Heap
public static Expression MarshalFromClr(
    Expression clrValue,
    Type sourceType,
    Expression heap)
{
    var pt = sourceType.GetPrimitiveType();
    if (pt is not null && pt.Value.IsStackValue())
        return sourceType == typeof(bool)
            ? Condition(clrValue, Constant(1L), Constant(0L))
            : sourceType != typeof(long) ? Convert(clrValue, typeof(long)) : clrValue;
    // Heap allocate
    var allocate = Ref<Heap>.Method(h => h.Allocate(null));
    return Convert(Call(heap, allocate, Convert(clrValue, typeof(object))), typeof(long));
}
```

### Usage in `EmitCallExternalDirect`

Current (4 inline sites):
```
// param
if (pt is not null && pt.Value.IsStackValue()) { ... }
else if (!paramType.IsValueType) { ... }

// instance
if (instPt is not null && instPt.Value.IsStackValue()) { ... }
else if (...) { ... }

// return
if (retPt is not null && retPt.Value.IsStackValue()) { ... }
else { ... }
```

After:
```
// param
rawArgs[argIdx] = VmValueMarshaller.MarshalToClr(rawArgs[argIdx], paramType, heapRawSlots);

// instance
rawArgs[0] = VmValueMarshaller.MarshalToClr(rawArgs[0], instanceType, heapRawSlots);

// return
result = VmValueMarshaller.MarshalFromClr(call, method.ReturnType, heap);
```

### Testing
- New unit tests calling `MarshalToClr`/`MarshalFromClr` with each primitive type and a reference type.
- Existing `EmitCallExternalDirect` tests implicitly cover the new code path.

---

## Phase 3 — Extract `RingAllocator`

**Files created:** `Poly/Interpretation/Vm/RingAllocator.cs`  
**Files changed:** `ProgramCompiler.cs`, `CompilationContext.cs`, `VmProgram.cs`, `PcToRingDepth.cs`  
**Lines changed:** net ~0 (restructured, not reduced)  
**Fixes:** K-034 (partial — documented gap), K-035 (side-table wired)

### What

Extract the three private ring-simulation methods (`BuildTargetDepth`, `ComputePrimitiveRingDepths`, `ComputePrimitiveConsumedPcs`) into a dedicated type with a single entry point:

```csharp
public sealed record RingAllocation {
    /// <summary>Maps producer PC → ring slot index.</summary>
    public IReadOnlyDictionary<int, int> ProducerToRingIdx { get; init; }
    /// <summary>Maps µop PC → eval-stack depth at that point.</summary>
    public IReadOnlyDictionary<int, int> RingDepthAtPC { get; init; }
    /// <summary>For each µop, the PCs that produced its consumed values.</summary>
    public IReadOnlyList<int[]> ConsumedPcs { get; init; }
    /// <summary>Maximum ring depth (number of ring slots needed).</summary>
    public int MaxDepth { get; init; }
    /// <summary>PcToRingDepth side-table for debug/EH introspection.</summary>
    public PcToRingDepth ToSideTable() => new(RingDepthAtPC);

    /// <summary>
    /// Compute ring allocation for a linked primitive sequence.
    /// Single pass simulates the eval-stack ring with branch-aware depth restoration.
    /// </summary>
    public static RingAllocation Compute(IReadOnlyList<PrimitiveNode> primitives);
}
```

### `CompilationContext` changes

Replace the three separate setup calls with one:

```csharp
// Before:
var ringDepthMap = ComputePrimitiveRingDepths(primitives, out var ringDepthAtPC);
ctx.ConfigureRingAllocation(ringDepthMap, 32, 32);
ctx.SetRingDepthMap(ringDepthAtPC);

// After:
var allocation = RingAllocation.Compute(primitives);
ctx.Configure(allocation, registerLimit: 32);
```

`CompilationContext.ConfigureRingAllocation` simplifies to accept the whole `RingAllocation` object:

```csharp
public void Configure(RingAllocation allocation, int registerLimit) {
    _registerLimit = registerLimit;
    _maxFrameDepth = registerLimit; // same default
    _pcToRingIdx.Clear();
    _ringRegisters.Clear();
    foreach (var kv in allocation.ProducerToRingIdx)
        _pcToRingIdx[kv.Key] = kv.Value;
    int regCount = Math.Min(registerLimit, allocation.MaxDepth);
    for (int i = 0; i < regCount; i++) {
        var reg = Variable(typeof(long), $"_r{i}");
        _ringRegisters.Add(reg);
        _locals.Add(reg);
    }
    _ringDepthAtPC.Clear();
    foreach (var kv in allocation.RingDepthAtPC)
        _ringDepthAtPC[kv.Key] = kv.Value;
}
```

### `VmProgram` changes

Wire the side-table into the output (fixes K-035). Note: `MaxActiveLocalsDepth`
sizes the `state.Registers` scratch buffer (indexed by `SavedSp + ringIdx` at
call boundaries), which is distinct from the ring depth. A minimum of 32 is
preserved as a safe default for the register buffer, while the actual ring
depth from allocation is used for the `_r{k}` LINQ local count.

```csharp
// Before:
return new VmProgram(del, 32);

// After:
var ringAllocation = RingAllocation.Compute(primitives);
int registerScratchSize = Math.Max(ringAllocation.MaxDepth, 32);
return new VmProgram(del, registerScratchSize, PcDepthMap: ringAllocation.ToSideTable());
```

This fixes K-035 (side-table never populated). INT-006 (hardcoded 32) was not
a real bug — `MaxActiveLocalsDepth` serves a different purpose from the ring
allocation depth.

### `ProgramCompiler.CompilePrimitives` call site

```csharp
// Before:
var ringDepthMap = ComputePrimitiveRingDepths(primitives, out var ringDepthAtPC);
ctx.ConfigureRingAllocation(ringDepthMap, 32, 32);
ctx.SetRingDepthMap(ringDepthAtPC);
var consumedPcs = ComputePrimitiveConsumedPcs(primitives);
// ...
return new VmProgram(del, 32);

// After:
var ringAllocation = RingAllocation.Compute(primitives);
ctx.Configure(ringAllocation, registerLimit: 32);
var consumedPcs = ringAllocation.ConsumedPcs;
// ...
return new VmProgram(del, ringAllocation.MaxDepth, PcDepthMap: ringAllocation.ToSideTable());
```

### Testing
- New `RingAllocationTests` with known primitive sequences, asserting exact slot assignments and max depth.
- Existing `Stress_DeepRingDepth` (ring depth ~50, spill path) and `Fuzz_Phi_NestedConditional_DifferentRingDepths` (branch convergence) continue to pass.
- Regression test for K-035: verify `VmProgram.PcDepthMap` is non-null after compilation.

---

## Phase 4 — Share Binary Op `ToPrimitives`

**Files changed:** All binary expression node files in `Poly/Syntax/Nodes/` (15 files)  
**Lines changed:** -90 (net reduction from ~180 to ~90)

### What
Add a shared helper on `BinaryExpression` (or static in `Expression` base) to eliminate the boilerplate:

```csharp
// In Expression.cs or a shared location:
protected static IEnumerable<PrimitiveNode> EmitBinaryOp(
    Node left, Node right, OpKind op, ExpansionContext ctx)
{
    foreach (var p in left.ToPrimitives(ctx)) yield return p;
    foreach (var p in right.ToPrimitives(ctx)) yield return p;
    yield return new PrimitiveNode.BinaryOp(op);
}
```

Each binary node file reduces from 5 lines to 1:

```csharp
// Before (5 lines):
public override IEnumerable<PrimitiveNode> ToPrimitives(ExpansionContext context) {
    foreach (var p in LeftHandValue.ToPrimitives(context)) yield return p;
    foreach (var p in RightHandValue.ToPrimitives(context)) yield return p;
    yield return new BinaryOp(OpKind.And);
}

// After (1 line):
public override IEnumerable<PrimitiveNode> ToPrimitives(ExpansionContext context) =>
    EmitBinaryOp(LeftHandValue, RightHandValue, OpKind.And, context);
```

### Why
- 15 files with identical structure — any change to the emit pattern touches 15 places.
- New binary-like expressions won't forget the pattern.

### Affected nodes

| Node | OpKind |
|------|--------|
| `Add` | `Add` |
| `Subtract` | `Sub` |
| `Multiply` | `Mul` |
| `Divide` | `Div` |
| `Modulo` | `Mod` |
| `Equal` | `Eq` |
| `NotEqual` | `Neq` |
| `LessThan` | `Lt` |
| `LessThanOrEqual` | `Lte` |
| `GreaterThan` | `Gt` |
| `GreaterThanOrEqual` | `Gte` |
| `And` | `And` |
| `Or` | `Or` |
| `BitwiseAnd` | `And` |
| `BitwiseOr` | `Or` |
| `BitwiseXor` | `Xor` |
| `ShiftLeft` | `Shl` |
| `ShiftRight` | `Shr` |

Some nodes (`And`, `Or`) are in `Syntax/Nodes/`. Bitwise nodes may be elsewhere — search for each.

### Testing
- Existing primitive expansion tests exercise every OpKind.
- Assert identical output before/after for each affected node type.

---

## Phase 5 — Fix Code Generator Gaps

**Files changed:** `CSharpGenerator.cs`, `LinqExpressionGenerator.cs`  
**Lines changed:** +~80 (additions to close gaps)

### What
Handle `BitwiseAnd`, `BitwiseOr`, `BitwiseXor`, `BitwiseNot`, `ShiftLeft`, `ShiftRight`, `PopCount`, `StridedSet`, `NewArray`, `SuspendNode` in:

- **`CSharpGenerator.WriteExpression`** — add switch arms that produce correct C# output instead of falling to `node.ToString()`. Bitwise: `&`, `|`, `^`, `~`, `<<`, `>>`. PopCount: `BitOperations.PopCount(...)` or intrinsic. StridedSet: skip or comment. NewArray: `new long[size]`. SuspendNode: empty.
- **`LinqExpressionGenerator.CompileNode`** — add switch arms instead of throwing. Bitwise: `Expression.And`, `Or`, `ExclusiveOr`, `Not`, `LeftShift`, `RightShift`.

### Why
- `CSharpGenerator` currently produces silently wrong C# for these (calls `ToString()` which gives Poly-internal representation).
- `LinqExpressionGenerator` currently crashes.
- The VM path handles them all correctly; the other generators should match.

### Priority
Lower than 1–4 because the VM path is the canonical one (per AGENTS.md) and these generators are secondary. But leaving them broken creates a trap for anyone who uses the non-VM paths.

---

## Phase 6 — Consolidate `GetPrimitiveType` Mapping

**Files changed:** `ClrTypeDefinition.cs`, `PrimitiveType.cs`  
**Lines changed:** -0 (restructure, not reduce)

### What
`ClrTypeDefinition.GetPrimitiveTypeId(Type)` is a second mapping from CLR types to primitive IDs, slightly different from `PrimitiveType.GetPrimitiveType(this Type)`. Collapse into one.

### Why
- Two tables mapping the same thing (CLR type → primitive classification).
- If they diverge, behavior depends on which one you call.
- Low risk — the types are well-defined (integers, float, double, bool, char, etc.).

### Approach
- Move the logic from `ClrTypeDefinition.GetPrimitiveTypeId` into `PrimitiveType.GetPrimitiveType` or make the former delegate to the latter.
- Verify both call sites produce identical results for all 20 mapped CLR types.

---

## Sequencing & Dependencies

```
Phase 1: Delete CallSiteCompiler     ← no dependencies, do first
    │
    ▼
Phase 2: Extract VmValueMarshaller   ← ProgramCompiler is only live consumer
    │
    ▼
Phase 3: Extract RingAllocator       ← biggest structural change
    │
    ▼
Phase 4: Share binary ToPrimitives   ← mechanical, independent
    │
    ▼
Phase 5: Fix generator gaps          ← independent of 1-4
    │
    ▼
Phase 6: Consolidate type mapping    ← independent, lowest priority
```

Phases 4, 5, and 6 are independent of 1–3 and each other. They can be parallelized.

---

## Test Strategy

| Phase | Validation |
|-------|-----------|
| 1 | Green `dotnet run --project Poly.Tests` after file deletion |
| 2 | Green tests + new `VmValueMarshallerTests` |
| 3 | Green tests + new `RingAllocationTests` (unit tests for slot assignment, max depth, branch convergence) |
| 4 | Green tests — compare ToPrimitives output before/after for each binary op |
| 5 | Green tests — new tests for previously-throwing paths in CSharpGenerator and LinqExpressionGenerator |
| 6 | Green tests — assertion that both mapping paths produce identical results for all CLR types |

`dotnet run --project Poly.Tests` runs all tests (TUnit framework).

---

## Future Work (Out of Scope)

- **Enforced pass ordering** — the 13-pass pipeline is convention-only. Would require dependency metadata on `INodeAnalyzer` and a topological sort in `AnalyzerBuilder`. Worth a separate plan.
- **ProgramCompiler file split** — the 1000-line file handles emission, call protocol, EH dispatch, and loop limiting. A future split could separate these, but the ring extraction (Phase 3) removes the biggest non-emission concern.
- **LinqExpressionGenerator numeric promotion** — the `GetPromotedNumericType` logic is only in LinqExpressionGenerator and isn't duplicate. If a C#-targeting lowering path emerges, it may need to be shared, but for now it's correctly scoped.
