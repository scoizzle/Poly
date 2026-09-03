> **Note (2026-07-10):** This experiment may reference pre-direct-ABI plans. Those plans live under `docs/plans/archive/interpretation/`. Prefer `DirectVmAbiEmitter` + current decisions for new work.

# Experiment: Linear µop Compilation (Label/Goto Dispatch Removal)

## Hypothesis

Replacing the switch-loop dispatch with a flat `Label`/`Goto` sequence would eliminate per-µop overhead: the `pc < count` check, the switch dispatch, and the `pc++` increment. The resulting delegate would be straight-line IL with branches only at `jmpf`/`jump` µops.

## Implementation

Built `CompileLinear` in `ProgramCompiler.cs` alongside the existing `Compile` (switch dispatch). Key design decisions:

- No loop, no `pc` variable in the common case
- Each µop position gets a `LabelTarget`
- Regular µops fall through to the next label (no explicit `goto`)
- `JumpOp` emits `Goto(target_label)`
- `JumpIfFalseOp` emits `IfThen(pop == 0, Goto(target_label))`
- `ReturnOp` emits `IfThen(fb < 0, Goto(exit))`
- `CallOp`/`ReturnFromCallOp`/`CallClosureOp` write back SP, call a frame-setup helper, then jump through a shared dispatch switch that reads `state.PC` and routes to the correct label
- `CommentOp` skips entirely (no operation, fall through)
- Breakpoint checks gated by `s.DebugMode` at runtime (emitted for every µop)
- `loadlocal v; single-tos-op; storelocal v` pattern fused into a single read-modify-write expression (eliminates two push/pop pairs)

## What went wrong

### 1. `state.Stack.Reserve()` modifies SP

`ValueStack.Reserve(int count)` both ensures array capacity **and** adds `count` to `SP`. The switch path's `Reserve(maxDepth)` at entry inflated `state.Stack.SP` while the local `sp` variable was set before the call. After `HandleCallLinear` read `state.Stack.SP` (the inflated value), all frame computations were wrong. Fixed by adding `WritebackSP()` before `HandleCallLinear`, mirroring the switch path's approach.

### 2. `Goto` cannot reach `LabelTarget` in nested `BlockExpression`

The initial design put all µop labels inside a nested `Expression.Block(body)` inside the entry block. The entry switch's `Expression.Goto(label_0)` could not reach labels defined in the nested block. Fixed by flattening everything into a single `Block` — labels, entry code, dispatch switch, and exit all in one scope.

### 3. `State.Stack.SP` vs local `sp` divergence

The local `sp` variable in the compiled delegate and `state.Stack.SP` must be kept in sync. Every `CallOp`/`ReturnFromCallOp` needed explicit `WritebackSP()` before and `ResyncSP()` after, adding complexity and defeating some of the simplicity benefit.

### 4. **No measurable performance gain**

The expression tree `switch` with a jump table and the `Label`/`Goto` sequence produce nearly identical IL. The switch path's `pc < count` check and `pc++` are single register operations — not a bottleneck. The linear path removed them but added fall-through guards and dispatch switch overhead. Benchmarks were indistinguishable within noise (±2%).

## What was learned

1. **The switch was never the bottleneck.** The JIT compiles `Expression.Switch` with enough cases into a proper jump table — as fast as `Goto`. The per-µop overhead from the loop (`pc < count`, `pc++`, jump back) is approximately zero in practice because the JIT keeps `pc` in a register and the loop is tight.

2. **Push/Pop through `slots[]` is the bottleneck.** Each µop's `slots[sp++]` / `slots[--sp]` goes through array bounds checking and memory traffic. No dispatch change reduces this.

3. **Data-flow tracking is the remaining lever.** The only thing that would meaningfully speed up µop execution is eliminating VM stack traffic for short-lived intermediate values — replacing `slots[sp]` with CLR locals for values produced and consumed within a small µop window. This requires a liveness analysis pass, not a dispatch change.

4. **Two compilation paths add maintenance cost.** Keeping both `Compile` (switch) and `CompileLinear` with an `IsCallFree` guard added complexity for zero benefit. The switch path handles all cases correctly.

## Outcome

**Rejected.** The linear path was removed. The switch path (`ProgramCompiler.Compile`) remains the sole compilation path. A full write-up of the data-flow tracking approach exists in `docs/plans/neurosymbolic-platform-from-first-principles.md` (the CLR-local allocation section).

## Files touched

| File | Change |
|------|--------|
| `ProgramCompiler.cs` | Added `CompileLinear`, `IsCallFree`, `TryFuseLoadOpStore`. All reverted. |
| `Vm.cs` | Added `HandleCallLinear`, `HandleCallClosureLinear`, `HandleReturnFromCallLinear`. All reverted. |
| `Bytecode.cs` | `EnsureCompiled` switched between `Compile` and `CompileLinear` via `IsCallFree`. Reverted. |
| `MicroOperations.cs` | `TraceBefore` gated by `state.Trace != null` (kept — independent improvement). |
| `ProgramCompiler.cs` | CommentOp skip in `Compile` (kept — independent improvement). |

## When to revisit

Re-evaluate if:
- The expression tree compiler gains a `Goto`-elimination optimization that makes linear sequences faster than switch dispatch
- A future .NET version provides an intrinsic for label-relative dispatch that outperforms jump tables
- The data-flow tracking pass is built and wants to emit fused µops that don't fit the switch-dispatch µop model
