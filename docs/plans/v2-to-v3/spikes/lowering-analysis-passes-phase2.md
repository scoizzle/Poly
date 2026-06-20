# Lowering Analysis Passes — Phase 2

> **Status:** Design / proposal  
> **Prerequisite:** `lowering-as-analysis-passes.md` (Phase 1: StackDepthAnalysis,
> LabelAssignment, UopGeneration, assembly step)

Phase 1 moved depth computation, label assignment, and µop generation into
analysis passes. The assembly step walks the AST in execution order, reads
per-node µop fragments (`LoweredUopMetadata`), flattens them into a single
instruction list, and computes `ConsumedFromPcs` via backward scan.

Since the assembly step already walks the AST node by node, **everything
previously planned as separate analysis passes in Phase 2 is better done
inline during assembly** — phi, source ranges, max locals, call sites, and
the constant pool. The assembly step has direct access to the node tree,
analysis metadata, and the eval-stack ring buffer, so separate passes would
add overhead without benefit.

What follows is the breakdown of each concern and how it fits into the
assembly walk.

---

## Ranking

| # | Proposal | Impact | Complexity | Files changed | New types |
|---|----------|--------|------------|---------------|-----------|
| 1 | Source Range Mapping | **High** — required for debugger, breakpoints, stack traces | **Low** — pure assembly-step bookkeeping, no new pass | 2 | None |
| 2 | Max Locals Depth | **Medium** — saves 32-slot waste, enables variable-count-dependent opts | **Low** — one field copy in UopGeneration | 2 | `MaxLocalsMetadata` |
| 3 | Phi Analysis | **High** — correctness for control-flow merge points | **Medium** — requires comparing exit stacks of two µop fragments | 3 | `PhiMetadata` |
| 4 | Call Site Enumeration | **Medium** — unblocks constructor/indirect calls | **Medium** — needs to track which CallExternal sites exist | 2 | `CallSiteMetadata` |
| 5 | Constant Pool | **Low** — cleanup, no new capability | **Low** — aggregate during assembly | 2 | None |
| 6 | Loop Invariant Hoist | **Medium** — optimization, measurable speedup for tight loops | **High** — requires StoreSlot analysis within loop body, hoisting in assembly | 3 | `InvariantHoistMetadata` |
| 7 | Tail Position Detection | **Low** — optimization, niche | **Low** — one check per ReturnOp | 2 | `TailCallMetadata` |

---

## 1. Source Range Mapping — debugger support

**Impact: High.** Without source mappings, the debugger can't map µop PCs to
source lines. Breakpoints in `VmDebugger` need this to translate source
locations to µop indices. Stack traces need it to display line numbers.

**Complexity: Low.** No new pass needed. Pure assembly-step bookkeeping.

**Current state:** `LoweringResult.SourceRanges` is `new Dictionary<NodeId, SourceRange>()`
— always empty. Every µop carries `SourceNodeId` but no one populates the map.

**Approach:** During the assembly AST walk, maintain a running mapping of
`NodeId → (firstUopIndex, lastUopIndex)`. When the walk enters a node,
record the current µop count as `first`. When it exits the node (or returns
from its Visit), record `last = currentCount - 1`. After the walk, produce
`IReadOnlyDictionary<NodeId, SourceRange>`.

**Where:** The assembly step in `Lowering.Lower`. No new pass file.

**Files:**
- `Poly/Interpretation/Vm/Lowering.cs` — assembly walk populates the map
- `Poly/Interpretation/Vm/LoweringResult.cs` — `SourceRanges` already exists

**Metadata needed:** None — `SourceNodeId` is already on every `Instruction`.
The `AnalysisResult` carries `SourceRange` per node from parsing (check:
`AnalysisResult` has no direct `GetSourceRange` method — but nodes may carry
it via `Node` properties or token data if the parser records it).

**Open question:** Does `SourceRange` exist in the AST today? If not, this
is blocked on parser source-location tracking. Verify before implementing.

---

## 2. Max Locals Depth — eliminate the `maxActiveLocalDepth` parameter

**Impact: Medium.** The current blanket 32-slot allocation wastes memory for
tiny programs (a `Constant(42)` program allocates 256 bytes for a 1-slot
register file). For larger programs, 32 may not be enough (though the Registers
array is built via `NewArrayBounds` which is fixed at compile time).

**Complexity: Low.** UopGeneration already tracks `_locals.Count` per Lambda,
and `_params.Count` gives the parameter count. Max depth =
`_params.Count + _locals.Count` for that Lambda. The global max across all
Lambdas in the program is the `MaxActiveLocalsDepth`.

**Approach:** In `UopGenerationPass`, after processing a Lambda body, emit
`MaxLocalsMetadata` on the Lambda node recording `_params.Count + _locals.Count`.
In the assembly step, find the Lambda with the highest count and set
`LoweringResult.MaxActiveLocalsDepth` to that value.

**Files:**
- `Poly/Interpretation/Analysis/LoweringPrep/UopGenerationPass.cs` — emit metadata
- `Poly/Interpretation/Analysis/LoweringPrep/StackDepthMetadata.cs` or a
  new `LocalsMetadata.cs` file for the record type
- `Poly/Interpretation/Vm/Lowering.cs` — assembly step reads max

**Metadata:**

```csharp
record MaxLocalsDepthMetadata(int Count) : IAnalysisMetadata;
```

**Open questions:** None. The data is already computed; it just needs to be
captured and forwarded.

---

## 3. Phi Analysis — anticipate control-flow merge points

**Impact: High.** The assembly step must correctly merge two control-flow
paths at `IfStatement` and `Conditional` nodes. Without phi, the first µop
after the merge would get wrong `ConsumedFromPcs` — it would see values
from only one branch. The current `ResolveProducers` computes this via the
CFG predecessor graph. We need to replace that with phi metadata.

**Complexity: Medium.** Requires comparing per-node µop fragments' exit
stacks. The challenge is that µop fragments are stored per-node, not as a
flat list. To compute exit stacks, we need to simulate the fragment's effect
— which requires knowing each µop's `PopCount`/`PushCount` and walking the
fragment in order.

**Approach:**

After UopGeneration (which stores `LoweredUopMetadata` on every node),
walk the AST. For each `IfStatement` and `Conditional`:

1. Read the then-branch's µop fragment (list of instructions).
2. Walk the fragment, maintaining a conceptual eval stack of µop PCs (just
   like the assembly step's backward scan, but on the fragment in isolation).
3. The exit stack of the then-branch's fragment is the result of this walk.
4. Do the same for the else-branch's fragment.
5. Compare the two exit stacks depth by depth. For each depth where the
   µop PCs differ, a φ is needed.
6. The first µop of the merge-consumer (the instruction that pops the
   merged values) gets `PhiSourcePcs` and `PhiAltPcs` metadata.

**Metadata:** Store phi info directly on the affected instruction, or on a
new metadata record attached to the `IfStatement`/`Conditional` node.

```csharp
/// <summary>Attached to the merge point's first consuming instruction.</summary>
record PhiMetadata(
    int[] ConsumedFromPcs,
    int[]? PhiSourcePcs,
    int[]? PhiAltPcs
) : IAnalysisMetadata;
```

But this is tricky — the merge consumer might be a µop from a parent node
(the instruction after the `IfStatement` in its enclosing `Block`), not
from the `IfStatement`'s own fragment. So phi metadata may need to be a
field on the `Instruction` record itself, or carried alongside.

**Alternative:** Don't pre-compute phi. Instead, make the assembly step
handle it inline: when visiting the `IfStatement`, save the ring buffer
before the then-branch, walk the then-branch (not storing to result), save
the then-exit, restore the ring, walk the else-branch, compare the two
exits, and insert phi at the next instruction. This avoids a separate pass
entirely — the assembly step becomes phi-aware.

This may be the simpler path: **phi is inherent to assembly, not a separate
pass.** The assembly walk already has the ring buffer and can compare branch
exits at merge points.

**Files (if separate pass):**
- New: `Poly/Interpretation/Analysis/LoweringPrep/PhiAnalysisPass.cs`
- New: metadata record (inline or separate)
- `Poly/Interpretation/Vm/Lowering.cs` — assembly step reads phi metadata

**Files (if in assembly step):**
- `Poly/Interpretation/Vm/Lowering.cs` — assembly walk does phi inline

**Recommendation:** Implement phi in the assembly step, not as a separate
pass. The assembly step already has the ring buffer, which is exactly the
data structure needed for phi. A separate pass would need to reconstruct
the ring buffer from per-node fragments — duplicating the assembly logic.

---

## 4. Call Site Enumeration — replace the empty `CallSites` list

**Impact: Medium.** Currently `CallExternal` is the only indirect call
instruction, and `VmProgram.CallSites` is empty. Populating call sites
enables: (a) proper constructor invocation via CLR, (b) indirect call
resolution, (c) potential devirtualization.

**Complexity: Medium.** The UopGeneration pass already emits `CallExternal`
instructions but doesn't record their descriptors. Need to define a
`CallSiteDescriptor` type and aggregate them.

**Approach:** In `UopGenerationPass`, maintain a `List<CallSiteDescriptor>`
that accumulates call sites as `CallExternal` instructions are emitted.
After the root node is processed, attach the list as metadata.

```csharp
record CallSiteListMetadata(List<CallSiteDescriptor> Sites) : IAnalysisMetadata;
```

The assembly step reads this and attaches to `LoweringResult.CallSites`.

**Files:**
- `Poly/Interpretation/Analysis/LoweringPrep/UopGenerationPass.cs` — accumulate sites
- `Poly/Interpretation/Vm/Lowering.cs` — assembly reads and attaches

**Metadata:**

```csharp
public sealed record CallSiteDescriptor(int FunctionIndex, int ArgumentCount);
```

**Open questions:** What goes in `CallSiteDescriptor`? The current
`VmProgram.CallSites` is `IReadOnlyList<CallSiteDelegate>?`. If
`CallSiteDelegate` is the right type, use it. Otherwise define a simpler
carrier. Check how `CallExternal` is actually used.

---

## 5. Constant Pool — pre-populate heap-allocated values

**Impact: Low.** Cleanliness improvement. Currently `Vm.Execute` imports
constants into the heap at runtime. The assembly step could pre-populate
them instead, making the VM dumber and easier to verify.

**Complexity: Low.** During lowering (current `Lowering.cs`), constants
that need heap allocation are already identified — they're the values
passed to `VmState.Heap.Allocate(constants[i])` in `Vm.Execute`. Move
this identification into the assembly step.

**Approach:** During the assembly walk, when the assembler encounters a
`LoadConst` with a value that can't be a flat integer (closures, strings,
arrays), collect it into a constant pool list. After the walk, attach the
list to `LoweringResult.Constants`.

**Files:**
- `Poly/Interpretation/Vm/Lowering.cs` — assembly step collects constants
- `Poly/Interpretation/Vm/LoweringResult.cs` — `Constants` already exists

**Metadata needed:** None. Pure assembly-step work.

---

## 6. Loop Invariant Hoist

**Impact: Medium.** For tight loops (like Mandelbrot's pixel loop), every
`LoadConst` and invariant `LoadSlot` re-executes on every iteration.
Hoisting them out of the loop eliminates redundant stack operations. The
effect is measurable in loop-heavy code.

**Complexity: High.** Requires:
1. A side-effect analysis of which variables are written to in the loop body.
2. Hoisting logic in the assembly step that emits the invariant µop before
   the loop and wires its `_v{pc}` into the loop body's copy.

The first requirement already partially exists — the `SideEffectAnalysisPass`
computes which nodes have side effects. But it works at the AST level, not
the µop level. Mapping AST side effects to µop variable writes is not
trivial.

**Approach:** The simplest correct approach:
1. In the assembly step, when entering a `WhileLoop`/`ForLoop` node, save
   the current ring buffer state.
2. Walk the loop body's µops (without committing them to the output).
   Track which `StoreSlot` offsets are written to.
3. Any `LoadSlot` whose offset is NOT in the written set is invariant.
   Any `LoadConst` is always invariant.
4. Emit the invariant µops before the loop. Inside the loop body, replace
   the invariant µop with a reference to the hoisted `_v{pc}`.

But this is essentially a mini-optimizer in the assembly step, which adds
significant complexity. The benefit is real but not critical.

**Files:**
- `Poly/Interpretation/Vm/Lowering.cs` — assembly step with hoisting logic
- Tests

**Alternative:** Skip this for now. The assembly step doesn't need it for
correctness. Revisit after the assembly step is stable.

---

## 7. Tail Position Detection

**Impact: Low.** Most test programs don't use tail calls. The Mandelbrot,
NQueens, and Collatz tests don't have tail-call-eligible functions. This
optimization has zero effect on the current test suite.

**Complexity: Low.** During UopGeneration, when emitting a `ReturnOp`,
check if the immediately preceding µop is a `CallExternalDirect`. If so,
and if the `ReturnOp` is at the end of a Lambda body, mark it with
`TailCallMetadata`.

The assembly step would read this metadata, and the compiler would emit a
tail-call sequence (restore frame, jump) instead of a normal call + return.

**Files:**
- `Poly/Interpretation/Analysis/LoweringPrep/UopGenerationPass.cs` — emit metadata
- `Poly/Interpretation/Vm/ProgramCompiler.cs` — consume metadata

**Metadata:**

```csharp
record TailCallMetadata : IAnalysisMetadata;
```

**Open questions:** The VM currently has no tail-call support at the µop
level. Adding it would require either: (a) a new `TailCall` instruction,
or (b) the compiler eliding the `ReturnOp` and lowering the preceding call
to a jump. Both are non-trivial changes to `ProgramCompiler.cs`. This is
genuinely low priority.

---

## Comparison table

| # | Proposal | Impact | Complexity | Correctness req? | New pass? |
|---|----------|--------|------------|-----------------|-----------|
| 1 | Source Range Mapping | High | Low | No | No |
| 2 | Max Locals Depth | Medium | Low | No | No |
| 3 | Phi Analysis (in assembly) | High | Medium | **Yes** | No |
| 4 | Call Site Enumeration | Medium | Medium | No | No |
| 5 | Constant Pool | Low | Low | No | No |
| 6 | Loop Invariant Hoist | Medium | High | No | No |
| 7 | Tail Position Detection | Low | Low | No | No |

**Correctness-required:** Only Phi Analysis. Everything else is either
debugging support, cleanup, or optimization.

---

## Implementation order

**Tier 1 — foundational (before assembly step is complete):**

1. **Phi Analysis (in assembly step)** — the assembly walk must handle merge
   points correctly. Without this, `IfStatement` and `Conditional` produce
   wrong `ConsumedFromPcs`. This is the last gap before `ResolveProducers`
   can be deleted.

2. **Source Range Mapping** — populate as part of the assembly walk. The
   debugger tests need this.

**Tier 2 — cleanup (alongside assembly step):**

3. **Max Locals Depth** — trivially captured during UopGeneration. One field.
4. **Constant Pool** — trivially collected during assembly. One list append.
5. **Call Site Enumeration** — collected during UopGeneration. One list
   append per `CallExternal` instruction.

**Tier 3 — optimization (after assembly step is stable):**

6. **Loop Invariant Hoist** — requires side-effect analysis at the µop
   level. Worth doing but not until the assembly step is verified correct.
7. **Tail Position Detection** — requires new VM instructions or compiler
   changes. Not worth doing until we have programs that actually benefit
   from tail calls.
