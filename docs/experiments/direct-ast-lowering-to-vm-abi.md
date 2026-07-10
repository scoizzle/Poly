> **Note (2026-07-10):** This experiment may reference pre-direct-ABI plans. Those plans live under `docs/plans/archive/interpretation/`. Prefer `DirectVmAbiEmitter` + current decisions for new work.

# Research: Direct AST Lowering to Bespoke VM ABI (Without Primitives as Mandatory Layer)

**Status:** Research spike / exploratory  
**Date:** 2026-07-06  
**Related:** 
- `docs/decisions/2026-07-04-primitives-as-canonical-ir.md`
- `docs/decisions/2026-06-08-vm-as-canonical-semantics.md`
- `docs/decisions/2026-05-31-neurosymbolic-platform-vision.md` (with 2026-07-06 clarification)
- `docs/plans/archive/interpretation/interpretation-system-resolution-plan.md`
- Current `LinqExpressionGenerator` and `ProgramCompiler`

---

**Preamble for Recursive Research (Simpler Models / Limited-Context Agents)**

**Goal of this preamble:** Enable even a simpler or smaller model to make steady, correct progress on this research by treating every section and sub-question as a decomposable, recursive task. Work one narrow slice at a time. Never tackle the whole document in one go.

**Recursive Research Loop (apply this to any section or open question):**

1. **Narrow the current scope ruthlessly.**
   - Read *only* the heading and first 1-2 paragraphs of the section you are assigned.
   - Read at most 1-2 directly referenced files (e.g., one decision + one source file).
   - If the scope still feels large, stop and decompose further before reading more.

2. **Decompose into 3–5 atomic sub-steps (or fewer).**
   - Turn the step into the smallest possible independent pieces.
   - Example decomposition for Section 2 ("What Direct Lowering Would Look Like"):
     - 2.1: How would a simple expression (Add, Member) be emitted directly against the bespoke ABI?
     - 2.2: How would TryCatchFinally / UsingStatement be emitted (real TryCatchFinally vs markers)?
     - 2.3: How would loops + break/continue be handled without Goto + ring restoration?
     - 2.4: How would the ring/Registers + FrameBase model be managed from a structured AST walk?
     - 2.5: What metadata would still need to be expanded at lowering time?

3. **Solve exactly one sub-step completely.**
   - For the chosen sub-step:
     - Gather only the minimal evidence required (one targeted grep or read of 1-2 classes/methods).
     - Produce a small, self-contained output: a bullet list of concrete implications, a 5–10 line code sketch, or a before/after comparison.
     - Explicitly note what information was sufficient and what is still missing.
   - Use structured output (e.g. "Findings for sub-step X:", "Evidence:", "Open sub-questions for recursion:").

4. **Recurse or synthesize.**
   - If a sub-step is still complex, create a micro-task for it and recurse (treat it as a new top-level step).
   - When all immediate sub-steps for the parent are done, write a 3–5 sentence synthesis paragraph that could stand alone.
   - Mark the parent step as "Partially explored – see sub-steps" and list the next recommended recursive step.

5. **Exit criteria for any step or sub-step (stop when met):**
   - You have at least one concrete, verifiable example or list of implications.
   - You have identified 1–3 crisp follow-up sub-questions.
   - Your output is written so a future (possibly even simpler) agent can continue without re-reading the whole document.
   - You have not introduced new architectural proposals without grounding them in the current codebase or prior decisions.

**Standing Rules for This Research (never violate):**
- The AST remains the primary symbolic and serializable form for models and synthesis.
- The bespoke VM ABI is the target runtime model (VmState, Registers/ring, Heap, FrameBase, SavedSp, value representation rules, etc.).
- When exploring "without primitives", always consider two variants:
  (a) Primitives become optional / secondary for the VM path.
  (b) Primitives are removed entirely for VM execution (kept only for other backends if needed).
- Always cross-check against the 2026-07-06 direction clarifications (AST symbolic primary + metadata expansion on lowering).
- Prefer small, evidence-based outputs over broad speculation.
- When you finish work on a step, append a "Next recursive step suggestion:" line.

**How to Start (for any agent):**
Pick exactly one top-level section (e.g. "2. What ... Would Look Like" or one bullet from Open Questions) and apply the Recursive Research Loop above. Do not read the entire document first.

After completing work, your final output should contain:
- Which step/sub-step you addressed.
- Your findings (evidence + concrete descriptions).
- The next suggested recursive micro-step.

---

## 1. Motivation and Problem Statement

The current execution path for the VM is:

```
AST (Syntax.Nodes / DomainExpression lowered to AST)
  → Analysis passes (many)
  → ExpansionPass / ToPrimitives() → flat PrimitiveNode[]
  → ProgramCompiler (ring allocation, label resolution, EH reconstruction)
  → Linq Expressions targeting the bespoke VM ABI
  → Action<VmState>
```

The bespoke VM ABI is the custom runtime model:
- `VmState` (Registers/ring, FrameBase, SavedSp, Heap, etc.)
- Long-based values (scalars inline, objects as heap handles)
- Custom call frames, upvalues, result extraction via `InterpretResult` + `RootValueKind`
- µop-level tracing, DebugInterrupt, loop limiting, etc.

This path has accumulated significant complexity:
- Flattening the AST forces loss of structure (control flow, lexical scopes, EH regions).
- Recovery requires: ring allocator + `BuildTargetDepth`, `ConsumedPcs`, `SavedSp`; `ExceptionRegionAnalysisPass` + `RegionMarker` + `ExceptionTableBuilder` + `DispatchException`; many analysis passes whose primary job is restoring what the tree already had.
- EH (Strategy B) in particular is a large reconstruction: markers in the flat stream + side table + separate handler compilation + CLR try/catch wrapper.
- Result: the "canonical IR" (primitives) is quite far from both the source AST and the final execution model.

Recent direction clarifications:
- The **AST** is the primary symbolic, serializable, model-facing IR.
- Primitives are the IR for the *VM execution engine*.
- Lowering should **expand** known metadata rather than discard information.
- `Poly/Ir/` is dead.

This raises a direct question: **If the primary consumer of the lowered form is the bespoke VM ABI, do we need the flat primitive layer at all for the main execution path?**

Could we lower *directly* from the rich AST to structured Linq Expressions that implement the bespoke ABI, preserving high-level structure where it helps?

## 2. What "Direct AST → Bespoke VM ABI" Would Look Like

### Proposed Emitter
A new (or refactored) component, e.g.:
- `VmAbiExpressionGenerator` (or `LinqExpressionGenerator` with a "bespoke ABI" mode / context)
- Or a dedicated `DirectVmLoweringPass` + emitter

It would:
- Walk the AST (after analysis passes that are still useful: type/member resolution, side effects, value representation, etc.).
- Emit `System.Linq.Expressions.Expression` trees that:
  - Use real `Expression.Block`, `Expression.TryCatchFinally`, `Expression.Loop` + labels, `Expression.Conditional`, etc.
  - Thread the custom ABI through everything:
    - Values live in ring registers (`Registers` array) or explicit slot locals.
    - Object values go through `state.Heap.Allocate(...)` / handle indirection.
    - Bools remain 0/1 or are normalized at ABI boundaries.
    - Calls manage `FrameBase`, `SavedSp`, upvalue capture via the existing closure mechanisms.
    - Result extraction still uses `RootValueKind` + `InterpretResult` logic.
- Preserve source-level structure for diagnostics, tracing, and human readability of the generated expressions.

Example sketch (pseudo):

```csharp
// Instead of flattening TryCatchFinally into markers + Gotos
case TryCatchFinally tcf:
    var tryBody = Compile(tcf.TryBlock, ctx);   // structured
    var catchClauses = ...;
    var finallyBody = tcf.FinallyBlock != null ? Compile(tcf.FinallyBlock, ctx) : null;

    // Emit real structured form, but every sub-expression knows the ABI
    return finallyBody != null 
        ? Expression.TryCatchFinally(tryBody, finallyBody, catchClauses)
        : Expression.TryCatch(tryBody, catchClauses);
```

The emitted tree would still ultimately be compiled to an `Action<VmState>`, but the intermediate expression tree stays much closer to the AST shape.

### Role of Analysis Passes
Many current passes exist primarily to feed structure recovery after flattening:
- `ControlFlowAnalysisPass`, `ExceptionRegionAnalysisPass`, `JumpTargetResolution`, parts of definite assignment, etc.

In a direct model these would be reduced or repurposed to:
- Correctness (types, definite assignment for the ABI).
- Optimization (side-effect analysis for elision, constant folding that can be pushed into the emitter).
- Metadata expansion (attach `ValueRepresentationMetadata`, call-site info, etc., directly to AST nodes for the emitter to consume).

### Metadata Expansion
As per current direction: at lowering time we have the AST + full analysis result. The emitter can directly produce:
- Richer per-node or per-subtree metadata.
- Self-describing ABI artifacts (e.g., "this subtree needs these heap types", "these variables are upvalues").
- No need for post-lowering reconstruction passes like `ExceptionTableBuilder`.

## 3. Implications of Not Having (or De-emphasizing) Primitives for the VM Path

### Benefits
- **Dramatically simpler control flow and EH**: Real `TryCatchFinally`, natural loop/break/continue, proper lexical blocks. No `RegionMarker`, no `DispatchException` PC scanning, no "finally after catch" special cases in the flat stream.
- **Less reconstruction machinery**: Ring allocator becomes less central (or can be applied more locally inside the emitter). Side tables for EH/debugging can be derived from the structured emitter if needed, rather than from a flat list.
- **Higher fidelity**: Generated expressions more closely mirror the source AST. Easier to debug the lowering itself. Source names, structure, and intent survive better into the expression tree (and thus into traces/debug info).
- **Reduced analysis surface**: Fewer passes whose job is "undo the flattening we just did."
- **Faster iteration on language features**: Adding `UsingStatement`, `Switch`, complex lambdas, etc., is closer to what `LinqExpressionGenerator` already does well.
- **Better alignment with "AST as symbolic primary"**: The thing models and synthesis see (AST) is also what drives execution more directly.

### Risks and Costs
- **Loss of uniform IR for backends**:
  - C# generator, potential WASM/AOT, peephole optimizer, portable serialization (INT-019) currently target (or could target) primitives.
  - A direct ABI emitter is tied to the Linq Expression + VmState world. Other backends would need their own emitters or a new common form.
- **µop-level tracing and introspection**:
  - The current design gives cheap per-µop tracing (`TraceBefore`, `VmTrace.LogUop`).
  - A direct emitter would need a different strategy (source-level tracing, expression tree visitors, or injecting trace points at AST nodes).
  - DebugInterrupt / single-stepping at "µop" granularity becomes harder or changes character.
- **Optimization surface**:
  - Peephole, DCE, fusion, etc., are currently envisioned at the primitive level.
  - Some of this could move to the AST/analysis level or into the emitter, but the "flat list of ops" is convenient for certain passes.
- **Synthesis and model targeting**:
  - If models or the synthesis layer ever want a stable low-level symbolic target (beyond the AST), primitives were intended to provide that.
  - With AST as the symbolic form, this may be less important — but it needs explicit confirmation.
- **Duplication risk**:
  - We already have `LinqExpressionGenerator` (native) and `ProgramCompiler` (ABI via primitives).
  - A third direct ABI emitter increases the "three ways to lower" problem unless we consolidate (e.g., make LinqExpressionGenerator the base and have ABI-specific lowering rules).
- **Performance / codegen quality**:
  - The ring allocator was specifically invented to keep the number of CLR locals small and enable JIT enregistration.
  - A direct emitter would need to achieve similar (or better) register pressure characteristics without the global ring simulation.
- **Canonical semantics claim**:
  - If the VM is still "canonical," but the lowering path changes, we must ensure behavioral equivalence (or explicitly decide what "canonical" means when there are multiple lowering strategies).

### What Happens to Existing Primitive Work?
- StackEffect, explicit slots, Phi, BasicBlock/Module concepts, CallExternal catalog, etc.
  - Could become optional annotations or an internal representation used *inside* a direct emitter.
  - Or kept as a separate "export" format for tools, serialization, or other backends.
- `PrimitiveExpansionMetadata` would no longer be the central artifact for VM execution.
- `ProgramCompiler` would shrink or be replaced for the main path.

### Interaction with Other Systems
- **Domain lowering**: Currently lowers to AST, then to primitives. Direct path would stop at AST + emitter.
- **Synthesis / Macro validation**: Uses VM for some validation. Would need to decide which lowering path is authoritative.
- **Testing and parity**: `AssertVmMatchesLinq` and cross-engine tests would compare the direct ABI emitter against the native Linq path.
- **Tracing / observability**: Major redesign needed if µop traces are valued.
- **INT-019 (portable IR)**: Would likely target AST (or a thin export of it) rather than primitives, or keep primitives as one possible export.

## 4. Comparison

| Aspect                        | Current (AST → Primitives → ProgramCompiler) | Direct AST → VmAbi Emitter                  | Native Linq (current reference) |
|-------------------------------|-----------------------------------------------|---------------------------------------------|---------------------------------|
| Structure preservation        | Poor (flat + markers)                         | Good (real blocks, try/catch, loops)       | Excellent                      |
| EH implementation             | Complex reconstruction (side tables)          | Natural `Expression.TryCatchFinally`       | Natural                        |
| Ring / register pressure      | Explicit global simulation                    | Local decisions inside emitter             | CLR decides                    |
| Analysis for structure        | Heavy recovery passes                         | Much lighter                               | Minimal                        |
| µop-level tracing             | Natural and cheap                             | Requires new mechanism                     | Not applicable                 |
| Uniformity for other backends | High                                          | Lower (per-backend emitters)               | N/A                            |
| Fidelity to AST for models    | Indirect                                      | Direct                                     | Direct                         |
| Metadata loss on lowering     | Significant                                   | Minimal (expansion opportunity)            | Minimal                        |

## 5. Open Questions

1. **What is the minimal set of use cases that truly require a flat primitive form today?**
   - µop tracing?
   - Future non-CLR backends?
   - Synthesis targeting?
   - Portable snapshots (INT-019)?

2. **Can we keep primitives as an *optional* export / optimization target** while making direct lowering the primary VM path?

3. **Tracing and debugging story** — is per-µop the right granularity, or can we achieve equivalent observability at the AST / expression level?

4. **Register allocation strategy** without a global ring — can a direct emitter produce comparably efficient code?

5. **How does this affect the "primitives are the canonical IR" decision?** Would we scope it more narrowly to "the canonical IR for certain backends and tools"?

6. **Consolidation opportunity**: Could the existing `LinqExpressionGenerator` be generalized so that both native and bespoke-ABI lowering are modes of the same structured walker?

7. **Impact on the neurosymbolic vision**: If models primarily see and produce AST, and execution is a projection from there, does the "symbolic IR" role of primitives diminish?

## 6. Potential Next Steps / Experiments

Use the preamble above recursively. Example first micro-task for a simpler model:

1. Read only Section 2 + this bullet.
2. Pick one construct (start with TryCatchFinally or a simple expression).
3. Produce a short sketch of direct emission vs current path.
4. Document findings + suggest the next recursive sub-step.

High-level next steps (break these down using the preamble):
- **Spike**: Implement a minimal direct emitter for a subset (arithmetic + comparisons + simple try/catch + one loop form) and compare generated expression trees + runtime behavior.
- **Complexity audit**: Count LOC and concepts dedicated to "undo flattening" (ring, EH tables, marker handling, etc.) and estimate savings.
- **Tracing prototype**: Explore injecting trace points at AST node boundaries vs. µop boundaries.
- **Backend impact assessment**: Catalog all current and planned consumers of primitives and classify "must have flat IR" vs. "can consume AST or richer form".
- **Decision framing**: If the spike looks promising, draft a narrow ADR or amendment that scopes the role of primitives more precisely and authorizes a direct path for the VM.

## 7. Related Documents to Update if Pursued

- `docs/decisions/2026-07-04-primitives-as-canonical-ir.md` (scope clarification)
- `docs/decisions/2026-06-08-vm-as-canonical-semantics.md`
- Interpretation system resolution plan and architecture review
- `Poly/Interpretation/README.md` and `Vm/README.md`
- Any plans mentioning INT-019, peephole, or portable IR

---

*This document is intentionally open-ended. Its purpose is to flush out the idea, surface implications, and provide a place to collect experiments and data before any architectural decision.*