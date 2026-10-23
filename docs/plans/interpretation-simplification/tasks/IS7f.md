# TASK IS7f - `typeof(x)` allocates a heap handle on every evaluation

Status: planned. Branch `slice/IS7f`. Lane C (`vm`). Depends on IS7e (merged).
Plan: [IS-README](../IS-README.md).

## Why

`EmitTypeOf` (`AbiEmitter.Expressions.cs:487-495`) emits `heap.Allocate(Constant(runtime))` **inline**, so every evaluation of a `typeof` expression allocates a fresh heap handle for the *same* `Type` object:

```csharp
case TypeOf t:
    var runtime = ResolveClrType(...);
    return Assign(RingVar(slot),
        Convert(Call(HeapLocal, HeapAllocate, Constant(runtime, object)), long));
```

The `Type` is known at compile time. In a loop, or in a function called per item, this is one heap entry per evaluation that is never reclaimed - `Allocate` only reuses handles that an explicit `Recycle` returned, and nothing recycles a type object.

Found while auditing allocation sites for [IS7e](IS7e.md); it is a bug, not a design gap.

## Scope

Make the handle a **compile-time constant**: allocate the type object once per program and reuse the handle.

1. `Context` keeps a small table `Type -> handle` (or reuses whatever constant-hoisting mechanism exists for strings, if one does), populated during emission so that two `typeof` nodes for the same type share one handle.
2. The handle must be allocated **before** the body runs - either as part of the heap's initial content at program entry, or via a leading statement in the compiled unit - so `typeof` compiles to `Constant(handle)` with no call at all.
3. If the existing machinery makes a pre-body allocation awkward, the fallback is to allocate once at first evaluation and cache it in a dedicated local, guarded by a null check. That keeps one handle per type, but costs a branch; prefer the constant form and say in the Log which shape landed.

## Files

- `Poly/Interpretation/Vm/Abi/AbiEmitter.Expressions.cs` - `EmitTypeOf`.
- `Poly/Interpretation/Vm/Abi/AbiEmitter.Context.cs` - the type-handle table.
- `Poly/Interpretation/Vm/Abi/AbiEmitter.cs` - program entry, if the handle must be seeded there.
- `Poly.Tests/Interpretation/TypeOfAllocationTests.cs` (new).
- `docs/interpretation/vm-abi-reference.md` - one line under the allocation list.

## Shape matrix

| Input | Before | After | Test that proves it |
|---|---|---|---|
| `typeof(long)` evaluated once | 1 allocation | **0** at evaluation; 1 for the program | `TypeOf_AllocatesOncePerProgram` (structural: one `Allocate` in the tree, and none inside the `typeof` expression) |
| `typeof(long)` in a 1000-iteration loop | 1000 allocations | 1000 none; `heap.Count` constant | `TypeOfInLoop_HeapDoesNotGrow` |
| two `typeof(long)` sites | 2 allocations | **1** shared handle | `TwoTypeOfSites_ShareOneHandle` |
| `typeof(A)` and `typeof(B)` | 2 | 2, distinct | `DistinctTypes_GetDistinctHandles` |
| the value observed at runtime | a `Type` object | the same `Type` object | `TypeOf_StillYieldsItsType` (existing behaviour preserved) |

## Done when

- Every row passes; the loop row asserts `heap.Count` before and after 1000 iterations is equal.
- The `typeof` value still round-trips to the expected `RuntimeType` at runtime - changing representation must not change meaning.
- `AstParityTests` 145/145; full suite green; build 0 warnings.
- Benchmarks (`--job medium`): all four hold. No program in `ProgramBenchmarks` uses `typeof`, so a flat result is expected and should be stated rather than presented as a win.

## SHIP if / NOT SHIP if

SHIP: no allocation at a `typeof` evaluation, one handle per distinct type, the value unchanged.
NOT SHIP: a per-evaluation allocation remains behind a cache that can miss; two sites still allocate twice; the emitted value's identity changes in an observable way.

## Tests

```
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/TypeOfAllocationTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/AstParityTests/*'
```
Then the full suite.

## Hand-edit

One table from type to handle, one arm that reads it instead of allocating.

## Needs Scot

none.

## Log

| Date | Who | Mill | SHA | Verdict / event | Findings |
|------|-----|------|-----|-----------------|----------|
