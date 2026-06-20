# Research: Eliminating Unnecessary Push/Pop for Short-Lived µop Values

## The Problem

The linear compilation experiment confirmed that dispatch overhead is negligible. **Stack traffic through `slots[SP]` is the bottleneck** (LINEAR-UOP-COMPILATION.md:44). Every µop that produces a value emits `slots[sp++] = val` and every consumer emits `slots[--sp]`. Each access goes through array bounds checking and L1 cache traffic.

The question: **which push/pop pairs are unnecessary, and how do we eliminate them?**

## Where Unnecessary Stack Traffic Comes From

### Pattern 1: Load-Use-Store sequences

Lowering emits `DupOp; StoreLocalOp` whenever an assignment's value is itself used as an expression value. The `DupOp` is the only way to preserve the value through the store. This is correct but the value goes through the stack twice:

```
; i++ used in an expression:
loadlocal i       ; slots[sp++] = i
dup               ; slots[sp++] = slots[sp-1]     ← value on stack twice
push 1
add               ; slots[--sp] as right, slots[sp-1] += right
storelocal i      ; slots[off] = slots[--sp]      ← pop for the store
                  ; result stays on stack (from dup)
```

After heuristic fusion (`DataFlowSameLocalBinary`):
```
loadlocal i       ; slots[sp++] = i
dup               ; slots[sp++] = slots[sp-1]
addimm 1          ; slots[sp-1] += 1
storelocal i      ; slots[off] = slots[--sp]
```

Every intermediate value travels through `slots[]`. The `dup` reads from `slots[sp-1]` and writes to `slots[sp]` — a round-trip through L1 cache for a value that's just a temporary copy.

### Pattern 2: Chained binary ops

```
loadlocal a       ; push a
loadlocal b       ; push b
add               ; pop b → slots[sp-1] = a + b
loadlocal c       ; push c
add               ; pop c → slots[sp-1] = (a+b) + c
```

Four stack accesses for a+b, then two more for c, then two for the outer add. The value `a+b` exists only to be consumed by the outer add — it's live for exactly one µop.

### Pattern 3: Method argument setup

```
loadlocal x       ; push arg0
loadlocal y       ; push arg1
call fn, 2        ; consume both on frame setup
```

The argument values are pushed to the stack only to be read back by `CallOp`'s frame layout logic. These values could go directly into the callee's argument slots.

### Pattern 4: IncLocalOp

`IncLocalOp` is a fused µop that does `slots[off] += inc; push result`. It reads and writes the local slot, but also pushes the result to the stack — which is immediately consumed by the enclosing expression or `PopOp` if the increment statement's value is unused.

## Proposed Approach: µop-Level Stack-to-Register Promotion

Replace VM stack slot access (`slots[sp++]` / `slots[--sp]`) with CLR local variables for short-lived values. A CLR local (`ParameterExpression` in expression trees) is allocated by the JIT to a register or native stack offset — no bounds check, no `long[]` indirection.

### Design Sketch

A new `IUopPass` called `StackToLocalPass` that runs before `ProgramCompiler.Compile`:

**Phase 1: Liveness Analysis (within each basic block)**

Partition the µop array into basic blocks (contiguous sequences with no control flow — bounded by `JumpOp`, `JumpIfFalseOp`, `CallOp`, `ReturnOp`). For each block:

1. Walk the µops forward, maintaining a virtual stack of "defs" — each entry records which µop produced the value currently at that stack position.
2. For each µop that pops values, record which defs it consumes.
3. For each µop that pushes values, record which position it produces.

Result: a per-µop use-def chain showing which µop's result feeds which consumer µop(s), **tracked through stack positions**.

**Phase 2: Allocation**

For each value that is:
- Defined by a single µop (always true in the current lowering)
- Consumed by exactly one µop (single-use) OR consumed by a µop within the same basic block
- Not accessed through `DupOp` (which creates a second reference point)

Allocate a CLR local `ParameterExpression` and replace the push/pop with direct local access:

```
Before:                     After:
  µop A: slots[sp++] = val    µop A: var_t = val
  ...                         ...
  µop B: op(slots[--sp])      µop B: op(var_t)
```

Dropping the `sp++` and `--sp` updates means the enclosing µop sequence no longer sees depth changes from these values. The pass must adjust the compiled expression to skip push/pop for allocated values.

**Phase 3: Expression Generation**

The pass doesn't rewrite µop records. Instead it produces a parallel data structure (`LocalAllocation[]`) consumed by `ProgramCompiler.Compile`. For each µop position where a CLR local replaces stack access, the compiler emits the local instead of `slots[sp++]` / `slots[--sp]`.

The µop-level `ToExpression` methods would need an optional override path: instead of the standard `ctx.Push(val)` → `ctx.SlotAt(ctx.SP++) = val`, produce `ctx.AllocatedVars[pos] = val` (if allocated).

### When Stack Traffic CANNOT Be Eliminated

- **Values that cross basic block boundaries** — must go through `slots[]` because the stack is the only persistent state between blocks.
- **Values consumed by `CallOp`** — the callee's frame reads arguments from `slots[FB + i]`. The argument values must be in the slot array.
- **Values consumed by `StoreLocalOp`** — the local slot is in the slot array. The value must be written there. But we can skip the `DupOp` in cases where the stored value isn't reused.
- **Values consumed by `DupOp`** — `DupOp`'s semantics are `slots[sp] = slots[sp-1]; sp++`. If the duplicated value is allocated to a CLR local, the dup can become a simple copy `var_t2 = var_t1`.
- **Values that may be live across an exception region** — must be in `slots[]` because exception handling unwinds SP.

### Relationship to Existing Passes

| Pass | Relationship |
|------|--------------|
| `UopHeuristicPass` | Its `DataFlowSameLocalBinary` already does similar data-flow tracking through the stack. The heuristics become special cases of the general allocator. |
| `LoopCsePass` | Creates temp locals for hoisted values. These temps use the same slot array. The allocator could promote them to CLR locals if they're short-lived. |
| `ConstantFoldingPass` | Folding reduces µop count (e.g., `Modulo(x, 2^n)` → `BitwiseAnd(x, 2^n-1)`). Fewer µops → fewer stack ops, independently of this approach. |
| `UopOptimizer` | This pass would run after heuristics and CSE, before compilation. |

### What Makes This Hard

1. **Stack depth tracking through the compiled delegate.** Currently, `sp` is a mutable `ParameterExpression` in the compiled delegate. If µop A skips its push, µop B at the old stack depth sees a different `sp`. The pass must maintain correct `sp` values for µops that still use `slots[]`.

2. **`DupOp` creates aliasing.** `DupOp` produces two references to the same value. If that value is allocated to a CLR local, the dup is a copy — but the original may be mutated (e.g., by `AddImmOp` which modifies `top()` in-place). This is the same problem as SSA phi elimination on stack machines.

3. **`PopOp` optimization.** If a value is `PopOp`'d immediately after being produced (lowering artifact), the push+pop cancel. Detection is trivial at the µop level.

4. **CompilationContext API coupling.** Every µop's `ToExpression` calls `ctx.Push()`/`ctx.Pop()`. Adding an alternate path means either:
   - A new method signature (e.g., `ToExpression(ctx, allocatedVars)`)
   - A runtime branch inside `Push`/`Pop` on the context (check if current µop is allocated)
   - Post-processing: compile normally but then walk the expression tree and substitute slot access with locals

5. **`DupOp` as barrier.** The presence of `DupOp` for a value means that value has two live references. In the current IR, `DupOp` is only emitted for `AssignmentValueIsUsed` — it's the only place a value is referenced twice. The allocator must ensure the dup is eliminated or handled.

## Alternative Approaches

### A. Fused µop expansion (simpler, more targeted)

Instead of a general allocator, extend the existing µop fusion pattern already used by `IncLocalOp`, `CmpLocalLeOp`, `CmpLocalJmpOp`, `BatchReduceOp`, and `CountBitsOp`. For each common pattern:

- `loadlocal v; unary; storelocal v` → fused µop (no temp value on stack)
- `loadlocal v; binop v; storelocal v` → fused µop  
- `loadlocal v; addimm C; ...` → expand `AddImmOp` patterns with more context

**Pros**: No new infrastructure. Each fused µop's `ToExpression` uses CLR locals internally.  
**Cons**: Exponential growth in µop types. Every combination needs a new record class. Doesn't generalize.

### B. Post-compilation expression tree rewriting

Compile µops to expression trees as today, then walk the resulting expression tree and replace `slots[sp++]` / `slots[--sp]` patterns with local variables. Requires analyzing the expression tree's structure to identify which slot accesses correspond to which values.

**Pros**: No changes to µop types or lowering.  
**Cons**: Expression tree analysis is fragile (the JIT may have already optimized things differently). Hard to map back to correct SP tracking.

### C. µop-level register allocation (SSA-lite)

The full SSA plan (`docs/plans/abstract-interpretation-and-ssa.md`) already designs this:
1. Build SSA form from µops (stack reconstruction, dominator tree, phi placement, variable renaming).
2. Optimize in SSA (SCCP, DCE, GVN, LICM).
3. Destruct SSA back to µops with stack scheduling and local slot assignment.

The SSA destruction's stack scheduling phase naturally eliminates unnecessary push/pop — it tracks stack depth and reuses values already on the stack instead of re-pushing them.

**Pros**: The most powerful approach. Also enables constant propagation, dead code elimination, and LICM at the µop level.  
**Cons**: 5-phase pipeline with dominator trees, phi placement, and SSA destruction. Significant investment. The plan was written for a `byte[]` opcode format that no longer exists — it needs re-targeting to current µop records.

### D. Basic-block-scoped CLR local allocation (the sweet spot?)

A middle ground between targeted fusion and full SSA:

1. Use the existing µop list directly (no SSA construction).
2. Walk each basic block forward, simulating the stack.
3. For each value that's produced and consumed within the block with no intervening control flow, emit a CLR local instead of slots access.
4. Adjust SP tracking for µops whose stack position has changed due to eliminated push/pop.

**What this DOES capture:**
- Chained binary ops (`loadlocal a; loadlocal b; add; loadlocal c; add`), 4+ stack ops replaced with 2 local accesses
- Load-Use sequences for arguments
- IncLocalOp result not consumed further (skip the push entirely)

**What this does NOT capture:**
- Values that cross block boundaries (need phi nodes or stack slots)
- Values consumed by calls (arguments must be in the slot array)
- Loop-invariant values (that's LoopCsePass's job)

This approach avoids SSA entirely while getting the most common case. It's similar to what `DataFlowSameLocalBinary` already does, but generalized to all µop results, not just `loadlocal` pairs feeding a commutative binop.

**Estimated benefit:** Chained arithmetic is common in benchmarks (mandelbrot: `zx*zx>>S`, `zy*zy>>S`, nested addition chains in pixel calculation). Sieve is memory-bound (array operations dominate). NQueens is recursion-bound (call overhead). Collatz is compute-bound (tight while loop with decrement and increment). The chained arithmetic in mandelbrot's inner loop would be the primary beneficiary.

## Suspend/Resume Constraint

The VM supports PC-level breakpoint suspension. The compiled delegate is a single `Action<VmState>` that loops dispatching µops. On breakpoint hit:

```
SavedPC = pc; Status = Suspended; pc = codeLen;  // return from delegate
```

On resume, `Vm.Execute` restores `PC = SavedPC` and calls the delegate again. All state survives in `VmState` (`slots[]`, `FrameBase`, `CachedArgSlots`, `SP`, `PC`).

**The constraint:** any CLR local variable in the compiled delegate is lost when the delegate returns. If µop A's result is promoted to a CLR temp `t_A` and µop B (same basic block) consumes it, a breakpoint between A and B loses `t_A` — on resume the delegate starts fresh from µop B's PC with no `t_A` available.

This makes the suspend/resume model a first-class constraint on any register promotion scheme.

## SSA Solves Suspend/Resume

SSA's live-out analysis at each µop tells you **exactly** which values are live at any program point. This enables a targeted write-back strategy:

```
if (hit breakpoint at PC i):
    foreach (t, slot) in spills[i]:
        slots[slot] = t;           // write back live CLR temps
    SavedPC = i;
    Status = Suspended;
    pc = codeLen;
```

Each entry in `spills[i]` is a pair `(CLR local, slot_position)` computed during SSA destruction:

- **Definition**: SSA value `v` is assigned to CLR temp `t`.
- **Live-out of µop i**: `v` is used after µop `i`.
- **Spill slot**: a known offset in `slots[]` allocated during destruction's local slot assignment phase.

The write-back is inside the breakpoint check's taken branch — **zero cost in the non-debugging path**. The JIT doesn't emit the write-back code at all when `DebugMode` is false because the entire breakpoint check is gated by `state.DebugMode`.

### No SSA vs SSA with CLR promotion vs SSA with all-spill

| Aspect | No SSA (current) | SSA + CLR temps + spill-on-suspend | SSA + all values in slots[] |
|--------|---|----|---|
| Stack traffic per µop | `slots[sp++]` / `slots[--sp]` | `t = val` (CLR local, register) | `slots[sp++]` / `slots[--sp]` (same as current) |
| Total µop count | As lowered | Reduced (SCCP, DCE, GVN, stack scheduling) | Reduced (same SSA passes) |
| Suspend risk | None (everything in slots[]) | None (live temps written back on suspend) | None (everything in slots[]) |
| Typed locals | No (all long) | Yes (SSA carries type per value) | No (all long in slots[]) |
| Non-debug overhead | Baseline | Zero (write-back gated by DebugMode) | Zero |
| Non-debug benefit | None | Register allocation + fewer µops | Fewer µops only |

### Why this argues for full SSA

Block-local CLR promotion (Approach D) cannot safely handle suspension because it lacks global live-range analysis. Without SSA, you don't know which CLR temps are live across which µops — and you can't target the write-back. You'd have to either:

1. **Spill every promoted value at every breakpoint** (safe but wasteful — spills values that aren't live).
2. **Only promote values that are consumed within the same µop** (zero optimization window — producer and consumer must be adjacent).
3. **Disable optimization when breakpoints are set** (defeats the purpose — debugging is when you most want correctness).

Full SSA eliminates all three problems because the live-out set at each µop is a direct product of the SSA construction. **SSA isn't just a more powerful optimizer — it's the minimal analysis needed to safely promote register values in a suspendable VM.**

### Interaction with the existing breakpoint codegen

The current breakpoint check in `ProgramCompiler.Compile` (lines 79–93) is:

```csharp
var breakCheck = Expression.IfThenElse(
    Expression.AndAlso(
        Expression.Property(s, DebugModeProp),
        Expression.AndAlso(... breakpoint set check ...)),
    Expression.Block(                                    // taken branch
        Expression.Assign(Expression.Property(s, SavedPCProp), pc),
        Expression.Assign(Expression.Property(s, StatusProp), suspendStatus),
        Expression.Assign(pc, codeLen)),
    Expression.Block(typeof(void), execBody));            // not-taken branch
```

With SSA promotion, the taken branch grows a spill sequence:

```csharp
Expression.Block(
    ... spill t0 → slots[off0],
    ... spill t1 → slots[off1],
    Expression.Assign(Expression.Property(s, SavedPCProp), pc),
    Expression.Assign(Expression.Property(s, StatusProp), suspendStatus),
    Expression.Assign(pc, codeLen))
```

The spill instructions reference the CLR temp variables and slot array — both already in scope in the compiled delegate. Each spill is a single `Expression.Assign(slots[off], tempVar)`. No heap allocation, no runtime reflection, no per-µop bookkeeping.

### Resume path

On resume, the delegate starts fresh. It reads `state.PC` (the breakpoint site), `state.Stack.SP` (unchanged), and dispatches from the restored PC. The spilled values are already in `slots[]` at their correct positions. The SSA destruction's stack scheduling phase on the next µop sequence naturally reads from `slots[]` (spilled values) and may re-promote to CLR temps within the new block.

This means the `spills[i]` mapping must be available from the **original** SSA destruction pass, not recomputed on resume. The mapping is a static data structure produced once during optimization, like `FunctionEntry.LocalCount`.

## Open Questions

1. **What's the optimization budget?** Full SSA? Block-local promotion? Just more fused µops? The right choice depends on how much performance we need and how much complexity we want.

2. **How do we measure "short-lived"?** Heuristic: defined in block i, consumed in block i or i+1, not consumed by a call. But is one block boundary acceptable? Two?

3. **What about `DupOp`?** Currently used only for assignment-value-used. If we allocate the underlying value to a CLR local, the dup becomes a copy `var_t2 = var_t1`. The JIT should handle this well (copy propagation), but we need to track which CLR local maps to which µop position.

4. **How does SP get adjusted?** If µop A pushes and we skip it, SP doesn't increment. Later µop B expects SP at a certain depth. The passes before compilation (heuristic, CSE) use PC-relative µop access, not SP. But the compiled delegate's `sp` variable must be correct. The pass must emit explicit SP adjustments.

5. **Can we reuse the existing µop array structure, or do we need a new IR?** The µop array is a flat list. For block-local promotion, we need at minimum a list of block boundaries and per-block use-def chains. This could be a separate data structure passed alongside the µop array.

6. **Does this make compilation slower?** The expression tree building involves more CLR locals, which means more `Expression.Variable` calls and more complex expression trees. The JIT may take longer, but execution should be faster. Trade-off measurable with benchmarks.

7. **Can we piggyback on `UopHeuristicPass.DataFlowSameLocalBinary`'s infrastructure?** That heuristic already walks µops with stack depth tracking and pop/push counting. The `StackEffect` table covers all µop types. A generalized allocator could reuse this forward-walk infrastructure.

8. **What's the precedent in production VMs?** The Hotspot C2 JIT promotes stack values to registers during JIT compilation (not during bytecode-to-HIR lowering). Poly's approach is different — we control the µop IR and the expression tree compilation, so we can do this promotion before the JIT sees it. This is closer to what the Hotspot C1 compiler does during bytecode-to-HIR translation (stack-to-register mapping).

## Measurement Strategy

Before implementing any approach, establish baseline:

1. Current µop count for each benchmark (total µops executed, not just unique).
2. Current `slots[]` access count per benchmark per iteration.
3. Current ratio of push/pop µops to total µops.

After implementation:

1. Same metrics. Target: eliminate 30-50% of `slots[]` accesses in compute-heavy benchmarks (mandelbrot, collatz).
2. Wall-clock benchmark times.
3. Generated expression tree size (number of CLR locals, total expression nodes).

The µop-level tracing infrastructure (`state.Trace`, `VmTrace.LogUop`) could be extended to count slot access frequency — a `long[] SlotsAccessCount` field on `VmState` that increments on every compile-time push/pop.

## Relevant Prior Art (in this repo)

| Artifact | Relevance |
|----------|-----------|
| `UopHeuristicPass.DataFlowSameLocalBinary` | Forward-walk with `StackEffect` table + stack depth tracking. Closest existing code to a generalized allocator. |
| `LoopCsePass.CollectReadLocals` | Builds per-µop-range local read sets. Infrastructure for liveness scanning. |
| `StackEffect` table (UopHeuristicPass.cs:196) | Pop/push counts for all 60+ µop types. |
| `CompileFusedPattern` in Lowering.cs | `TryEmitCountBits`, `TryEmitStridedSet` — pattern for fusing µop sequences into compound operations with CLR locals. |
| `CompilationContext.GetOrCreateAlias` | Creates CLR `ParameterExpression` locals for array aliases. Precedent for cross-µop CLR locals. |
| `ComputeMaxDepth` (ProgramCompiler.cs:28) | Simple µop scan to estimate stack depth. Could be extended to compute exact per-block depth. |
| `CmpLocalLeOp`, `CmpLocalJmpOp` (MicroOperations.cs) | Fused µop types with local-access semantics. Currently unused — potential vehicle for targeted fusion. |

## Recommendation Path

The suspend/resume analysis changes the tier ranking. Block-local promotion (Approach D) cannot safely handle suspension without SSA's live-out analysis — it would require either spilling every promoted value at every breakpoint or disabling the optimization when debugging. Both defeat the purpose.

**Full µop-level SSA (Approach C) is the minimal correct approach** because:
- It provides the live-out analysis needed for targeted spill-on-suspend at zero non-debug cost.
- It reduces µop count (30–50% via stack scheduling), which directly reduces `slots[]` traffic.
- It enables typed CLR locals during destruction (values in `slots[]` remain typed during spilling).
- It unlocks the full optimization pipeline (SCCP, DCE, GVN, LICM) — the neurosymbolic loop will need all of these.
- The block-local promotion approach's code (use-def chain reconstruction, CLR local allocation, SP adjustment) is a subset of what SSA destruction already does. Building it independently is duplicate effort.

The plan in `docs/plans/abstract-interpretation-and-ssa.md` needs re-targeting from `byte[]` opcodes to `MicroOp[]` records. The SSA IR types (`SsaValue`, `SsaBlock`, `SsaInstruction`) remain valid; what changes is:
- **Stack reconstruction (Phase 1b)**: instead of decoding `byte[]` opcodes, iterate `MicroOp[]` calling `StackEffect` on each record.
- **µop type mapping**: `MicroOp` subclasses map to `SsaOpcode` enum values (e.g., `AddOp` → `SsaOpcode.Add`).
- **Destruction (Phase 3)**: instead of emitting bytecode, emit `MicroOp[]` records — the stack scheduling phase produces a compact µop sequence.

**Re-targeting estimate:** moderate effort. The SSA algorithms are standard; the work is in the µop ↔ SsaOpcode mapping and verifying the destruction produces correct µop sequences that pass the existing 1315-test suite.
