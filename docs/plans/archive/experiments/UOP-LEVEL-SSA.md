# Experiment: µop-Level SSA Construction

## Hypothesis

Building SSA form from the lowered µop array and running optimizations (DCE, SCCP, GVN, LICM) there would eliminate unnecessary stack traffic through `slots[sp]` — the proven bottleneck. The SSA destruction phase would assign values to local slots, bypassing the array bounds-checked `long[]` entirely.

## What was built

| Component | Description | Status |
|-----------|-------------|--------|
| `SsaTypes.cs` | `SsaValue`, `SsaInstruction`, `SsaBlock`, `SsaProgram` | Built |
| `SsaBuilder.cs` | µop → SSA: block discovery, stack reconstruction via `StackEffect` table, iterative phi placement, reaching-definition tracking for locals/args | Built |
| `SsaDestructor.cs` | SSA → µop: two variants (simple PC-order emission with jump remapping, and spill-all with per-value local slot assignment) | Built |
| `SsaDominators.cs` | Lengauer-Tarjan dominator tree, dominance frontiers, natural loop detection | Built |
| `SsaDeadCodeElimination.cs` | Mark-sweep DCE | Built, not wired |
| `SsaOptimizer.cs` | Orchestrator, wired into `UopOptimizer` pipeline | Built (round-trip only) |
| `SsaRoundTripTests.cs` | 7 tests: round-trip, locals, control flow, dominators | 1320 passing |

## Why it was overly complicated

### 1. Stack-machine reverse engineering

The µop array is a stack machine — values flow through implicit `slots[sp]` positions, not named variables. The builder must reconstruct which µop produced which stack value and which µop consumed it. This is the hardest part of the plan and the most error-prone. Every edge case (DupOp keeping the original on the stack, DivRemOp pushing two values, IncLocalOp reading and writing a local) requires special handling in both the builder and destructor.

### 2. Stack effect mutation by optimization passes

In a register-based SSA, removing an instruction is safe — its result is simply unavailable for subsequent uses. In a stack-based µop sequence, removing an instruction changes the stack depth for every subsequent µop. Every pass (DCE, SCCP, GVN) must preserve the exact stack effect, or the destructor must insert compensating PushOp/PopOp µops. This means the passes aren't independent — they require the destructor to understand stack balance.

### 3. Call/return ABI

`CallOp`, `CallClosureOp`, and `CallExternalOp` change `FrameBase` and `CachedArgSlots` on the `VmState`. The spill-all destructor's `StoreLocalOp(slot)` after a `CallOp` uses the callee's `FB`/`CAS`, corrupting the callee's frame. Special-casing call µops in the destructor is possible but adds another exception to an already exception-heavy design.

### 4. µop count explosion

The spill-all destructor wraps every µop with `LoadLocalOp`/`StoreLocalOp` pairs, roughly doubling the µop count. While DCE should remove dead code faster than the spill adds, the net benefit isn't guaranteed until all passes are working. The simple PC-order destructor adds zero overhead but makes most passes unsafe.

### 5. Limited optimization surface at µop level

Many optimizations that would benefit execution are better expressed at the AST level: type-directed constant folding, variable lifetime shortening, function inlining, dead branch elimination. At the µop level, all values are `long` and all function calls are opaque. The AST carries the type and structure information that makes SSA optimization valuable.

## What survived

The µop-level heuristic pass (`UopHeuristicPass`) remains. It handles the one thing AST-level passes cannot: stack-machine fusion patterns like `loadlocal v; loadlocal v; binop` → `loadlocal v; dup; binop`. This is a simple pattern match on the µop sequence, not a full SSA construction.

## Recommendation

Build SSA at the **AST level** (before lowering to µops) using the existing `AnalysisContext` infrastructure. The AST already has:
- Named variables with types (not uniform `long` slots)
- Structured control flow (not raw `JumpOp` targets)
- Function boundaries (not a flat µop array with function bodies appended)
- An existing pass pipeline (`INodeAnalyzer`, `AnalysisContext.Metadata`)

The µop-level heuristic pass continues to handle stack fusion. The AST-level SSA handles everything else.

## Files to remove

| File | Lines |
|------|-------|
| `Ssa/SsaTypes.cs` | 99 |
| `Ssa/SsaBuilder.cs` | 318 |
| `Ssa/SsaDestructor.cs` | 177 (simple version) |
| `Ssa/SsaDominators.cs` | 128 |
| `Ssa/SsaDeadCodeElimination.cs` | 100 |
| `Ssa/SsaOptimizer.cs` | 42 |
| `SsaRoundTripTests.cs` | 142 |

Cleanup: revert `UopOptimizer.cs` to remove SSA import and call.
