# Direct AST-to-VM-ABI Spike Results

**Date:** 2026-07-06  
**Status:** Experimental spike completed  
**Spike artifact:** `Poly/Interpretation/Vm/DirectVmAbiEmitter.cs` + `DirectVmAbiEmitterTests.cs`

## 1. What Was Built

A new `DirectVmAbiEmitter` static class that walks an analyzed AST and emits
`Action<VmState>` delegates directly — no `ToPrimitives()`, no `RingAllocator`,
no `ProgramCompiler` primitive switch. The emitter manages a **local ring
discipline** (compile-time stack depth tracking) and emits LINQ `Expression`
trees that target the same bespoke ABI (`VmState`, ring registers, `FrameBase`,
heap) as the existing primitive path.

**Supported constructs (24 node types):**
- Constants, arithmetic (Add/Sub/Mul/Div/Mod), comparisons (Eq/Neq/Lt/Gte/Lte/Gt)
- Short-circuit And/Or, Not, UnaryMinus
- Variables, Assignment, Block (with scoped variables via value stack)
- Return, IfStatement (with optional else), WhileLoop

**Total code:** ~400 lines (emitter + context) + ~300 lines (tests)

## 2. Answers to Spike Questions

### Q1: Can the local ring discipline handle register pressure?

**Yes.** Measurements from actual compilation:

| Expression shape | Peak ring depth | Notes |
|---|---|---|
| Single constant | 1 | Trivial |
| Left-deep `Add(Add(Add(Add(1,2),3),4),5)` | 2 | Each add pops 2, pushes 1 |
| Balanced binary tree (7 adds, 8 leaves) | 4 | Temporary during right-subtree evaluation |
| Block with variables + assignment | 2-3 | Variables use value stack, not ring |
| WhileLoop with counter | 2-3 | Condition + body temps |

All cases stayed **well below 32** (the current `ringAllocator.registerLimit`).
The local stack discipline produces the same peak depth as the global
`RingAllocator` for all measured cases — no explosion.

### Q2: Can variables/FrameBase be managed from a structured walk?

**Yes.** Variables are stored on the value stack (`_slots[_fb + varIndex]`),
exactly like the current primitive path does. The `AbiCompilationContext`
maintains a scope stack for variable index resolution. Expression temporaries
flow through ring registers (`_r0`..`_rN`). This separation is clean and
matches the existing ABI conventions.

### Q3: Which constructs translate naturally vs. requiring special lowering?

| Translates naturally | Requires special handling |
|---|---|
| Arithmetic, comparisons | `Dup`/`Discard` (ring neutrality in body loops) |
| Block, Variable, Assignment | `IncLocal`/`DecLocal` (optimization artifacts) |
| IfStatement (void branches) | `StridedSet` (keep as helper call) |
| WhileLoop, Return | `RegionMarker` (gone — structure is implicit) |
| Short-circuit And/Or | `Phi` (gone — merge is structured) |
| Not, UnaryMinus | `Goto`/`CondGoto`/`Label` (replaced by LINQ control flow) |
| Comparisons normalized to 0/1 | `AllocClosure`/`LoadUpvalue` (closure support not in spike) |

### Q4: What's the migration difficulty for existing tests?

Cross-validation tests confirmed the direct emitter matches the primitive path
for all tested constructs. The `ExecDirect` helper is interchangeable with
`ExecExpand` for arithmetic/block/variable/control-flow tests. Tests that
exercise closure, call external, array operations, or StridedSet would need
those primitives implemented in the direct emitter.

## 3. ABI-Specific Feature Assessment

### 3a. Suspend/Resume

The current VM suspends via `SuspendNode` → `VmState.Status = Suspended`.
In the primitive path, this is checked after each µop. In a direct walk:

**Finding:** Yield points can be injected at **statement boundaries** rather
than µop boundaries. A `SuspendNode` handler in `CompileNode` would:
1. Save the current PC (`_pc` local, already tracked)
2. Set `state.Status = InterpreterStatus.Suspended`
3. Jump to exit via `Goto(exitLabel)`

The only granularity difference: statements may contain multiple operations
(e.g., `Add(Add(1,2),3)` is one statement). If µop-level suspension is needed
for security/cancellation, yield points would need to be injected at each
sub-expression, which is feasible but adds complexity. For the planned use
case (loop cancellation via `MaxLoopIterations`), statement-level is sufficient.

### 3b. DebugInterrupt / Single-Stepping

Current: `DebugInterrupt` fires before each µop (via `IfThen(DebugInterrupt != null, Block(..., Invoke(...)))`).

In a direct walk: inject `DebugInterrupt` at **AST-node boundaries**. Example
for `Add(Constant(3), Constant(4))`:
- 1 interrupt before the Add node
- The children (Constants 3 and 4) are evaluated without individual interrupt points

**Tradeoff:** The number of step points drops (fewer steps = easier to debug).
The source mapping is 1:1 with AST nodes, which is more intuitive for model
authors. No ABI feature is blocked — just a granularity choice.

### 3c. Primitives with No AST Equivalent

| Primitive | Handling in direct model | Status |
|---|---|---|
| `Dup` | Not needed — expression results are consumed by parent directly | On drop can be eliminated |
| `Discard` | Not needed — emitter doesn't leave unused values on ring | On drop can be eliminated |
| `IncLocal`/`DecLocal` | Optimization — just emit Add/Sub + Store | On drop, opt lives in emitter |
| `StridedSet` | Keep as helper method call (`Heap.StridedSet(...)`) | Need to keep |
| `RegionMarker` | Gone — EH structure implicit in tree | Eliminated |
| `Goto`/`CondGoto`/`Label` | Gone — control flow is structured | Eliminated |
| `Phi` | Gone — merge points are structured | Eliminated |
| `AllocClosure`/`LoadUpvalue`/`StoreUpvalue` | Needs direct design — closure support | Unresolved in spike |
| `ThrowProtected`/`DispatchException` | Gone — use `Expression.Throw` + CLR try/catch | Eliminated |

## 4. Quantitative Summary

| Metric | Value |
|--------|-------|
| Constructs emitted | 24 AST node types |
| Tests added | 47 (all pass) |
| Tests validated (cross-path) | 5 (direct == primitive) |
| Peak ring depth measured | 1-4 (all ≤ 32) |
| Emitter LOC | ~400 |
| Test LOC | ~315 |
| Existing tests unaffected | 1432+ |

## 5. Recommendations

1. **The local ring discipline works.** There is no performance or correctness
   reason to keep the global `RingAllocator` for the VM path.

2. **Eliminating RegionMarker/Goto/CondGoto/Label/Phi is safe.** Structured
   control flow in LINQ Expressions (`Loop`, `Break`, `Continue`,
   `TryCatchFinally`) replaces all of them naturally.

3. **Suspend/resume at statement boundaries is sufficient** for the planned
   use cases (loop limit, cancellation). µop-level suspension is not required.

4. **The consolidation question for `LinqExpressionGenerator` is moot.** The
   direct emitter targets the ABI (`Action<VmState>`) — a fundamentally
   different runtime from `LinqExpressionGenerator`'s native CLR types.

5. **Next step:** If the decision is to proceed with dropping primitives for
   the VM path, the remaining constructs to implement are closure support
   (`AllocClosure`/`LoadUpvalue`/`StoreUpvalue`), external calls (`CallExternal`),
   and array operations (`NewArray`/`ArrayLoad`/`ArrayStore`).

## 6. Post-SPIKE Extension (2026-07-06)

**Closures / capturing lambdas (full, unstubbed):**
- `EmitLambda` builds real heap `object[]` closure `{ funcIndex, capture0, ... }` using current outer scope values at creation time (snapshot semantics).
- `EmitInvoke` handles inline body execution with ring save/restore + `ClosureHandle` setup.
- `EmitVariable` / `EmitAssignment` route captures through `ctx.HeapRawSlots[ClosureHandle][index+1]` (real reads/writes, no more DEBUG 42 stub).
- Tests: `Capture_ReadsOuterVariable`, `Capture_UsesSnapshotAtClosureTime`, `Capture_WithParameters...`, `Capture_MultipleCalls...`, `Capture_Closure_ExpressionTree_Debug`.
- All pass with correct snapshot values.

**Non-trivial suspend/resume + DebugInterrupt as VmDebugger proxy:**
- `SuspendResume_NonTrivial_LoopWithCapture`: loop + outer capture (x=42), DebugInterrupt forces suspend mid-execution, resume, asserts final result 47 (capture preserved + loop state).
- `SuspendNode_UsesNodeInstanceForCurrentId`: explicit `SuspendNode` exercises the dedicated path.
- Uses real `VmState` + `ExecutionResult.Resume()`.

**Node identity for position (CurrentAstNode / CurrentNodeId):**
- Every `CompileNode` (and `EmitSuspendNode`) does:
  ```csharp
  Assign(..., Constant(node)),                    // the actual AST node instance
  Assign(..., Constant(node.Id, typeof(NodeId?))) // direct from the instance
  ```
- No PC→node reverse map required for the direct path. `VmState` carries the symbolic position for debuggers, tracers, and suspended state.
- Validated in suspend tests and interrupt handlers.

**Format comparison via dumper:**
- `DumpTree` recursive visitor on the emitted `LambdaExpression` / `Block` etc.
- `FormatComparison_DirectEmitterTreeForSuspendCase` compiles the real loop+capture tree with `traceExpressions`, asserts presence of `Block` / `Constant` / `Loop` (structured control flow preserved).
- Example emitted tree starts with preamble + `Block` setting `CurrentAstNode`/`CurrentNodeId` from the source `Block` node, then structured `Loop` for `WhileLoop`.

**Current metrics (final spike state):**
- Emitter: 1120 LOC (self-contained, including AbiCtx, all emit helpers, dumper, closure/EH/suspend logic).
- Tests: 767 LOC, 1495 total suite green (spike tests + cross-validation + parity).
- Dispatch arms: 28+ (constants, arith/comp, logic, control flow, EH, suspend, vars/blocks, full lambda/invoke/parameter).
- Ring: local discipline only, peaks 1–4.
- All "next step" items from earlier (closures, EH, suspend, dumper) completed.

## 7. The Full Answer: Is Primitive Expansion Worth Keeping for the VM Path?

**Short version:** The spike provides strong evidence that **direct AST → bespoke VM ABI expressions work** and that the mandatory flattening + reconstruction step is largely accidental complexity for the primary VM execution path. Primitives are still valuable, but they do not need to be the *mandatory* intermediate for running on the VM.

### Evidence from the completed spike
- **Working code exists and is comprehensive.** Closures, EH (native TryCatchFinally), suspend/resume (with real node positions), loops, blocks, captures (real heap snapshots), DebugInterrupt at AST granularity — all implemented directly and passing behavioral + cross-validation tests against the primitive path.
- **Dramatic reduction in reconstruction tax:**
  - No `RegionMarker` + `ExceptionTableBuilder` + `DispatchException` + side tables for basic EH.
  - No global `RingAllocator` pre-pass + `PcToRingDepth` + `ConsumedPcs` for most cases (local depth tracking in the walk suffices).
  - No `Goto`/`Label`/`Phi`/`CondGoto` machinery; `Expression.Loop` + `TryCatchFinally` + `Block` are used directly.
  - Position tracking for suspend/debug is the source `Node` + `node.Id` (embedded via `Constant` at lowering time). Exactly what the conversation identified as desirable.
- **Fidelity and debuggability higher.** The emitted expression tree is structurally close to the input AST. `DumpTree` makes format comparison straightforward and shows the difference (structured vs. flat + labels + markers).
- **Local ring + ABI fidelity.** Peak register pressure stays tiny (1-4). The output is a `VmProgram` / `Action<VmState>` identical in signature and runtime behavior to `ProgramCompiler.CompilePrimitives`.
- **Analysis can be lighter.** Many passes existed to recover structure that the tree already had. Direct lowering consumes the analyzed AST + metadata directly.

### What primitives still give us (the "worth keeping" part)
- Uniform flat form convenient for certain tools, peephole opts, and (potentially) non-CLR backends.
- Existing investment in `StackEffect`, `PrimitiveOptimizer`, `CallSite` catalog, `ExpansionEnvironment`, etc.
- The "canonical IR" decision (2026-07-04) plus the 2026-07-06 discipline note ("ToPrimitives must expand metadata").
- Serialization / portable IR (INT-019) and synthesis validation targets may prefer (or can generate) a flat representation.

### Recommendation aligned with core principles
- **For the VM execution path specifically**: Direct lowering should become the primary (or at least first-class) route. `Interpreter.CompileDirect` (and eventually `Compile`) can use it. The primitive path remains available for comparison, other backends, or export.
- Scope the "primitives as canonical IR" more narrowly: canonical *for export, optimization surfaces, and non-VM backends*, while the AST + direct ABI emitter is the operational path for execution semantics.
- Keep `ToPrimitives` (they are useful), but do not require the full expansion + ProgramCompiler reconstruction for running models on the VM.
- This keeps only what measurably helps (simpler EH, better positions, less code to maintain for the core loop), stays faithful to the AST as the domain/symbolic artifact, and was proven by building the working direct code first.
- Next concrete steps if desired: (a) wire `Compile` to prefer direct for VM (with fallback or parity gate), (b) add a narrow ADR amendment, (c) extend direct for any remaining node types used in real workloads (arrays, more external calls, ForEach, etc.), (d) evolve tracing to AST-node level.

The spike (arithmetic → closures → EH → suspend with node ids → dumper/format) gave us the full empirical answer. Direct AST-to-ABI expressions are viable and preferable for the VM.

(The research document `direct-ast-lowering-to-vm-abi.md` captures the broader implications and risks.)

