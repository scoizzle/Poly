# ADR: Heap Reclamation Strategy

**Date:** 2026-06-08  
**Status:** Accepted  

## Context

The VM heap is an append-only `List<object?>` with stable index handles. There is no garbage collection. Slots are never reclaimed. Long-running synthesis loops, evolution feedback cycles, or any iterative execution will grow the heap without bound.

A full tracing GC is premature: the VM is not yet running programs with complex object graphs that require reachability analysis. However, the infrastructure to reuse dead slots is trivial and pays for itself immediately.

## Decision

Use a **free-list** approach with explicit slot null-out. No background scanning. No tracing GC.

### Contract

1. **`Heap` gains a `Queue<int> FreeSlots`** (or `Stack<int>` for LIFO reuse).

2. **`Set(handle, null)`** adds `handle` to the free list. Existing code already calls `Set` via `StoreValue` — this is the natural null-out path when a reference is known dead.

3. **`Allocate(value)`** checks the free list first. If `FreeSlots` is non-empty, it deques a handle, writes `value`, and returns it. Otherwise it appends (current behavior).

4. **`Return` opcode** optionally nulls out all local slots for the returning frame. This is a heuristic: if the locals held heap references that are now dead, `Set(localHandle, null)` adds them to the free list. This requires the frame header to track which locals are heap handles vs raw ints — or we conservatively null all local slots and accept a small cost.

### Implementation sketch

```csharp
internal sealed class Heap {
    private readonly List<object?> _objects = [];
    private readonly Stack<int> _freeSlots = [];
    public Action<int, object?>? OnAllocate { get; set; }
    public int Count => _objects.Count;

    public int Allocate(object? value) {
        if (_freeSlots.TryPop(out int handle)) {
            _objects[handle] = value;
            OnAllocate?.Invoke(handle, value);
            return handle;
        }
        handle = _objects.Count;
        _objects.Add(value);
        OnAllocate?.Invoke(handle, value);
        return handle;
    }

    public void Set(int handle, object? value) {
        _objects[handle] = value;
        if (value is null)
            _freeSlots.Push(handle);
    }
}
```

### Out of scope

- Tracing GC (requires root scanning, write barriers, compaction).
- Background `ValueTask` scan (not justified until heap exceeds thousands of slots).
- Finalization / `IDisposable` tracking.

## Rationale

- Minimal complexity: 10 lines of additional code.
- Catches the most common leak pattern: temporary heap objects whose handle is held by a local or temp slot that is never reused.
- Free-list allocation keeps the heap size bounded in practice for most IR programs.
- Conservative null-out of locals at `Return` is cheap: 1 write per local per call.

## Consequences

- `Heap.Set(handle, null)` now has the side effect of reclaiming the slot.
- All existing `Heap.Set` call sites are already correct (they write a value, which may be null).
- The free list may fragment if large objects are allocated and freed while small objects fill the gaps — this is acceptable until fragmentation becomes a measured problem.
- A future tracing GC can reuse the same `Allocate` contract (try free list, then append).