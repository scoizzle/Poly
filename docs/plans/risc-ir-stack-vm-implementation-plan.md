# Implementation Plan: Bytecode VM for the Reference Interpreter

**Status**: Active implementation — Phases 0–4 complete, Phase 5 in progress  
**Owner**: [To be assigned]  
**Related**: 
- `docs/plans/v2-to-v3/tree-walking-interpreter-design.md`
- `docs/decisions/2026-05-31-neurosymbolic-platform-vision.md`
- `docs/decisions/2026-06-post-lowering-insight-analysis.md`
- `docs/plans/v2-to-v3/workstreams/ws8-analysis-unification-and-lowering.md`
- AGENTS.md (core principles, placement rules, build/test discipline)

## Current Implementation Status

The original 8-phase plan has been partially executed. The current codebase has diverged from the plan's naming conventions and architecture in several ways:

| Plan name | Actual name | Status |
|-----------|-------------|--------|
| `RiscInstruction` (enum + record) | `OpCode` (enum) — raw byte opcodes with immediate operands | Complete |
| `RiscProgram` | `Bytecode` — `byte[] Code` + source map + constants + call sites + regions | Complete |
| `RiscValueStack` (byte-backed pool) | `ValueStack` (`int[]` — 4-byte slots with generic `Push<T>`/`Pop<T>`) | Complete |
| `RiscHeap` | `Heap` (`List<object?>`) | Complete |
| `RiscFrameHeader` | `FrameHeader` (4-int struct, `MemoryMarshal` blit) | Complete |
| `RiscState` | `VmState` | Complete |
| `RiscVm` | `Vm` (dispatch loop) | Complete |
| `RiscLowering` | `Lowering` | Complete |
| `RiscOp` (8-byte aligned) | `OpCode` (1-byte) + byte immediates | Complete |
| Signed handles (pos=heap, neg=stack) | `LoadValue`/`StoreValue` (int handle + int size) | Partial |
| 8-byte byte stack | 4-byte `int[]` stack | Diverged |

### What's Implemented (40+ AST node types)
- Arithmetic: Add, Sub, Mul, Div, Mod, Neg (int, signed/unsigned); DAdd–DGe (double) via `ResolveBinaryOp`
- Comparisons: Eq–Ge, ULt–UGe, DEq–DGe (type-aware via `ResolveBinaryOp`)
- Boolean: And/Or (short-circuit via Dup+JumpIfFalse), Not
- Flow control: Conditional, If/Else, While/DoWhile/For, Break/Continue, Goto/Label, Switch
- Variables: LoadArg/StoreArg (params), LoadLocal/StoreLocal (locals), `DiscoverLocals` pre-scan
- Data: Assignment (param/local dest), Coalesce, Default, NullForgiving
- Member access: property getter, field getter, array indexer (via `CallSiteCompiler`)
- Object allocation: `New` lowered to constructor call (via `CallSiteCompiler.CompileConstructor`)
- Exception: TryCatchFinally with region table, Throw, EndFinally
- Interrupts: Int vector, Iret
- Calls: Call (internal Poly functions), CallExternal (CLR via `CallSiteDelegate`)
- CLR interop: `CallSiteCompiler` generates typed `CallSiteDelegate` for any `MethodInfo`, `ConstructorInfo`, or `FieldInfo`
- Type-aware lowering: `ResolveBinaryOp` checks `analysis.GetResolvedType(node)?.GetRuntimeType()` to emit correct op for double/uint paths

### Phase 4 Complete
The 5 priority items from Phase 4 are now fully implemented:

1. **LoadConst type preservation** — heap preload restored, `LoadConst` pushes heap handle, `PushDouble` opcode with `TryInlinableDouble`
2. **Member/IndexAccess/New lowering** — property get, field get, indexer get, constructor call via `CallSiteCompiler`
3. **Non-param Variable support** — `DiscoverLocals`, `LoadLocal`/`StoreLocal` opcodes, `FunctionEntry.LocalCount`, local frame slots
4. **Type-aware lowering** — `ResolveBinaryOp` emits DAdd/DSub/DDiv/DNeg/DLt/etc. for double, UDiv/UMod/ULt–UGe for uint
5. **Cross-path parity test harness** — 25 tests in `VmTreeWalkingParityTests.cs` that run AST through both tree-walker and VM, asserting identical results (with `bool`→`int` normalization)

### Known Gaps (Phase 5 items)
1. **Block intermediate values not popped** — lowering emits consecutive expressions without popping intermediate results, causing stack buildup (surface for ints, breaks doubles/longs)
2. **Result extraction uses PopInt only** — `Vm.Execute` calls `PopInt()` for final result; doubles/longs (2-slot values) produce garbage; heap handles for strings/objects are returned as raw ints
3. **Lambda/closures** — closure environment allocation + captured-variable storage (upvalues)
4. **Await** — async state machine frames

## Context & Motivation

The current tree-walking interpreter (`Poly/Interpretation/TreeWalking/TreeWalkingInterpreter.cs` and supporting types) has become complex for the simplicity it was intended to represent:

- Recursive `ExecuteCurrentNode` + big `switch` on ~25 node kinds.
- Ad-hoc resumption (e.g. `BlockIndex:{hash}` metadata in `EvaluateBlock`, duplicated signal handling in loops).
- Runtime re-interpretation of analysis metadata (`CanElide`, `GetNodeReplacement`, `IsPure`, resolved members) inside the hot path.
- Interleaved concerns (breakpoints, `ITreeWalkerCompiler` plugins, insight registration, CLR fallbacks via reflection, `AnalyzeForEvaluation` auto-pipeline).
- `EvaluationStack` (object?) and `CallStack`/`StackFrame` exist but are under-utilized for a true "stack machine" (the `DataStack` prototype in `Testing.cs` shows the intended byte-backed direction).

Meanwhile, analysis has matured significantly:
- `SideEffectAnalysisPass` as proper DCE (`AggregateChildren`, flyweights `NoSideEffects`/`Elidable`, sparse metadata, direct Block handling, intra-loop elision).
- `ControlFlowAnalysisPass` owning reachability, termination (pure-infinite vs effectful), pruning, `MustExecuteMetadata`, sound `HasMutationToVars` (external state, `Suspend`, non-pure Invoke, volatile via `Mutability`, const skipping).
- `ConstantFoldingPass` + `NodeReplacementMetadata`.
- First-class `Mutability` enum on `ITypeMember` (with `CompileTimeConst` implying `ReadOnlyAfterInit`, Clr/AST impls, consumption in purity/mutation/emission).
- Resolved members + `ITypeMember` delegates for fast paths.
- All of this is already exploited by the generators (`LinqExpressionGenerator`, `CSharpGenerator`) at "lowering time" for DCE/elision/replacements.

The neurosymbolic platform vision (and tree-walking design doc) requires:
- The interpreter as **canonical semantics** (fast feedback, macro validation, conformance suite that other backends must match).
- Suspendable, introspectable, re-analyzable lowered code (for post-lowering insight, `SuspendedExecution`, registered `INodeAnalyzer` + `ILiveStateAnalyzer`, re-analysis on resume).
- Evolvable to bytecode ("the tree-walker can inform what operations the bytecode needs").
- Explicit VM model even for tree-walking (`EvaluationStack`, `CallStack`, `StackFrame`, `InterpreterState`).
- Analysis as the compiler frontend; execution (tree-walker or future VM) as a tier.

The design we converged on (via extensive pseudo-code iteration) is:
- Full analysis pipeline → lowering to a tiny linear **RISC IR**.
- Execute the IR with a **simple stack VM**.

**Implementation placement (explicit direction):** All artifacts (IR definitions, source maps, lowering, stacks, heap, state, tiny VM loop, frame segment handling, CALL_EXTERNAL marshaling, suspend integration) live under `Poly/Interpretation/VirtualMachine/`. Type names keep the "Risc" prefix (reflecting the bespoke minimal RISC IR); the directory and primary namespace is `VirtualMachine`. This follows the project's module boundaries (execution engines / interpreters under `Interpretation/`, parallel to the existing `TreeWalking/` implementation during transition). See Phase 0 for the concrete initial layout.

**Initial directory & namespace (target after Phase 0 skeleton):**
```
Poly/Interpretation/VirtualMachine/
    RiscInstruction.cs          // enum RiscOp + RiscInstruction record
    RiscProgram.cs              // instructions + instrToNode (NodeId source map) + consts + externals
    RiscValueStack.cs           // byte[] + sp + ReserveBytes(size)→Span<byte> + growth + patching
    RiscHeap.cs                 // List<object?> wrapper; positive indices = heap handles
    RiscState.cs                // stack bytes/sp/tags, frameBases, heap, pc, program, AnalysisResult...
    RiscFrameHeader.cs          // (optional) constants or layout record for on-stack headers
    RiscVm.cs                   // tiny dispatch loop (internal static Execute or class)
    RiscLowering.cs             // (Phase 2) AnalysisResult + Node → RiscProgram
```
Namespace for all: `Poly.Interpretation.VirtualMachine` (or a nested `.Risc` if preferred for the IR concept; start flat under VirtualMachine). No public surface until proven. Internal types + one routing point in the existing interpreter entry.

**Key model elements (recap of the converged design)**
- Byte-backed operand stack (`ValueStack` / `RiscValueStack`): 8-byte aligned slots, direct `Span<byte>` writes (no temps), `ReserveBytes(size)`, offset peeking.
- Separate (removable) tag array (for development/insight safety; removal is "trivial" once IR is fully typed and lowering is strict).
- `List<object?>` heap for references (indices are the "handles").
- Frames as **fixed segments of the stack** (minimal `frameBases: List<int>` of base offsets + on-stack headers containing `retPC`, `savedPrevBase`, `callerPerspectiveSP` used only at creation time).
- Signed handles: `>= 0` = heap index; `< 0` = negated absolute index (byte offset) in the stack. Resolution for negative: `real = -handle` (absolute). The frame's entry point is used only at creation time to compute the absolute offset from a relative position within the frame (e.g. `absolute = frame_base + relative_delta; handle = -absolute`). Handles (as negated absolutes) are portable across calls; no live PC or current sp is used at dereference time for provided stack references. On growth, patch live negative handle values on the stack (in addition to frame bases).
- Size pushed on stack before `LOAD_VALUE`/`STORE_VALUE` (pops size + signed handle; sign selects memory domain; direct copy of exactly `size` bytes into reserved padded slots). This unifies handling of primitives + variable-sized payloads (structs) + indirect access through stack handles.
- `LOAD_REF` moves the signed handle itself (lightweight "address"); `LOAD_VALUE` dereferences to bytes.
- Internal `CALL` (target PC) + `CALL_EXTERNAL` (resolved delegate/site or heap delegate handle).
- `RETURN` does O(1) truncation to the caller's segment boundary (`sp = preArgSP` using saved info + arg size from signature).
- Stack references (negative handles) enable by-ref to ancestor segments, including "heap-ref cells" (a stack slot holding a positive heap index / "reference to the heap"). Callee can read the current heap ref or mutate the cell to point at a different heap object.
- CLR interop for delegates/type methods via `CALL_EXTERNAL` (prefer pre-resolved typed delegates from `ITypeMember` / analysis; typed `ref`/`out` via C# `ref` local temp + sync-back using the original stack handle + `STORE_VALUE` for heap-ref cells; direct spans + minimal boxing).
- Full suspend/resume/introspection: `SuspendedExecution` captures byte buffer + heap + `frameBases` + `pc` + source map (`instrToNode`). Insight analyzers run on suspend. Re-analysis on resume can produce a better lowering.

- The tree-walker remains available (as fallback/bootstrap) during transition but the RISC path (implemented under `Poly/Interpretation/VirtualMachine/`) becomes the primary for the reference interpreter.
- All analysis complexity (elision, folding, control simplification, purity via `Mutability`, resolved members) is applied once at lowering time. The VM is a tiny, uniform loop.
- Preserve: exact test observables (especially the 1200-cycle suspend/reanalyze soak, cross-engine fuzz vs Linq/C#, lifecycle/breakpoints, analysis policy), node fidelity (`AtNode`, breakpoints, insight on original `Node`), re-analyzability, no extra C# stack pressure for data values, minimal core.

This directly addresses the original complaint ("the tree walking interpreter is too complicated for the simplicity it was supposed to represent") while delivering the full neurosymbolic requirements and the stack-ref + CLR-ref interop scenarios we explored.

## Goals & Non-Goals

**Goals**
- A dramatically simpler reference execution engine (tiny dispatch, explicit stacks, uniform handling of stack/heap via signed handles + size).
- Full support for the evolved model (stack refs for by-ref to ancestor segments including heap-ref cells, frames as stack segments, signed handles as negated absolutes for stack locations, size-on-stack loads/stores, direct destination spans).
- First-class CLR interop for delegates/type methods, including `ref`/`out` arguments that are stack handles (negated absolute indices) pointing at heap-ref cells (via temp + typed delegate + sync-back using the stack handle + STORE_VALUE).
- Seamless integration with suspend/resume, `SuspendedExecution` + insight analyzers (live state + node targeting), re-analysis on resume.
- 100% test parity (identical observables to tree-walker path for all 1200+ tests, including complex suspend + mutation + re-analysis scenarios).
- Analysis remains the single source of truth (lowering consumes `AnalysisResult` + all metadata).
- Easy future evolution (additional backends, JIT hints from the IR, post-lowering insight on the RISC form itself).
- Alignment with AGENTS.md (working code before abstractions, first consumers for guardrails, keep only what measurably helps, domain model as key artifact, explicit analysis policy, etc.).

**Non-Goals (for this plan)**
- Immediate removal of the tree-walker (keep as fallback + for bootstrap/early validation until RISC path is proven).
- Full dynamic plugin model or late-bound everything upfront (defer `ITreeWalkerCompiler` until core is solid).
- Production-hot-path optimizations or full AOT/JIT (focus on reference interpreter + test parity first).
- Changes to Linq/C# generators or Domain lowering (they can consume the same analysis metadata independently).
- New public API surface until the RISC path has demonstrated value via tests and the scenarios we discussed.

## Phased Approach (Incremental, "Make Working Before Perfect")

### Phase 4 (Current): Type-Aware Lowering + Object Model — The 5 Priority Items

**Goal**: Close the gap between the tree-walker and the VM for all core expression types, enabling cross-path parity tests.

**Tasks** (implemented in priority order):

**4a. LoadConst type preservation** (highest impact per effort)
- Restore constants heap preload so all non-inlineable constants reside on the heap at startup
- Change `LoadConst` to push the heap handle (constant index) as the stack value instead of `val is int iv ? iv : 0`
- Add `TryInlinableDouble` + `PushDouble` opcode so `Constant(3.14)` emits inline 2-slot push
- Add `TryInlinableString` — strings that are heap objects get a heap handle pushed via `LoadConst`
- Fix `LoadConst` handler to push doubles via `Push<double>()` when the constant is double

**4b. Member / IndexAccess / New lowering**
- Lower `Member` (property/field read) to `CallExternal` via compiler-generated accessor sites
- Lower `IndexAccess` to `CallExternal` indexer getter
- Lower `New` to `CallExternal` constructor call + heap allocation
- Requires completed 4a (so object references can flow as heap handles)

**4c. Non-param Variable support**
- Add local variable area to the frame header (local slot count)
- Lower `Variable` nodes not in `paramIndexMap` to `LoadArg`/`StoreArg` into the local area
- Lower `Assignment` to non-param destinations similarly

**4d. Type-aware lowering**
- Thread expression type information through the lowering pass
- Emit `DAdd`/`DSub`/`DMul`/`DDiv`/`DEq`–`DGe` when operands are double
- Emit `UDiv`/`UMod`/`ULt`–`UGe` when operands are unsigned
- Emit `Add`/`Sub`/`Mul`/`Div`/`Eq`–`Ge` for signed int (default / remainder)

**4e. Cross-path parity test harness**
- New helper `AssertAllEnginesMatch(Node ast)` that runs tree-walker, LINQ path, and VM bytecode on the same AST and asserts identical results
- Start with arithmetic/comparison/control-flow subset
- Expand as each of 4a–4d extends VM coverage
- Add regression tests for every gap discovered

**Verification gate**: Full test suite (1220 passing). Each of 4a–4d has dedicated tests. Parity harness covers all newly working scenarios.

### Phase 5 (Next): Block Cleanup, Multi-Slot Results, Lambda/Closures, Await

**Goal**: Fix remaining correctness issues and support the full interpretable AST surface.

**Tasks** (implemented in priority order):

**5a. Block intermediate value popping** (small, high impact)

Problem: Lowering's `case Block` emits each expression sequentially without popping non-last results. After `Block([PushInt 1, PushInt 2])`, SP=2 with both values on the stack. The last PopInt reads `2` but leaves `1` below. This means:
- `Pop<double>()` would read both slots as a double (wrong for two separate ints)
- Stack has stale data that interferes with subsequent operations
- Prevents us from using SP-count to infer multi-slot results

Fix: In `Emit(Block)`, emit `Pop` after each expression except the last.

Risk: Some expressions are void (e.g., `IfStatement` without else, `ThrowStatement`). After a void expression, `Pop` would consume the previous result. Mitigation: skip `Pop` when the expression's resolved type is void or the expression belongs to a category of non-value-producing nodes.

Implementation:
```csharp
case Block block:
    for (int i = 0; i < block.Nodes.Count; i++) {
        Emit(block.Nodes[i], ...);
        if (i < block.Nodes.Count - 1 && EmitsValue(block.Nodes[i], analysis))
            code.Add((byte)OpCode.Pop);
    }
    return;
```

Add `EmitsValue(Node, AnalysisResult?)` helper that returns false for: `IfStatement` without else, `ThrowStatement`, `WhileLoop`/`DoWhileLoop`/`ForLoop` (as statements), `TypeDefinitionNode`, `LabelDeclaration` (no value), `TypeCast`/`TypeIs`/`TypeAs`/`Parameter`/`ParameterReference` (no-ops).

**5b. Multi-slot result extraction** (medium)

Problem: `Vm.Execute` always calls `PopInt()` for the final value. Multi-slot types (double = 2 slots, long = 2 slots) produce garbage because only 4 of 8 bytes are read. Heap handles for objects/strings are returned as raw `int` instead of the heap object.

Fix: Store the expression result type in `Bytecode` metadata, or change the approach to always box the result via heap allocation.

Option A (preferred): Add `ResultType` field to `Bytecode` (or a `FunctionEntry` refinement). At lowering time, record the resolved CLR type of the root expression. At result extraction, use this type to determine the pop strategy:
- `null`/`typeof(void)` → `Void`
- `typeof(int)`/`typeof(bool)` etc. → `PopInt()`
- `typeof(double)`/`typeof(float)` → `Pop<double>()`
- `typeof(long)`/`typeof(ulong)` → `Pop<long>()`
- Other reference types → `PopInt()` (handle), then `heap.Get(handle)` to get the actual object

Option B: Always box non-int results via heap allocation at the end of lowering. Emit a "Box result" sequence that stores the top-of-stack onto the heap and pushes the handle. Then `PopInt()` always returns a heap handle, and the extraction resolves it.

Implementation (Option A):
1. Change `Bytecode` to carry `Type? ResultType`
2. In `Lower()`, set `resultType = analysis.GetResolvedType(root)?.GetRuntimeType()`
3. In `Vm.Execute`, extract final value using result type:
   ```csharp
   var resultType = prog.ResultType;
   if (state.Stack.IsEmpty || resultType == typeof(void))
       finalResult = InterpreterResult.Void;
   else if (resultType == typeof(double) || resultType == typeof(float))
       finalResult = FromValue(state.Stack.Pop<double>());
   else if (resultType == typeof(long) || resultType == typeof(ulong))
       finalResult = FromValue(state.Stack.Pop<long>());
   else if (resultType is not null && !resultType.IsPrimitive)
       finalResult = FromValue(state.Heap.Get(state.Stack.PopInt()));
   else
       finalResult = FromValue(state.Stack.PopInt());
   ```

**5c. Lambda/closures** (larger feature)

Problem: Lambda expressions capture local variables ("upvalues"). When a lambda escapes its declaring scope, captured variables must be hoisted to a heap-allocated closure environment.

Approach:
1. Analysis phase: `LambdaCaptureAnalysis` identifies captured variables per lambda + their lifetime (also use existing analysis metadata like `Mutability`).
2. Lowering phase: For each lambda that captures variables:
   a. Create a closure environment type (struct or class-like layout) with slots for each captured variable.
   b. Rewrite captured variable references to access the closure environment instead of the local frame slot.
   c. Allocate the closure environment when the lambda is created (at the point of the `Lambda` node evaluation).
   d. Compile lambda body as a separate FunctionEntry that receives the environment as an implicit parameter.
3. The closure's MethodDefinitionNode is handled by the existing `DiscoverFunctions` + `Call` path (already emits `LoadArg` for implicit first arg = environment reference).

Defer: Non-escaping lambdas (inlined directly without environment) — can be optimized later.

Implementation order:
- Add closure environment type generation in lowering
- Add `ClosureEnvironment` opcode or reuse `New` to allocate
- Rewrite captured Variable/Assignment to access environment
- Wire lambda body via `DiscoverFunctions` with env param
- Add `LoadUpvalue`/`StoreUpvalue` opcodes for fast env slot access

**5d. Await** (largest, deferred until cross-entity policy tests demand it)

Problem: `Await` requires an async state machine. The expression `await task` needs to save the current continuation, yield, and resume when the task completes.

Approach (matches existing tree-walker behavior):
1. Lower `Await` to a `Int` + suspend + resume sequence, extracting the result synchronously via `GetAwaiter().GetResult()`.
2. When cross-entity policy tests require real async, introduce state machine frames with saved PC + local slots + eval stack snapshot.

This is already partially implemented: the tree-walker's `EvaluateAwait` extracts results via `GetAwaiter().GetResult()`. The VM's current `Await` handling emits `Int 0` (suspend). Wire this to the actual `InterpreterResult.Suspended` flow for initial parity.

Full async state machine implementation:
- New opcodes: `SaveState`, `ResumeState` (saves/restores PC + local slots)
- Frame header extension: `AsyncFlag`, `SavedStatePC`
- The host (evaluator) checks for `SuspendedExecution` after `Int` and resumes later
- On resume, jumps to saved PC with restored locals

### Phase 6: Suspend/Resume Integration + Tag Removal

Same as original Phase 6 — full neurosymbolic features, wire VM into `SuspendedExecution`.

### Phase 7: Test Parity, Fuzz, Cutover

Same as original Phase 7 — VM as primary path, tree-walker as fallback.

### Phase 8: Polish, Documentation, Cleanup

Same as original Phase 8.

## Original Phase Plan (kept for reference)

Phases 0–3 are complete. See below for the original plan details.

We follow the project's established pattern (see `docs/plans/v2-to-v3/` workstreams and `master-roadmap.md`): small verifiable increments, working code + green tests at each step, "tags for now then remove", explicit verification of suspend/insight/stack-ref/CLR-ref scenarios, and the 1200-cycle soak as a gate.

### Phase 0: Foundations, Audit & Skeleton (0 behavior change, 1-2 weeks)

**Goals**: Understand current invariants, establish skeleton, no risk to existing tests.

**Tasks**:
- Full audit of paths that must be preserved with identical observables:
  - Tree-walker dispatch, `EvaluateBlock` (elision + resumption), signal handling (`HandleSignal`, loop duplication), `AnalyzeForEvaluation` (full pipeline order + `BindIncomingParameterTypes` + policy enforcement), `SuspendedExecution` + `ExecutionInsightAnalyzer`, lifecycle (Evaluate/Resume/Dispose, already-evaluating guards), CLR interop (`Read/WriteResolvedMemberValue`, reflection fallbacks, `ClrType*` delegates).
  - Test surface: 1200-cycle soak (`SuspendResumeReanalyze_Soak_1200Cycles_CompletesDeterministically` with re-analysis), fuzz/cross-engine (`GrammarDrivenFuzz`, `CrossEngineInvariant`), lifecycle/breakpoints, analysis policy, execution semantics (short-circuit, suspend-in-And/Or, etc.), state/assignment (including member/index writeback), invariants/stress.
  - Existing prototypes: `DataStack` in `Testing.cs` (byte `Push(Span<byte>)` + generic `Push<T>`/`Pop<T>` via `MemoryMarshal.AsBytes` — this is the direct inspiration for the new stack).
- Create skeleton under `Poly/Interpretation/VirtualMachine/` (execution tier placement under `Interpretation/`, parallel to `TreeWalking/`; this file already lives under `docs/plans/`):
  - Directory: `Poly/Interpretation/VirtualMachine/`
  - Initial files (namespace `Poly.Interpretation.VirtualMachine`; all internal during development):
    - `RiscInstruction.cs` — opcode enum + minimal payload record (supports i64 immediates for signed handles/sizes).
    - `RiscProgram.cs` — `List<RiscInstruction>`, `Dictionary<int, NodeId> instrToNode` source map (every instr → original NodeId), const pool, external call signature table.
    - `RiscValueStack.cs` — byte-backed stack (inspired directly by `DataStack` prototype in `Testing.cs`); 8-byte aligned slots, `ReserveBytes(size) → Span<byte>` for direct destination writes (no temp stackallocs), `Push64`/`Pop64`/`Peek64(offset)`, growth with base adjustment + live negative handle patching, `AsRawBytes()`.
    - `RiscHeap.cs` — thin wrapper over `List<object?>` (positive indices are heap handles).
    - `RiscFrameHeader.cs` (or documented layout in RiscState) — on-stack header written at each frame base: `retPC`, `savedPrevBase`, `callerPerspectiveSP` (or equivalent for creation-time absolute computation).
    - `RiscState.cs` — VM state (stack bytes + sp + optional separate tags, `frameBases: List<int>`, heap, pc, program, AnalysisResult, suspend/breakpoint state). Frames are pure segments of the single operand stack.
    - `RiscVm.cs` — the tiny dispatch loop (target: dramatically smaller than `ExecuteCurrentNode`).
    - `RiscLowering.cs` — analysis → RISC IR (created in Phase 2).
  - Internal routing flag (e.g. in `InterpreterOptions` or a new `VirtualMachineOptions`) to select the RISC path (off by default during development).
  - Build note: `Poly/Interpretation/VirtualMachine/*.cs` is picked up automatically by `Poly/Poly.csproj` (no new project references). Tests continue to live in `Poly.Tests/`.
- Update `docs/plans/v2-to-v3/tree-walking-interpreter-design.md` and `docs/plans/v2-to-v3/workstreams/ws8-analysis-unification-and-lowering.md` with pointers to this plan.
- Add a simple "Risc smoke" test harness (using the existing test infrastructure in Poly.Tests) that exercises the new VirtualMachine path (initially behind the routing flag) and can be expanded later. The harness itself stays in the test project; it will import types from `Poly.Interpretation.VirtualMachine`.
- **Verification gate**: `dotnet build Poly/Poly.csproj`; full test run (still 1200/1200 on tree-walker); no observable changes; audit notes committed.

**Deliverable**: Audit notes + skeleton under `Poly/Interpretation/VirtualMachine/` + updated docs (including this plan reflecting the placement). Zero risk.

### Phase 1: IR Definition + Source Maps (1-2 weeks)

**Goals**: Make the "program" first-class and node-faithful.

**Tasks**:
- Finalize minimal RISC opcode set (informed by current tree-walker + what analysis can prune):
  - Data movement: `LOAD_CONST`, `LOAD_VAR`/`STORE_VAR` (and param equivalents), `DUP`/`POP`.
  - Arithmetic/logic/comparison (can start untyped with size or typed; unify with size-on-stack later).
  - Control: `JUMP`, `JUMP_IF_FALSE`.
  - Calls: `CALL` (internal target PC + arg count), `CALL_EXTERNAL` (resolved site index or heap delegate handle + arg byte count + hasReturn + signature metadata).
  - Bulk + indirection: `LOAD_VALUE`/`STORE_VALUE` (pops size then signed handle; sign selects domain; direct copy of `size` bytes).
  - Suspension: `SUSPEND`.
  - Frame/segment management can be implicit in `CALL`/`RETURN` (or explicit `ALLOC_FRAME` if helpful).
- Define supporting types (all under `Poly/Interpretation/VirtualMachine/`):
  - `RiscInstruction` (Op + payload; support signed handles as i64 immediates or stack values, sizes as i64).
  - `RiscProgram` (instructions + `Dictionary<int, NodeId> instrToNode` + const pool + external signature table).
  - Frame header layout (written on-stack at `frameBase`): `retPC` (8), `savedPrevBase` (8), `callerPerspectiveSP` (8) or equivalent (used only at handle creation time to compute negated absolute offsets; handles provided to callees are self-contained negated absolutes).
- Add resolution helpers (internal, in `RiscState` or a small utility in the VirtualMachine module):
  - `ResolveStackHandle(handle)` → real byte offset (`real = -handle` for negated absolute form). The issuing frame's base/entry point is used **only** when the handle is first created (to turn a relative delta into an absolute byte offset, then `handle = -abs`). At use/dereference time (including for handles passed as params to callees), no live PC, live sp, or "current frame perspective" is consulted — the negated absolute is self-contained.
  - Growth logic patches any live negative handle values that reside on the stack.
- **Verification**: IR construction + source-map round-tripping tests (no VM execution yet; `RiscProgram` + `RiscInstruction` under VirtualMachine). Ensure every instruction can map back to a `NodeId` via `instrToNode`.

**Deliverable**: IR types + basic tests. The IR must be faithful enough that a future tree-walker on the IR would produce the same observables as the AST tree-walker.

### Phase 2: Lowering Pass (analysis → linear RISC IR) (2-3 weeks)

**Goals**: The first consumer of the mature analysis surface; produce a lowered form that bakes in all optimizations.

**Tasks**:
- New `RiscLowering.cs` (or `RiscLoweringPass`) under `Poly/Interpretation/VirtualMachine/`.
- Input: root `Node` + full `AnalysisResult` (the same pipeline the tree-walker uses today: `UseTypeResolver` + `UseMemberResolver` + `UseVariableScopeValidator` + `UseConstantFolding` + `UseSideEffectAnalysis` + `UseControlFlowAnalysis` + param binding).
- Output: `RiscProgram`.
- Lowering rules (all decisions come from analysis metadata — no duplication):
  - Elision: omit instructions for nodes where `CanElide` is true (except last-in-block or value-producing positions). Use the size-on-stack form for variable payloads.
  - Replacements: emit the folded constant (from `ConstantValueMetadata`) instead of the original subtree.
  - Control flow: direct jumps for pruned branches; richer infinite-loop info already computed.
  - Calls: internal `CALL` with PC offset; external `CALL_EXTERNAL` using resolved `ITypeMember` delegate/site (preferred) or heap delegate handle. Attach signature metadata for arg/return sizes and by-ref classification.
  - Stack references: when a location in an ancestor frame is exposed (by-ref param, etc.), emit a negative handle as the negated absolute byte offset (computed using the issuing frame's base at creation time: `abs = frame_base + rel_delta; handle = -abs`). For "heap-ref cells" (a stack slot holding a positive heap index), the passed handle is the negated absolute offset to the *cell*; the cell itself holds the positive heap index. Resolution at use: `real = -handle` (no live PC/sp).
  - Size-on-stack for `LOAD_VALUE`/`STORE_VALUE` (enables variable structs + indirect through stack handles + uniformity).
  - Unified addressing via sign of handle (positive = heap index, negative = negated absolute stack offset).
  - Emit on-stack frame headers on `CALL` (including the caller's perspective for later stack-handle resolution in the callee).
  - Attach `NodeId` to every emitted instruction.
- Start narrow (expressions, blocks, simple control) then add calls, externals, stack refs.
- The lowering (`RiscLowering.cs` under VirtualMachine) must be **faithful** — any observable that the tree-walker produces from the analyzed AST must be reproducible from the IR.
- **Verification**: Lower → execute (once Phase 3 exists) must match tree-walker observables for a growing set of tests. Add IR pretty-printer + round-trip tests. All lowering logic lives in the `Poly/Interpretation/VirtualMachine/` module.

**Deliverable**: Working lowering for a useful subset. This is the first real consumer of the analysis unification work.

### Phase 3: Core VM + Internal Execution (make the engine run) (2-3 weeks)

**Goals**: A working, simple stack VM for the IR (internal calls first).

**Tasks**:
- Evolve or introduce the byte stack (directly inspired by `DataStack` in `Testing.cs`; implementation file `RiscValueStack.cs` under `Poly/Interpretation/VirtualMachine/`):
  - `RiscValueStack`: `byte[] data` (or `Memory<byte>` from pool), `sp`, `ReserveSlot()` / `ReserveBytes(size)` returning `Span<byte>` for direct writes (the key "no temp stackallocs" rule), `Push64`/`Pop64`/`Peek64(offset)`, growth (copy + adjust bases + patch live negative handle *values* on the stack), `AsRawBytes()`, `Dispose`.
  - 8-byte alignment enforced for slots.
  - Separate `tags` array (parallel `StackTag[]` or `byte[]`; same logical length as sp/8; write on reserve/push; used by insight + for growth patching + early safety asserts during the "tags for now" period).
- Heap: thin `RiscHeap` wrapper over `List<object?>` (indices are the handles; file `RiscHeap.cs`).
- `RiscState` (evolve `InterpreterState`; file `RiscState.cs`): `stackBytes` + `sp` + `tags`, `frameBases: List<int>`, `heap`, `code`/`program`, `pc`, `AnalysisResult`, `BreakpointSkipNodeId`, suspend state, etc. Frames are **not** a separate concept — they are fixed segments of the single operand stack (base offsets recorded in `frameBases`; headers written directly into the stack bytes at each base).
- Tiny dispatch loop in `RiscVm.cs` (target: dramatically smaller than current `ExecuteCurrentNode`):
  - `LOAD_CONST`, arithmetic, etc.: direct writes to reserved slots.
  - `LOAD_VALUE`/`STORE_VALUE`: pop size + signed h; resolve via sign (positive → heap index in RiscHeap; negative → `real = -handle` absolute byte offset into stack bytes); direct `Span` copy of exactly `size` bytes into the reserved padded-to-8 destination; set tags if present.
  - `CALL` (internal): write on-stack frame header at `newBase`, `frameBases.Add(newBase)`, `sp += localBytes` (or header + declared locals), `pc = target`.
  - `RETURN`: read header from current base, compute pre-arg SP boundary, truncate `sp`, pop base, `pc = retPC`. Leaves return value per calling convention (or void yields no value).
  - `SUSPEND`: capture `SuspendedExecution` (raw byte buffer + heap + `frameBases` + `pc` + source map for `AtNode` via `instrToNode`), run insight analyzers, yield.
  - `CALL_EXTERNAL`: see Phase 5 (CLR delegate resolution + heap-ref cell marshaling for ref/out).
- Basic internal execution first (expressions → blocks → control flow → simple calls). Use size + signed handles from the beginning.
- Frame segments + perspective resolution from day 1.
- **Verification**: Build green. Unit tests for VM on hand-crafted IR (tests live in Poly.Tests, exercising `Poly.Interpretation.VirtualMachine.RiscVm` etc.). Port simple expression/block/control tests from the tree-walker path. Run the 1200-cycle soak (still on tree-walker) + start exercising new path on a subset.

**Deliverable**: Working RISC execution for a useful subset of the language. Tree-walker path still primary for full tests.

### Phase 4: Stack References, By-Ref Semantics & Heap-Ref Cells (2 weeks)

**Goals**: The "exciting" cross-frame + mutable-ref scenarios.

**Tasks**:
- Full support for negative stack handles (creation + resolution logic lives primarily in lowering + `RiscState` / `RiscVm` helpers under VirtualMachine):
  - Creation (at lowering time or on first issuance in the VM when exposing an ancestor location): compute absolute byte offset using the **issuing frame's base/entry point** (`abs = frameBase + relativeDeltaWithinFrame`), then `handle = -abs` (the value pushed/stored is the negated absolute).
  - Resolution (LOAD_VALUE, STORE_VALUE, indirect, CALL_EXTERNAL marshaling for ref/out): `real = -handle` (direct absolute byte offset into the stack byte array). At use/dereference time for any provided stack reference (including those passed as call arguments), the VM **must not** consult the live PC, live sp, or the *current* frame's base — the negated absolute value is self-contained and portable.
  - Growth of the underlying byte buffer: after reallocation/copy, patch any live negative handle *values* that exist on the stack (using the tag array to locate candidate slots quickly) as well as adjusting all `frameBases`.
  - Validation (debug builds or IR-level): a stack handle must resolve inside a live ancestor segment (walk `frameBases`).
- `LOAD_INDIRECT` / `STORE_INDIRECT` (or unified into the sized load/store) for dereferencing through stack handles. Direct `Span` copy from/to the resolved location.
- Heap-ref cells (core motivating scenario for stack refs): a stack slot (in an ancestor segment) holding a positive heap index ("reference to the heap"). A stack handle (negated absolute) to the *cell itself* allows a callee (or CLR delegate) to:
  - Read: `LOAD_VALUE` (size=8) via the negative handle (`real = -handle` into stack bytes) → yields the current positive heap index.
  - Mutate: push new positive heap index, size=8, push the original negative stack handle to the cell, `STORE_VALUE` → the ancestor's cell is updated; the caller observes the new heap object after RETURN.
  This is exactly the "stack allocation of a reference to the heap, passed by reference through a call stack, and modified to a different reference to the heap" model. The same cell handle can be turned into a real CLR `ref T` for typed delegates via a temp + sync-back write using the original handle (see Phase 5).
- Passing stack handles as args through `CALL` (they live in the callee's segment as params; the handle value is the negated absolute, resolved as `real = -handle` using the absolute offset; no live perspective needed at use if stored as absolute).
- Truncation on `RETURN` using saved bases (invalidates handles into the returning segment).
- Growth: adjust `frameBases`; patch live negative handle *values* (the negated absolutes) on the stack (scan using tags).
- Validation (debug or IR-enforced): a stack handle (negated absolute) must resolve to a byte offset within a live ancestor segment (via frameBases).
- **Verification**: New + ported tests for cross-frame access, heap-ref cell mutation passed by ref (read + reassign to different heap object), suspend during such mutation + resume + re-analysis (the mutation must survive and be visible). Must match tree-walker observables exactly. Include the 1200-cycle soak exercising these paths.

**Deliverable**: Working by-ref stack references, including the exact "stack allocation of a reference to the heap, passed by reference through a call stack, and modified to a different reference to the heap" scenario.

### Phase 5: CLR Interop (`CALL_EXTERNAL`) + Ref/Out Marshaling (2-3 weeks)

**Goals**: Support calling CLR delegates and type methods, with proper handling of `ref`/`out` (especially the stack-handle-to-heap-ref-cell case).

**Tasks**:
- Extend lowering to emit `CALL_EXTERNAL` for calls that target CLR (using resolved `ITypeMember` / `Clr*` from analysis, or a heap delegate handle for truly dynamic cases).
- Instruction carries: resolved site index (preferred) or heap delegate handle, arg byte count (or sizes on stack), hasReturn, signature metadata (for sizes, by-ref classification, value kinds).
- VM handling for `CALL_EXTERNAL`:
  - Resolve target (prefer pre-resolved typed delegate from analysis/`ITypeMember`; fallback to heap delegate + `DynamicInvoke`).
  - Marshal args from the current frame's stack segment (direct spans; primitives unboxed; heap refs resolved to objects; stack handles turned into CLR `ref`/`out`).
  - Special marshaling for by-ref stack-handle to heap-ref cell (the key scenario we explored; implemented in `RiscVm.cs` `CALL_EXTERNAL` handling + helpers in the VirtualMachine module):
    ```pseudo
    // In CALL_EXTERNAL arg prep for a by-ref that is a stack handle (negated absolute) to a heap-ref cell
    long stackHandle = ...; // the arg value itself is the negated absolute (e.g. -absOffset), or size + handle on stack per the size-on-stack convention
    long realCellOffset = -stackHandle;   // direct: self-contained negated absolute; NO live pc/sp/current base used at deref for provided handles
    long currentHeapH = ReadI64(stackBytes, realCellOffset);
    T currentObj = (T)heap[currentHeapH];
    T temp = currentObj;
    theTypedRefDelegate(ref temp);   // resolved typed delegate from ITypeMember / analysis at lowering time (preferred path)
    if (!ReferenceEquals(temp, currentObj)) {
        long newH = HeapAllocate(s, temp);
        // write the new heap index back into the original cell using the provided stack handle + STORE_VALUE
        // (size pushed, then handle, then STORE_VALUE will pop and write exactly size bytes to real = -handle)
        PushI64(newH);
        PushI64(8);
        PushI64(stackHandle);  // the original negative (negated abs) handle
        STORE_VALUE;
    }
    ```
    The original stack handle (negated absolute) is what the caller provided; the VM uses it directly for the write-back via the normal `STORE_VALUE` path. This keeps the "simulate a stack allocation of a reference to the heap, passed by reference, then mutated to a different reference" semantics exact.
  - For `out`: similar (start with default, always write back).
  - For value-type cells or other by-ref cases: analogous temp + sync.
  - Pinning only when truly required (e.g. pointers); prefer managed `ref` temps for reference types.
  - Return values written back via `ReserveBytes` + direct write (or heap allocate + push handle).
  - Exceptions → `InterpreterSignal.Throw` (uniform with internal).
- Lowering attaches the necessary metadata (resolved delegate, signature info, by-ref classification for stack-handle cases).
- **Verification**: Extend existing CLR interop tests. Add dedicated scenarios for stack-handle-to-heap-ref-cell passed as `ref`/`out` to CLR methods/delegates (mutation visible post-call, survives suspend + re-analysis + resume). Must match tree-walker / generator observables. Include in the 1200-cycle soak.

**Deliverable**: Working CLR interop, including the full stack-ref + CLR `ref`/`out` mutation scenario.

### Phase 6: Suspend/Resume/Insight Integration + Tag Removal (2 weeks)

**Goals**: Full neurosymbolic features; clean up temporary scaffolding.

**Tasks**:
- Wire the RISC state (`RiscState` in VirtualMachine) into `InterpreterState` / `SuspendedExecution` / `Suspend` / `Resume` (integration touches both the new module and the existing suspend/insight machinery):
  - Capture/restore: raw byte buffer (or pooled owner), `sp`, heap, `frameBases`, `pc`, `AnalysisResult`, `BreakpointSkipNodeId`, program (or a stable ID), etc. Negated-absolute handles are self-contained; frame headers carry only what is needed for RETURN and creation-time absolute computation.
  - `AtNode` resolution via `instrToNode[pc]` (source map in `RiscProgram`); for outer frames use the CALL instruction's mapped Node via its saved return PC.
  - Run registered insight + live-state analyzers (`ExecutionInsightAnalyzer` etc.) on suspend (they see the full state + source-mapped original Nodes).
- Re-analysis on `Resume(analysisResult)`: re-run the analyzer on the original AST (or a checkpoint), re-lower (via `RiscLowering` under VirtualMachine) to (potentially better) IR, continue from a safe PC (or restart the activation if needed). The RISC path must support this with identical observables to the tree-walker path.
- Node fidelity everywhere: every `RiscInstruction` + every on-stack frame header entry + every value that originated from a Node maps back to a `NodeId` via the program source map.
- Remove the separate tag array once:
  - The IR is fully typed (lowering emits correct sizes/kinds).
  - All insight/live-state paths have been updated to derive what they need from IR + PC + heap + explicit stack maps if required.
  - Growth patching no longer depends on scanning tags.
- Update `ExecutionInsightAnalyzer` and any `INodeAnalyzer` / `ILiveStateAnalyzer` consumers to work with the RISC representation (or a view over it) while preserving exact output for existing tests.
- **Verification**: Full lifecycle/breakpoint/suspend tests (including during CLR calls and stack-ref mutation). The 1200-cycle soak + fuzz must still produce identical observables (suspend count, `AtNode`, final values, insight output, cross-engine equivalence). Re-analysis during suspend must work and produce correct (possibly improved) behavior on resume. All new VM/suspend integration code is under `Poly/Interpretation/VirtualMachine/`.

**Deliverable**: Production-quality suspend/insight on the RISC path; tags removed.

### Phase 7: Test Parity, Fuzz, Deprecation & Cutover (2-3 weeks)

**Goals**: The RISC path is the primary; tree-walker is fallback or removed.

**Tasks**:
- Port or add tests that exercise the RISC path (expressions, control, internal calls, stack refs + heap-ref cells, CLR interop including ref/out, suspend during mutation + re-analysis).
- Run the full fuzz/cross-engine suite and the 1200-cycle soak **against the RISC path** (must be green and produce identical observables to the tree-walker path).
- Add regression tests for growth while stack refs are live, multi-level stack refs, suspend during external CLR ref mutation, etc.
- Once parity is solid: make the RISC path the default (behind the internal flag or by routing in `TreeWalkingInterpreter` / a new clean entry point). The tree-walker can stay as an opt-in fallback or be deprecated.
- Measure: allocations (byte stack + baked analysis should win), hot-path speed, suspend overhead.
- **Verification**: `dotnet build`; full test run (1200+ on RISC path); soak + fuzz green and equivalent; no regressions in any existing behavior or the new scenarios.

**Deliverable**: RISC path is the reference interpreter for practical use. All tests pass on it.

### Phase 8: Polish, Documentation, Performance & Cleanup (1-2 weeks)

**Tasks**:
- Update documentation:
  - `docs/plans/v2-to-v3/tree-walking-interpreter-design.md` (reflect the RISC path as primary, how it satisfies the original design tenets; note the execution implementation moved to `Interpretation/VirtualMachine` while tree-walker remains the prior reference).
  - This plan (mark completed sections).
  - `docs/decisions/` if any architectural decision needs formalizing (e.g. "frames as stack segments", "signed handles as negated absolutes", "tags as temporary scaffolding", "RISC IR as post-analysis lowering target").
  - Any new "Risc IR" or "stack VM" overview (may live under `docs/` or as comments in the VirtualMachine module).
- Remove remaining scaffolding (dual paths, tags, unused tree-walker code if safe).
- Performance: profile, address hot spots (only after working + tests are green). Consider better growth strategy, small-size fast paths, etc.
- Conformance: document that the RISC IR + VM is the canonical semantics (other backends must match it).
- **Verification**: Clean build, full tests, updated docs, any new decision records.

**Deliverable**: Production-ready RISC interpreter path, clean codebase, complete docs.

## Cross-Cutting Concerns & Verification Strategy

**Test & Observability Gates (mandatory at end of every phase that touches execution)**
- `dotnet build Poly/Poly.csproj` (0 errors).
- Full `dotnet run --project Poly.Tests/Poly.Tests.csproj` → "1200 succeeded, 0 failed".
- The 1200-cycle `SuspendResumeReanalyze_Soak_1200Cycles_CompletesDeterministically` (with periodic re-analysis) must complete with correct final value and exact suspend count.
- Cross-engine fuzz/invariants (`GrammarDrivenFuzz`, `CrossEngineInvariant`, arithmetic metamorphic, etc.) must match the tree-walker / Linq / C# paths.
- Lifecycle/breakpoint/suspend tests (Evaluate/Resume guards, already-evaluating, breakpoint on pure, suspend-in-And/Or, etc.).
- Analysis policy tests (precomputed vs settings, resume with refined analysis).
- New targeted tests for each major feature (stack refs, heap-ref cells, CLR ref/out through them, suspend during mutation, growth with live refs, etc.).
- Insight analyzer output must still be correct (mixed types, call depth, Create flags, etc.).

**Specific Scenario Coverage (add explicit tests)**
- Stack allocation of a heap-ref cell → pass stack handle (negated absolute offset to the cell) by ref through CALL → callee reads current ref via `LOAD_VALUE` (size + negative handle, real = -handle) → mutates to different heap ref via `STORE_VALUE` → caller sees mutation after RETURN.
- Same scenario but the mutation happens inside a CLR `ref`/`out` call (temp + typed delegate + sync-back).
- Suspend while a stack ref to a heap-ref cell is live and being mutated (by internal code or CLR); resume + re-analysis; mutation is visible and correct.
- Growth of the byte buffer while stack refs (including to heap-ref cells) are live.
- Multi-level nesting (grand-callee mutates a cell allocated in grandparent via chained stack handles with negated absolute offsets).
- Re-analysis on resume produces a better lowering (more elision) and the new IR still produces identical observables.

**Risks & Mitigations**
- Test drift / observable changes: Mandatory parity gates at every execution-touching phase. Keep tree-walker path live until RISC soak/fuzz are green and equivalent.
- Complexity in lowering or ref marshaling: "Make it work then fix" — start with resolved cases + simple by-ref cells; add dynamic later. First consumers = existing CLR tests + the new stack-ref + CLR ref/out scenarios.
- Growth + live stack refs: Explicit patching + tests that force growth during suspend with active refs.
- Fidelity for insight/suspend/re-analysis: Source map + frame headers from day 1; test with real `ExecutionInsightAnalyzer` + registered insight analyzers.
- Performance during transition: Measure early; the byte stack + baked analysis should be a net win.
- AGENTS.md alignment: Every phase produces working, tested code before the next abstraction. New types (IR, VM) only justified by first consumers (the analysis surface + the suspend/insight tests + the CLR interop scenarios we explored).

**Milestones / Exit Criteria**
- End of Phase 3: RISC path can execute a useful subset (expressions, blocks, control, simple internal calls) and pass a growing subset of the existing tests.
- End of Phase 5: Full stack-ref + heap-ref cell + CLR `ref`/`out` mutation scenario works and is covered by tests.
- End of Phase 7: RISC path is the default for the reference interpreter; all 1200+ tests + soak + fuzz are green and produce identical observables to the tree-walker path. Tree-walker can be deprecated or kept as opt-in.
- End of Phase 8: Clean codebase, complete docs, RISC path is the canonical semantics.

**Next Immediate Steps (after plan approval)**
1. Phase 0 audit + skeleton (create `docs/plans/risc-ir-stack-vm-implementation-plan.md` is already done; now create the source skeleton under `Poly/Interpretation/VirtualMachine/` as detailed in Phase 0).
2. Start Phase 1 (IR definition + source maps) in parallel with more audit reading.
3. Use the existing `DataStack` in `Testing.cs` as the direct prototype / starting point for `RiscValueStack.cs`.
4. Confirm no additional decision record is required for placement (this plan update records the explicit "under VirtualMachine" direction).

This plan is deliberately incremental, test-driven, and aligned with the project's established patterns (see the v2-to-v3 workstreams). It delivers the exciting capabilities we designed (stack refs for by-ref heap-ref cells, clean CLR interop for ref/out, simple VM, full neurosymbolic suspend/insight) while keeping risk low and staying true to the core engineering principles.

Let's build it! If any phase needs adjustment or you want to claim the first micro-task (e.g. "create the VirtualMachine/ skeleton under Poly/Interpretation/VirtualMachine + basic RISC IR types"), just say the word. This is going to be great.