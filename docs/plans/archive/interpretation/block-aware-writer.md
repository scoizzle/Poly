> **ARCHIVED (2026-07-10)** — Do not implement. Superseded by direct AST→VM-ABI (`DirectVmAbiEmitter`). See `docs/plans/archive/interpretation/README.md`.
>
> Original document follows for historical context only.

# Plan: Block-Aware Program Writer

## Problem

Every µop compiles to `slots[sp++] = val` / `slots[--sp]` — a `long[]` with bounds checking and L1 traffic. This is the proven bottleneck. Values produced and consumed within the same basic block travel through the array for no benefit.

## Core Insight

The lowering already knows the control flow structure — it's walking the AST with `IfStatement`, `WhileLoop`, etc. The µops it emits preserve that structure via `JumpOp`/`JumpIfFalseOp`. A **block-aware compiler** can rediscover those blocks from the µop list and compile them with CLR locals for intra-block values, only spilling to `slots[]` at block boundaries.

No changes to lowering. No new µop types. No SSA reconstruction. Just a smarter `ProgramCompiler.Compile`.

## Architecture

```
Lowering → µop[] (unchanged)
               ↓
    BlockAwareCompiler (replaces ProgramCompiler.Compile)
               ↓
               ├── Walk µops, discover basic blocks
               ├── Per block: compile µops using CLR locals
               │   └── _valueStack tracks CLR Expression values
               │   └── _localCache: local_index → CLR Expression
               │   └── _dirtyLocals: modified this block
               ├── At block boundaries: spill dirty locals to slots[]
               └── At CallOp/ReturnOp: fall through to dispatch loop
```

## Per-Block µop Compilation

The compiler walks the µop list and identifies blocks by terminator µops (`JumpOp`, `JumpIfFalseOp`, `ReturnOp`, `ReturnFromCallOp`, `CallOp`, `ThrowOp`). Within each block, instead of emitting `slots[sp++]` / `slots[--sp]` for every operation, the compiler maintains:

### `_valueStack: Stack<Expression>`

Mirrors the VM stack but holds CLR `Expression` objects. When a µop would push, the compiler pushes a new CLR variable assignment. When a µop would pop, the compiler pops from this stack.

### `_localCache: Dictionary<int, Expression>`

Each local variable's current value as a CLR expression. Hydrated lazily (first `LoadLocal` in a block reads from `slots[]` and caches). Updated eagerly (`StoreLocal` creates a new CLR local and caches it without writing to `slots[]`).

### `_dirtyLocals: HashSet<int>`

Locals modified in the current block. At each block boundary, these are flushed to `slots[]`.

### Example trace: `x = a + b`

```
// Lowering emits:
loadlocal a
loadlocal b
add
storelocal x

// Block-aware compiler:
_ = _localCache.TryGetValue(a, out expr_a)  // miss → t_a = slots[off_a]; cache
_ = _localCache.TryGetValue(b, out expr_b)  // miss → t_b = slots[off_b]; cache
t_sum = Expression.Add(expr_a, expr_b)
_valueStack.Push(t_sum)
expr_x = _valueStack.Pop()
t_x_new = expr_x
_localCache[x] = t_x_new
_dirtyLocals.Add(x)
```

**Result:** zero `slots[sp]` accesses — all values flow through CLR temps. The `slots[]` is touched exactly once per local at first use in the block (lazy load).

### Block boundary (e.g., if/else merge or loop back-edge)

```
EndBlock():
  foreach idx in _dirtyLocals:
      body.Add(Expression.Assign(slots[off(idx)], _localCache[idx]))
  _dirtyLocals.Clear()
  _localCache.Clear()
  _valueStack.Clear()

BeginBlock():
  _localCache.Clear()   // forces lazy re-load on next use
  _valueStack.Clear()
```

## µop Compilation Table

Each µop type has a known stack-effect (pop count, push count). The compiler emits one case per µop type:

| µop | Compilation |
|-----|-------------|
| `PushOp(v)` | `t = v; push t` |
| `PopOp` | `pop` (discard top of value stack) |
| `DupOp` | `t = top; push t` |
| `LoadLocalOp(i)` | cache hit → push cached; miss → `t = slots[off]; cache; push t` |
| `StoreLocalOp(i)` | `v = pop; t = v; cache[i] = t; dirty.add(i)` |
| `LoadArgOp(i)` | `t = slots[FB+i]; push t` (never cached — args can't be stored) |
| `StoreArgOp(i)` | `v = pop; emit slots[FB+i] = v` (always spills — arg mutations must be visible) |
| `IncLocalOp(i, inc)` | miss → `t = slots[off]; cache`; `t2 = t + inc; cache[i] = t2; dirty.add(i); push t2` |
| `AddOp(imm)` | imm=null: `r=pop; l=pop; t=l+r; push t`. imm!=null: `l=pop; t=l+imm; push t` |
| `SubOp` / `MulOp` / `DivOp` etc. | same pattern |
| `EqOp` / `NeOp` / `LtOp` etc. | same pattern, result is 1L or 0L |
| `NegOp` | `v=pop; t=-v; push t` |
| `NotOp` | `v=pop; t=v==0?1:0; push t` |
| `BitNotOp` | `v=pop; t=~v; push t` |
| `BitAndOp` / `BitOrOp` / `BitXorOp` / `ShlOp` / `ShrOp` | binary pattern with immediate variant |
| `DivRemOp` | `r=pop; q=pop; push(q%r); push(q/r)` |
| `JumpOp(t)` | `goto label_t` (block terminator) |
| `JumpIfFalseOp(t)` | `v=pop; if(v==0) goto label_t` (block terminator) |
| `ReturnOp` | `v=pop; if(fb<0) goto exit` (block terminator) |
| `ReturnFromCallOp(a)` | `v=pop; unpack metadata; restore fb/pc; write result` (block terminator) |
| `CallOp(f,a)` | `spill all dirty; writeback; handleCall; resync` (block boundary) |
| `CallClosureOp` | same as CallOp |
| `CallExternalOp(s)` | same as CallOp |
| `LoadValueOp` | `h=pop; if(h>=0) t=heap[h]; else t=slots[-h]; push t` |
| `StoreValueOp` | `v=pop; h=pop; if(h>=0) heap[h]=v; else slots[-h]=v` |
| `NewArrayOp(a)` | `s=pop; arr=new long[s]; if(alias) aliasVar=arr; push dummy` |
| `NewArrayImmOp(s,a)` | `arr=new long[s]; if(alias) aliasVar=arr; push dummy` |
| `ArrayLoadOp(a)` | `i=pop; if(alias) t=alias[i]; else h=pop; t=heap[h][i]; push t` |
| `ArrayStoreOp(a)` | `v=pop; i=pop; if(alias) alias[i]=v; else h=pop; heap[h][i]=v` |
| `AllocClosureOp(f,c)` | `spill; writebackSP; HandleAllocClosure; resyncSP` |
| `LoadUpvalueOp(i)` | `t=HandleLoadUpvalue(state,i); push t` |
| `StoreUpvalueOp(i)` | `v=pop; HandleStoreUpvalue(state,i,v)` |
| `ThrowOp` | `v=pop; spill; HandleThrow(state,v)` |
| `EndFinallyOp` | `spill; HandleEndFinally(state)` |
| `BatchReduceOp(r,a)` | compound — compiled as loop in expression tree (unchanged) |
| `CountBitsOp(a)` | compound (unchanged) |
| `StridedSetOp(a)` | compound (unchanged) |
| `CmpLocalLeOp(i,c)` | miss → load; `t = slots[off] <= c ? 1 : 0; push t` |
| `CmpLocalJmpOp(i,c,t)` | miss → load; `if(slots[off] <= c) goto t; else pc++` (block terminator) |
| `CommentOp` | no-op (no expression generated) |

## CallOp / ReturnFromCallOp Barrier

`CallOp` changes `FrameBase` and `CachedArgSlots`. After `HandleCall`, `state.PC` points to the callee. The dispatch loop runs the callee's µops. `ReturnFromCallOp` restores the caller's `FB`/`CAS` and `PC`.

The compiler cannot inline the callee's body — the dispatch loop must handle call/return. So for `CallOp`-like µops:

1. Spill all dirty locals to `slots[]` (caller's frame)
2. Write back the local `sp` and `pc` to `state`
3. Emit `HandleCall(state, funcIndex, argSlots)` which sets `state.PC = calleeEntry`
4. Emit `state.Stack.SetSP(sp)` and `state.PC = pc` as part of the dispatch exit
5. The dispatch loop runs the callee's µops
6. On return (`ReturnFromCallOp`), restore `state.PC` to the instruction after the call

After the call returns, the caller's µops continue. The local cache has been cleared (because the callee may have modified slots). Subsequent loads are lazy from `slots[]`.

This is the only case where the dispatch loop is needed. For intra-block µop sequences with no calls, the dispatch loop is bypassed entirely and µops compile to a linear expression sequence.

Wait — the current architecture doesn't have a separate dispatch loop per call. The compiled delegate is ONE loop: `while (pc < count) { switch(pc) { ... } }`. The `HandleCall` sets PC to the callee's entry, and the NEXT iteration of the loop dispatches the callee's µops. This works because all µops (for all functions) are in the same µop array.

So the compiled delegate CAN'T have a linear sequence for the caller and a separate dispatch for the callee — they're all in one delegate. The dispatch loop stays.

But the per-µop expression tree doesn't have to go through `slots[sp]`. Each µop's expression can use CLR locals instead. The dispatch loop still dispatches, but the µop body is different.

Here's the key insight: **the dispatch loop stays, but `sp` is no longer used for intra-block value flow.** The `sp` variable is still maintained (for call/return frame setup), but most µops don't read or write it. Instead, µops use the writer's `_valueStack` and `_localCache`.

But wait — each µop's `ToExpression` is called independently during `ProgramCompiler.Compile`. The writer's `_valueStack` and `_localCache` are per-compilation-session state, not per-µop. We need cross-µop state during compilation.

This means the compilation MUST be a single walk over the µop list, not a per-µop `ToExpression` call. The `ProgramCompiler.Compile` method is replaced entirely.

Let me check what this means concretely. Currently:

```csharp
for (int i = 0; i < uops.Count; i++) {
    var uop = uops[i];
    var execBody = new List<Expression> { uop.ToExpression(ctx) };
    // ... wrap with breakpoint check, trace, pc++
    switchCases[i] = Expression.SwitchCase(..., Expression.Constant(i));
}
```

Each µop calls its own `ToExpression`. The writer replaces this with:

```csharp
for (int i = 0; i < uops.Count; i++) {
    var uop = uops[i];
    // Compile µop using writer's state (no ToExpression call)
    var expr = CompileUop(uop, writer);
    // ... wrap with breakpoint check
    switchCases[i] = Expression.SwitchCase(..., Expression.Constant(i));
}
```

Where `CompileUop` is a switch on µop type:

```csharp
Expression CompileUop(MicroOp op, BlockAwareWriter writer) => op switch {
    LoadLocalOp ll => CompileLoadLocal(ll, writer),
    AddOp a => CompileAdd(a, writer),
    ...
};
```

This gives the writer access to its `_valueStack`, `_localCache`, and `_dirtyLocals` across consecutive µops within the same block.

**This is the big change:** `ProgramCompiler.Compile` is rewritten to use a per-µop compilation switch that accesses writer state. The µop `ToExpression` methods are no longer called during normal compilation (they remain for the µop-level debug dump and as a reference implementation).

The work involved:
1. Write the `CompileUop` switch (~60 cases, each 3-15 lines = ~500 lines)
2. Write the `BlockAwareWriter` class (~200 lines)
3. Replace `ProgramCompiler.Compile` body (the loop + switch case building stays, but the per-µop expression changes)

Total new code: ~700 lines. The µop `ToExpression` methods remain as reference and for fallback.

### What becomes simpler

- The `CompilationContext` class is no longer needed for the primary compilation path (its Push/Pop compile to `slots[sp]`). The writer has its own value stack.
- `ComputeMaxDepth` is no longer needed — the `sp` variable is only used for call/return frame setup, not µop value flow. `stack.Reserve(maxDepth)` might still be needed for call frames.
- The µop heuristic pass and LoopCsePass become optional — they reduce µop count but the writer already eliminates `slots[sp]` traffic regardless of µop count.

### What stays the same

- Lowering (no changes)
- µop types and their `ToExpression` methods (unused by writer, kept for compatibility)
- `VmState`, `Vm.Execute`, `ValueStack`, `Heap`, etc.
- The `Bytecode` structure
- All analysis passes
- All tests

## Block Discovery

Identical to the SSA builder's approach: scan µop list for `JumpOp`/`JumpIfFalseOp`/`ReturnOp`/`ReturnFromCallOp`/`ThrowOp` targets to find block boundaries.

Each block has:
- Start PC (first µop in the block)
- End PC (last µop before a terminator)
- Terminator µop (Jump, JumpIfFalse, Return, etc.)

Within a block, the writer walks µops sequentially. The `_valueStack` tracks CLR expressions. The `_localCache` tracks cached locals.

## Breakpoints

Each µop gets a breakpoint check as before. The break check's taken branch includes spill code for the current µop's dirty locals:

```csharp
// For µop at PC i:
var breakCheck = Expression.IfThenElse(
    Expression.AndAlso(DebugMode, bpPCs.Contains(pc)),
    Expression.Block(
        // Spill dirty locals at this program point
        slots[off(x)] = _localCache[x],        // for each dirty x
        slots[off(y)] = _localCache[y],
        SavedPC = pc,
        Status = Suspended,
        pc = codeLen
    ),
    Expression.Block(typeof(void), execBody)
);
```

The spill set varies per µop — only locals that have been written since the last `BeginBlock` AND are still live. The writer tracks this in `_dirtyLocals`.

## Replacing the Heuristic and LoopCse Passes

The block-aware writer makes these passes largely redundant:

- **`UopHeuristicPass`** fuses `loadlocal v; loadlocal v; binop` → `loadlocal v; dup; binop`. The writer's `_localCache` already avoids the second load — it hits the cache. No fusion needed.

- **`LoopCsePass`** hoists pure µop subsequences. The writer's CLR locals already avoid redundant loads within a block. Cross-block hoisting (loop-invariant load hoisting) could be done by a pre-compilation analysis pass or deferred to when a concrete need appears.

Both passes can remain inactive (the pipeline still exists but neither pass is registered). The µop optimizer pipeline becomes:

```
current = uops;  // no heuristic pass, no CSE pass
```

## Validation

1. **Round-trip**: compile a µop array with the writer, execute, compare result with the current compiler. Start with simple arithmetic, then control flow, then calls.

2. **All 1315 tests**: run the full test suite with the writer as the compilation backend. Fix failures until all pass.

3. **Benchmarks**: compare `slots[sp]` access counts via the µop trace infrastructure. Expected: 80-90% reduction in intra-block slot traffic.

4. **Switch dispatch overhead**: measure whether the dispatch loop itself (the `switch(pc)` statement) adds measurable overhead. If so, investigate compiling fully linear blocks with fall-through labels (the previous linear compilation experiment showed no benefit, so likely not an issue).

## Key Risks

1. **Writer complexity**: ~700 lines of new compilation code. Mechanical but must be correct for all 60 µop types. Addressed by writing incrementally — start with arithmetic and control flow, then add calls, heap, arrays, etc.

2. **CallOp barrier**: the dispatch loop must remain for call/return. The writer's local cache is cleared at each call boundary, forcing lazy re-load. This is correct but may be a performance concern for programs with many small function calls (NQueens). Addressed by benchmarking first; if calls are an issue, investigate per-function writer instances.

3. **Deferred slot writes**: at block boundaries, dirty locals are spilled. If a program has many very small blocks (e.g., short-circuit && with many operands), the spill/reload overhead could be high relative to the computation saved. Addressed by measuring and optimizing: for very short blocks, the writer could skip the cache entirely and use direct slots[] access.

4. **Code size increase**: CLR locals produce more expression tree nodes than `slots[sp]` accesses. The JIT may take longer to compile the delegate. Addressed by measuring first-run compilation time.

## Migration Path

1. Implement `BlockAwareCompiler` alongside the existing `ProgramCompiler.Compile`
2. Use it for all flat µop sequences first (no function calls)
3. Gradually add support for calls, then all µop types
4. Once fully validated, `BlockAwareCompiler` becomes `ProgramCompiler.Compile`
5. The old compiler remains as `ProgramCompiler.CompileLegacy` for A/B comparison
6. The µop `ToExpression` methods remain for debugging and reference
