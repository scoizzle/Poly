> **ARCHIVED (2026-07-10)** — Do not implement. Superseded by direct AST→VM-ABI (`DirectVmAbiEmitter`). See `docs/plans/archive/interpretation/README.md`.
>
> Original document follows for historical context only.

# Abstract Interpretation & SSA Plans

## 1. Abstract Interpretation for Type Resolution

**Concept:** Instead of pattern-matching node types to infer types, run a simplified symbolic interpreter *during analysis* — the same way Julia does. Constant expressions are evaluated concretely. Control flow branches are explored. Types flow through variables, calls, and returns.

### How it augments the current `TypeResolver`

The current `TypeResolver` is a pure pattern-match switch:

```csharp
node switch {
    Constant c => typeof(c.Value),
    Add => ResolveArithmeticType(...),
    Invoke invoke => ResolveMethodInvocationType(...),
    Lambda lambda => ResolveBlockType(lambda.Body),
    _ => null
}
```

This cannot resolve:
- Types through complex control flow (`if (cond) x = 1 else x = "hello"` → type is `object`)
- Recursive lambda return types
- Generic instantiations where the type depends on a value
- The narrowed type after an `if` check (`if (x is string)` → in the then-branch, `x` is `string`)

An abstract interpreter pass (new `INodeAnalyzer`, runs after `TypeResolver`) addresses these by tracking *abstract values* — either a concrete type or a known constant:

### Abstract value lattice

```
Top (unknown/any)
   ↑
   Type (concrete CLR type)
   ↑
   Constant (known concrete value, e.g. 42, "hello")
   ↑
   Bottom (unreachable / contradiction)
```

### How it works

The interpreter walks the AST like a simplified VM, maintaining an abstract environment `Dictionary<string, AbstractValue>` per scope. For each node:

| Node | Abstract operation |
|------|-------------------|
| `Constant(42)` | Return `AbstractValue.Constant(42)` |
| `Add(a, b)` | If both are Constant → Constant(a + b). Else if both have type int → Type(int). Else → Top. |
| `Variable("x")` | Look up `x` in the environment |
| `Assignment(x, v)` | Store v's abstract value into `x`'s slot |
| `IfStatement(cond, then, else)` | Evaluate both branches independently, merge environments at merge point |
| `Invoke(lambda, args)` | If lambda body is known, inline-evaluate with argument bindings |
| `Parameter p` | If the caller's argument is a Constant → Constant. Else Type(p). |

### Type narrowing after `if`

The key capability the current resolver lacks:

```
if (x is string) {
    // Here, x should be known as string
    y = x.Length;  // Should resolve to int
}
```

The abstract interpreter tracks the *refined* type after a `TypeIs` check. In the then-branch, `x`'s abstract value is narrowed to `Type(string)`. In the else-branch, it's narrowed to `Not(string)` (or left as Top).

### Relationship to existing passes

| Current pass | Augmented by |
|-------------|--------------|
| `TypeResolver` | Becomes the fallback for nodes the abstract interpreter can't reach. The abstract interpreter fills in types that the pattern-match misses. |
| `LambdaReturnTypeAnalyzer` | Replaced entirely — the abstract interpreter resolves lambda return types during inline evaluation. |
| `ConstantFoldingPass` | Subsumed — the abstract interpreter already evaluates constants. The folding pass becomes a cache of the abstract interpreter's results. |

### Implementation sketch

```csharp
internal sealed class AbstractInterpreter : INodeAnalyzer {
    private readonly Stack<AbstractEnvironment> _envStack = new();
    private AbstractEnvironment _env = new();

    public void Analyze(AnalysisContext context, Node node) {
        if (!context.TryBeginAnalyzerVisit<AbstractInterpreter>(node)) return;
        
        var result = Eval(context, node);
        if (result is TypeValue tv)
            context.SetResolvedType(node, tv.Type);
    }

    private AbstractValue Eval(AnalysisContext context, Node node) => node switch {
        Constant c => AbstractValue.Constant(c.Value),
        Variable v => _env.TryGetValue(v.Name, out var val) ? val : AbstractValue.Top,
        Assignment a when a.Destination is Variable v => {
            var val = Eval(context, a.Value);
            _env[v.Name] = val;
            return val;
        },
        IfStatement ifStmt => EvalIf(context, ifStmt),
        Invoke invoke => EvalInvoke(context, invoke),
        TypeIs ti => HandleTypeNarrowing(context, ti),
        Block block => EvalBlock(context, block),
        // ... more cases
    };
}
```

---

## 2. SSA Form for the Lowered IR — Full Plan

## 2. SSA Form for the Lowered IR — Full Plan

**Concept:** Transform the lowered bytecode into Static Single Assignment form — every virtual register is assigned exactly once, and `phi` nodes merge values at control flow join points. Optimize in SSA, then destruct back to stack bytecode for the VM.

### Data structures

The SSA IR is a separate representation from `byte[]`:

```csharp
// A virtual register — analogous to a value on the evaluation stack
internal sealed record SsaValue(int Id, SsaType Type);

internal enum SsaType : byte { Int, Long, Double, HeapRef }

// A basic block with ordered instructions
internal sealed class SsaBlock {
    public int Id;
    public List<SsaInstruction> Instructions = [];
    public List<int> Predecessors = [];
    public List<int> Successors = [];
    public int? LoopHeader;       // if this block is a loop header, the back-edge block id
}

internal sealed class SsaInstruction {
    public SsaOpcode Opcode;
    public SsaValue? Result;       // the value this instruction produces (null for stores, jumps)
    public List<SsaValue> Operands = [];  // values consumed
    public List<SsaValue> PhiOperands = []; // for phi: [pred0_val, pred1_val, ...]
    public int OriginalPC;         // source bytecode PC for debug info
}

internal enum SsaOpcode : byte {
    // Values
    ConstInt, ConstLong, ConstDouble, LoadLocal, LoadArg, LoadUpvalue,
    // Arithmetic
    Add, Sub, Mul, Div, Mod, Neg,
    DAdd, DSub, DMul, DDiv, DNeg,
    // Comparisons
    Eq, Ne, Lt, Le, Gt, Ge,
    DEq, DNe, DLt, DLe, DGt, DGe,
    // Stack management (SSA-only, for destruction)
    Phi,
    // Control flow
    Jump, Branch, Return, Throw, Call,
    // VM bridge (opaque to SSA — passes through)
    StoreLocal, StoreArg, StoreUpvalue, CallExternal, AllocateClosure, CallClosure,
    IsNull, Narrow, Not, Int, Iret, StrConcat, EnumeratorMoveNext,
    Dup, Pop, // SSA eliminates these; present only during construction
}
```

```csharp
internal sealed class SsaProgram {
    public List<SsaBlock> Blocks = [];
    public List<SsaFunction> Functions = [];
    public Dictionary<int, SsaValue> Constants = []; // LoadConst index → SsaValue
    public List<CallSiteDelegate> CallSites = [];
}

internal sealed record SsaFunction(int EntryBlock, int ParamCount, int LocalCount);
```

### Phase 1: SSA Construction (bytecode → SSA)

The `SsaBuilder` reads the stack bytecode and reconstructs data flow. This is the hardest phase because the VM is stack-based and SSA is register-based — the stack content at each instruction must be recovered.

#### 1a. Basic block discovery

Walk the code linearly. A new block starts at:
- Every `Jump` / `JumpIfFalse` target (any PC that appears as a jump operand)
- Every instruction immediately after a `Jump`, `Return`, or `Throw` (fall-through)
- The entry point of each function (from `FunctionEntry.PC`)
- Every `Call` return point (the instruction after a Call)

Each block gets a list of predecessors (blocks that can jump to it) and successors (blocks it can jump to).

#### 1b. Stack reconstruction via data-flow analysis

For each block, compute the *stack state* at entry — the set of `SsaValue` ids on the evaluation stack. This is a classic forward data-flow problem:

```
In[block] = meet(Out[pred] for pred in block.Predecessors)
Out[block] = transfer(In[block], block.Instructions)
```

The meet operation for stack height: if predecessors have different stack heights, insert `phi` nodes for the values below the common height. If heights are irreconcilable (one predecessor leaves 3 values, another leaves 2), the program is malformed — this shouldn't happen if the lowering is correct, and the `StackDepthAnalyzer` catches it.

After this analysis, each instruction knows the `SsaValue` ids on the stack before it executes. The builder emits:
- `Pop` → consumes `SsaValue` from the top of the reconstructed stack
- `PushInt 42` → produces a new `SsaValue`, pushed onto the reconstructed stack
- `Add` → consumes 2 `SsaValue`s, produces 1 new one

#### 1c. Phi placement

At blocks with multiple predecessors (join points), `phi` nodes are inserted for each `SsaValue` that is live across the incoming edges. A value is "live" if it's defined in one predecessor and used in a successor.

Placement strategy: use dominance frontiers. A `phi` for `v` is placed at every block in the dominance frontier of the block that defines `v`. This is the minimal placement.

For the Poly IR, a simplified approach works: at each join point with N predecessors, for each SsaValue that exists in Out[pred_i] with different ids across predecessors, insert a `phi` that merges them.

#### 1d. Variable renaming

Each store to a local (`StoreLocal N`) produces a new SSA version of that local. Each load (`LoadLocal N`) reads the reaching definition — the most recent StoreLocal to the same N in the dominator tree.

During the stack reconstruction pass, a side-table `Dictionary<int, SsaValue>` tracks the current SSA value for each local slot. Stores update it, loads read it.

### Phase 2: SSA Optimization Passes

Each pass operates on `SsaProgram` and preserves SSA form (all values assigned once, phi at dominance frontiers).

#### 2a. Constant Propagation (Sparse Conditional Constant Propagation)

Standard Wegman-Zadeck algorithm:
- Lattice: Top (unknown) → Constant(c) → Bottom (overdefined)
- Worklist of SSA instructions and control flow edges
- For each instruction with constant operands, evaluate it and update the result's lattice value
- For `Branch` instructions with a constant condition, mark the taken edge as executable and the not-taken edge as not executable (dead branch elimination)

**Example:**
```
v1 = ConstInt 5
v2 = ConstInt 3
v3 = Add v1, v2    →  folded to v3 = ConstInt 8
v4 = LoadLocal 0
v5 = Lt v3, v4     →  v5 = Lt 8, v4  (partial fold — v4 is unknown)
```

#### 2b. Dead Code Elimination

Mark every instruction that has no users (its `SsaValue` is never an operand of another instruction). Remove them. Iterate until no changes.

Critical instructions (branches, returns, stores, calls, throws) are always live even if their result is unused.

**Example:**
```
v1 = ConstInt 5       ; never used → removed
v2 = LoadLocal 0
StoreLocal 0, v2      ; store is critical (side effect) → kept
```

#### 2c. Global Value Numbering

Two instructions that compute the same operation on the same operands produce the same value. Replace all references to the second with the first.

Hash each instruction by `(Opcode, Operands)` and deduplicate.

**Example:**
```
v1 = Add x, 5
v2 = Add x, 5         ; same opcode and operands → v2 = v1
v3 = Add x, 5         ; v3 = v1
w = Add v1, v2        →  w = Add v1, v1  (after dedup)
```

#### 2d. Loop Invariant Code Motion

For each loop (identified by a back-edge in the CFG), find instructions whose operands are all defined outside the loop. Move them to the loop pre-header block (the block just before the loop header).

**Example:**
```
before loop:
  v1 = ConstInt 10
  v2 = ConstInt 3     ; loop-invariant

loop:
  v3 = Mul v1, v2     ; loop-invariant → hoisted before loop
  v4 = LoadLocal 0
  v5 = Add v3, v4
  ...
```

Requires: natural loop detection (dominator-based), loop nesting tree.

### Phase 3: SSA Destruction (SSA → bytecode)

After optimization, convert the SSA program back to the VM's stack bytecode.

#### 3a. Phi elimination

Each `phi(v0, v1, ..., vN)` at block B with predecessors P0..PN is eliminated by inserting copy instructions at the end of each predecessor:

```
// Before:
Block B1:          Block B2:         Block B3:
  ...                ...               ...
  → B3               → B3             v2 = phi(v0, v1)

// After:
Block B1:          Block B2:         Block B3:
  ...                ...               ...
  push v0            push v1          (copy already on stack)
  → B3               → B3
```

This means the phi's value is placed on the evaluation stack by each predecessor before jumping to B3. No explicit copy is needed — the value is already on the stack from the predecessor's execution.

#### 3b. Stack scheduling (linearization)

Each SSA block must be converted to a sequence of stack operations. The naive approach:

1. For each instruction in the block in order:
   - Push all operands onto the stack (in order)
   - Execute the operation (pops operands, pushes result)
2. This produces correct bytecode but with many redundant `Dup`/`Pop` pairs

Optimization: track the stack depth and reuse values already on the stack. If operand `v1` was just computed and is still on the stack, don't push it again — just use it. This reduces instruction count by 30-50%.

#### 3c. Local slot assignment

SSA values that are live across block boundaries (used in a successor block) must be spilled to local slots. Use a simple linear scan:

1. Sort all SSA values by their first appearance in the block order
2. Assign live ranges to local slots greedily
3. Insert `StoreLocal` at the definition point and `LoadLocal` at each use point

Values used only within a single basic block are kept on the evaluation stack and never spilled.

### Pipeline integration

The SSA optimizer sits inside `Optimizer.Optimize()`:

```csharp
public static Bytecode Optimize(Bytecode input) {
    var ssa = SsaBuilder.Build(input);
    if (ssa is null) return input; // malformed, skip

    ConstantPropagation.Run(ssa);
    DeadCodeElimination.Run(ssa);
    GlobalValueNumbering.Run(ssa);
    LoopInvariantCodeMotion.Run(ssa);

    return SsaDestructor.Destroy(ssa);
}
```

The peephole optimizer's current patterns (`PushInt 0; Add` → identity) are subsumed by SSA constant propagation. The `Dup; Pop` pattern is handled by SSA dead code elimination (the `Dup` result has no users). The remaining peephole patterns (jump threading, multi-pop) handle artifacts the SSA passes can't reach.

### Files

All new files under `Poly/Interpretation/VirtualMachine/Ssa/`:

| File | Contents |
|------|----------|
| `SsaTypes.cs` — Type definitions | `SsaValue`, `SsaBlock`, `SsaInstruction`, `SsaProgram`, `SsaFunction`, enums |
| `SsaBuilder.cs` — Construction | Bytecode → SSA (stack reconstruction, phi placement, renaming) |
| `SsaOptimizer.cs` — Orchestrator | `Optimize(Bytecode)`, pipeline ordering |
| `SsaConstantPropagation.cs` | SCCP with lattice |
| `SsaDeadCodeElimination.cs` | Mark-sweep DCE |
| `SsaGlobalValueNumbering.cs` | GVN via hash consing |
| `SsaLoopInvariantCodeMotion.cs` | Loop detection + hoisting |
| `SsaDestructor.cs` | SSA → bytecode (phi elimination, stack scheduling, spilling) |

### Testing strategy

Each phase is testable independently:

1. **SsaBuilder**: Feed hand-crafted bytecodes (like `VmSkeletonTests` does), verify the SSA program has correct blocks, phis, and value numbering.
2. **Optimization passes**: Construct small SSA programs, run the pass, assert the expected transformations.
3. **Round-trip**: Run builder → destructor without optimizations, verify the bytecode is identical to the input (modulo phi elimination artifacts).
4. **Full pipeline**: Run `Optimizer.Optimize()` on the `VmParityTests` suite and assert all tests pass with identical results.

### Dominator tree computation

Required by: phi placement, loop detection, LICM.

Algorithm: **Lengauer-Tarjan** — computes the immediate dominator for every block in near-linear time O(E α(N)).

Implementation sketch:
```csharp
internal static class Dominators {
    public static Dictionary<int, int> Compute(SsaProgram prog) {
        // 1. DFS from entry block to assign preorder numbers
        // 2. Walk in reverse preorder, computing semi-dominator via path compression
        // 3. Compute immediate dominator from semi-dominator
        // 4. Return dictionary: block id → immediate dominator id
    }

    public static Dictionary<int, HashSet<int>> DominanceFrontier(SsaProgram prog) {
        // For each block, find the set of blocks it doesn't strictly dominate
        // but that have a predecessor it does dominate
    }
}
```

Lengauer-Tarjan is standard enough that any compiler textbook provides the pseudocode. The implementation is ~80 lines.

### Exception region CFG edges

Exception regions introduce implicit control flow that the basic block discovery must account for:

```
try_start:
  [try body instructions]
  Jump try_success        ← explicit edge

try_catch:
  [catch handler]
  Jump try_merge          ← explicit edge

try_finally:
  [finally handler]
  EndFinally              ← re-throws or falls through

try_merge:
  ...
```

The `Throw` opcode inside a try body has an **implicit** edge to the catch handler (if present) or the finally handler. The builder must:

1. At each `Throw` instruction, look up `FindRegion(pc)` to find the enclosing try block.
2. If the region has a catch block, add `ThrowPC → CatchBlock` as an implicit successor edge.
3. If the region has a finally block, add `ThrowPC → FinallyBlock` as an implicit successor edge (the `PendingExceptionValue` mechanism).
4. The `EndFinally` opcode has an implicit edge back to the original throw's target (catch) or out of the function.

These implicit edges affect the CFG shape for dominance computation and phi placement. They don't affect stack reconstruction (the stack at the throw point is a single exception value, which is consumed by the catch handler).

### Heap-allocated values (closures, upvalues)

`LoadUpvalue` and `StoreUpvalue` access heap memory. SSA cannot track through heap stores — the reaching-definition analysis is local to the function. Rules:

- `LoadUpvalue` produces an SSA value marked as `SsaType.HeapRef`. It is never constant-folded or GVN'd because the heap may have changed between two loads.
- `StoreUpvalue` is always a critical instruction (never eliminated by DCE).
- `AllocateClosure` produces a `HeapRef` value. It is never eliminated (allocations have side effects).
- `CallClosure` consumes a `HeapRef` (the closure) and arguments, produces a result. Opaque.

These rules are enforced by the DCE pass — instructions with `SsaType.HeapRef` results or operands are never removed.

### Concrete bytecode example: while loop

Input bytecode (from the lowering of `while (i < 5) { i = i + 1 }`):

```
PC=0:  LoadLocal 0     ; push i
PC=5:  PushInt 5       ; push 5
PC=10: Lt              ; pop 2, push (i < 5)
PC=11: JumpIfFalse → L2  ; pop condition
PC=16: LoadLocal 0     ; push i
PC=21: PushInt 1       ; push 1
PC=26: Add             ; pop 2, push i+1
PC=27: Dup             ; duplicate
PC=28: StoreLocal 0    ; pop 1, store
PC=33: Pop             ; pop 1
PC=34: Jump → L0       ; back to condition
PC=39: L2: ...
```

Basic blocks:
- **B0** (PC=0-15): entry → condition + branch
- **B1** (PC=16-38): loop body
- **B2** (PC=39+): loop exit

CFG: `B0 → B1, B2` (conditional), `B1 → B0` (back edge), `B2 → exit`.

Stack at B0 entry: `[]` (empty — function entry).
Stack at B1 entry: `[]` — after `JumpIfFalse` pops the condition, the stack is empty.
Stack at B2 entry: `[]` — same.

Phi placement: no phis needed! No value is defined in one predecessor and used in another. The loop counter `i` is stored to a local slot (`StoreLocal 0`) and loaded via `LoadLocal 0`. The SSA builder's reaching-definition table tracks the local slot across blocks, producing:

```
B0:
  v1 = LoadLocal 0        ; reaching def: local[0] from entry (undefined)
  v2 = ConstInt 5
  v3 = Lt v1, v2
  Branch v3, B1, B2

B1:
  v4 = LoadLocal 0        ; reaching def: local[0] from the phi below
  v5 = ConstInt 1
  v6 = Add v4, v5
  StoreLocal 0, v6        ; updates reaching def of local[0] in this block
  Jump B0
```

The `LoadLocal 0` at the condition header (B0) reads local[0], which may come from either the function entry (undefined) or the back-edge (B1's `StoreLocal 0`). This is where a **phi** is needed:

```
B0 (loop header):
  v0 = phi(entry: undef, B1: v6)    ; merge of local[0] across entry and back-edge
  v1 = LoadLocal 0  →  v1 = v0      ; replaced by SSA value
  v2 = ConstInt 5
  v3 = Lt v1, v2
  Branch v3, B1, B2
```

The builder detects this because the reaching-definition table has two different values for local[0] at the entry of B0 (one from the function entry, one from B1's back-edge). It inserts the phi and replaces the `LoadLocal 0` with a reference to the phi's result.

### Concrete bytecode example: try/catch

Input bytecode (from `try { throw 42; } catch (ex) { ... }`):

```
PC=0:  PushInt 42        ; exception value
PC=5:  Throw             ; implicit edge to catch handler
PC=6:  Jump → exit       ; normal path (never reached)
PC=11: Pop               ; catch handler: pop exception
PC=12: [catch body...]
PC=17: Jump → exit
PC=22: exit: ...
```

Exception region: `try=[0, 6), catch=11`.

Basic blocks:
- **B0** (PC=0-5): try body
- **B1** (PC=6-10): after try (unreachable in practice — throw always takes the implicit edge)
- **B2** (PC=11-21): catch handler
- **B3** (PC=22+): exit

Implicit edges: `B0 → B2` (via `Throw` + exception region).

Stack at B2 entry: `[]` — the catch handler's implicit first instruction is `Pop` or `StoreArg`, which consumes the exception value from the VM's implicit push.

The builder must add the implicit edge `B0 → B2` during block discovery. During phi placement, the stack state at B2 entry has no phis needed (the exception value is consumed by `Pop`/`StoreArg`, not tracked as an SSA value).

### Induction variable analysis (post-LICM)

After LICM, identify induction variables — values that change by a constant amount each loop iteration:

```
loop:
  i = phi(i0, i_next)
  i_next = Add i, 1
```

Pattern: a phi at the loop header whose operands are the initial value and an arithmetic operation on the phi result.

For Poly's IR, the most valuable transformation is **dead induction variable elimination** — if `i` is only read by its own increment and a bounds check (`Lt i, N`), and `N` is known, the loop can be bounded by an explicit counter rather than a variable load.

This is a follow-up optimization to add after the core SSA pipeline is stable. Not part of the first implementation.

### Loop unrolling

SSA provides the preconditions for loop unrolling directly from IV analysis + constant propagation:

```
Trip count = (N - i0) / step
  where i = phi(i0, i_next), i_next = i + step, bound = Lt i, N, N is constant
```

When the trip count is a small known constant (e.g. 4, 8, 16), unrolling eliminates the loop control overhead entirely:

```
; Before (trip count = 4):       ; After (unrolled 4x):
  i = 0                            body(0)
loop:                              body(1)
  body(i)                          body(2)
  i = i + 1                        body(3)
  if i < 4 goto loop
```

The unroller duplicates the loop body N times, renames SSA values per copy, and removes the phi + branch. For loops with unknown or large trip counts, it can still peel the first iteration (loop peeling) to eliminate the entry branch.

SSA makes this straightforward:
- IV analysis identifies the induction variable and trip count
- The phi node at the loop header tells the unroller which values change per iteration
- Each unrolled copy gets fresh SSA values — no register conflicts
- After unrolling, DCE cleans up dead phi operands and GVN deduplicates across copies

This is valuable for the neurosymbolic loop because macros often generate small counted loops (`for i in 0..3 { ... }`) that the model produces as imperative IR. Unrolling them is a pure win — no branch penalty, and the optimizer sees each iteration's operations independently.

Implementation order: add after LICM and IV analysis are stable.

### Inter-procedural considerations

Each function has its own `SsaProgram`. Calls between functions are opaque — the `Call` opcode consumes arguments and produces a result, but the callee's SSA form is not inlined.

For `AllocateClosure` + `CallClosure`: the closure's target function index is known at compile time (`Closure.FuncIndex`). The builder could look up the target function's SSA form and inline it. This is equivalent to function inlining at the SSA level — powerful, but not part of the first implementation.

### Files (updated)

| File | Contents |
|------|----------|
| `SsaTypes.cs` | All type definitions, enums, value/block/program records |
| `SsaDominators.cs` | Lengauer-Tarjan dominator tree + dominance frontier |
| `SsaBuilder.cs` | Bytecode → SSA (BB discovery, stack reconstruction, phi placement, renaming) |
| `SsaOptimizer.cs` | Orchestrator: `Optimize(Bytecode)`, pass ordering |
| `SsaConstantPropagation.cs` | Sparse conditional constant propagation |
| `SsaDeadCodeElimination.cs` | Mark-sweep with heap-ref/critical-instruction rules |
| `SsaGlobalValueNumbering.cs` | Hash-consing GVN |
| `SsaLoopInvariantCodeMotion.cs` | Natural loop detection + hoisting |
| `SsaDestructor.cs` | SSA → bytecode (phi elimination, stack scheduling, spilling) |

### Risks and mitigations

| Risk | Mitigation |
|------|------------|
| Stack reconstruction fails on complex CFGs | The VM's lowering produces structured control flow (no irreducible loops). Stack heights at join points should always match. The `StackDepthAnalyzer` catches mismatches before SSA runs. |
| Phi placement inserts too many phis | The dominance-frontier approach is minimal. For Poly's structured IR, manual placement at loop headers + if-merge points is simpler and correct. |
| Spilling in the destructor produces worse code than the original | Compare bytecode size before and after. If spilling degrades quality, use the unoptimized version for that function. |
| Exception region implicit edges complicate CFG | The builder adds explicit edges from each `Throw` to its catch/finally handler. The dominator tree and phi placement handle these like normal branches. |
| SSA doesn't handle all opcodes cleanly (CallExternal, CallClosure) | Treat them as opaque — they consume/produce values but their internals are invisible to SSA. The operands and results are tracked; the operation isn't analyzed. |
