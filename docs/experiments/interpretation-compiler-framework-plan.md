# Interpretation → Extensible Compiler Framework (Revised)

Date: 2026-06-24
Author: GitHub Copilot (assistant), incorporating code review of existing pipeline

## 1. Problem Statement

The current `UopGenerationPass` (`Poly/Interpretation/Analysis/LoweringPrep/UopGenerationPass.cs`) is a monolithic 700-line switch that maps every AST node type directly to µop fragments. Adding a new language construct means adding another `case` arm and manually managing slot allocation, label references, and frame metadata. The φ merge logic in `Lowering.Assemble()` (`Poly/Interpretation/Vm/Lowering.cs` lines 120–215) uses heuristic ring matching and runtime reflection (`(dynamic)x.Target`) to detect where phi nodes are needed—correct but fragile and hard to test in isolation.

A secondary concern: there are already two implicit backends (VM µops → expression tree and `LinqExpressionGenerator`'s direct `System.Linq.Expressions` emission), but they share no common lower-level representation. Adding a third (C# source, WASM, AOT) would require duplicating the lowering logic.

## 2. Goals

- Introduce a canonical **block-structured CFG IR with SSA values** that replaces ad-hoc µop fragments.
- Establish a single canonical pipeline: **AST → IR → backend**. Every AST node emits exactly one IR fragment; every backend visits IR.
- Replace the monolithic 700-line switch with a virtual `Emit` method on the `Node` base class, so every node type carries emit logic by default and override is opt-in at the type level.
- Replace the ring-based φ heuristic with explicit `Phi` instructions inserted by an SSA construction pass.
- Expose backends as **visitors** over the IR that never touch AST node code.
- Handle closures, heap constants, external call sites, and incremental compilation—all omitted from the previous plan.
- Maintain full backwards compatibility during migration; co-exist with old pipeline until parity is proven.

## 3. Canonical IR Design

### 3.1 Guiding principles

- **Single canonical pipeline**: AST → IR → backend. The IR is the only intermediate representation. Each AST node emits exactly one IR fragment. Backends never see AST nodes.
- **Block-structured CFG**: every block has a label, an ordered list of instructions, and a terminator (branch, jump, return, throw). No flat instruction list with label markers.
- **SSA values**: each instruction produces zero or one `Value`. Consumers reference that value directly—no implicit stack or ring. This makes dataflow explicit.
- **Typed, but minimally**: IR types are a small enum (`Int64`, `Float64`, `Boolean`, `Handle`, `Void`), kept CLR-agnostic. The CLR adaptation layer maps Handle to `object?`.
- **Scoped locals fallback**: for variable reassignment (`x = x + 1`), the IR allows mutable local slots accessed via `LoadLocal`/`StoreLocal` as an escape from SSA purity. The SSA construction pass converts these to SSA values with φ nodes at join points.

### 3.2 Core types

```csharp
// ── Values ──────────────────────────────────────────────────────────
public enum TypeKind { Int64, Float64, Boolean, Handle, Void }

/// <summary>Opaque handle to a value produced by an IR instruction.
/// Carries its type and a reference back to the defining instruction.</summary>
public sealed record Value(Instr Definition, TypeKind Kind, int Index);

// ── Instructions ────────────────────────────────────────────────────
public abstract record Instr(TypeKind ResultType, NodeId? Source);

public sealed record Const(long Value, TypeKind Kind, NodeId? Source) : Instr(Kind, Source);
public sealed record BinOp(OpKind Op, Value Left, Value Right, NodeId? Source) : Instr(Op.ResultType(Left.Kind, Right.Kind), Source);
public sealed record UnaryOp(UnaryOpKind Op, Value Operand, NodeId? Source) : Instr(Operand.Kind, Source);
public sealed record LoadLocal(int SlotIndex, TypeKind Kind, NodeId? Source) : Instr(Kind, Source);
public sealed record StoreLocal(int SlotIndex, Value Val, NodeId? Source) : Instr(TypeKind.Void, Source);
public sealed record Parameter(int SlotIndex, TypeKind Kind, NodeId? Source) : Instr(Kind, Source);  // entry-block function argument; defines initial SSA value for the slot
public sealed record Call(Value Target, Value[] Args, int ArgCount, bool IsExternal, int CallSiteIndex, NodeId? Source)
    : Instr(TypeKind.Int64 /* resolved from call-site table */, Source);
// CallSiteIndex: index into Module.CallSites (analogous to VmProgram.CallSites).
// Resolved during IR generation from AnalysisResult.GetResolvedMember().
public sealed record AllocClosure(int FuncIndex, Value[] Captures, NodeId? Source) : Instr(TypeKind.Handle, Source);
public sealed record LoadUpvalue(int UpvalueIndex, NodeId? Source) : Instr(TypeKind.Int64, Source);
public sealed record StoreUpvalue(int UpvalueIndex, Value Val, NodeId? Source) : Instr(TypeKind.Void, Source);
public sealed record AllocHeap(int HandleIndex, NodeId? Source) : Instr(TypeKind.Handle, Source);
public sealed record Phi(int? SlotIndex, Value[] Incoming, NodeId? Source) : Instr(Incoming[0].Kind, Source);
// SlotIndex is null for value-level Phis (emitted by IfStatement.Emit, WhileLoop.Emit —
// they select between expression values rather than a named local variable).
// SlotIndex is non-null for slot-level Phis (inserted by InsertPhis during SSA construction
// — they merge the current SSA value of a named mutable slot).

// ── Terminators ─────────────────────────────────────────────────────
public abstract record Terminator(NodeId? Source);
public sealed record Goto(BasicBlock Target) : Terminator(null);
public sealed record CondBranch(Value Condition, BasicBlock ThenTarget, BasicBlock ElseTarget) : Terminator(null);
public sealed record Ret(Value? Result) : Terminator(null);
public sealed record Throw(Value Exception) : Terminator(null);

// ── Blocks & Modules ────────────────────────────────────────────────
public sealed class BasicBlock {
    public string Name { get; }
    public List<Instr> Instructions { get; } = new();
    public Terminator? Terminator { get; set; }
}

public sealed class Module {
    public List<BasicBlock> Blocks { get; } = new();
    public List<BasicBlock> ExportedFunctions { get; } = new(); // entry points
    public List<object?> HeapConstants { get; } = new();     // indexed by handle
    public List<CallSite> CallSites { get; } = new();        // indexed by CallSiteIndex; mirrors VmProgram.CallSites
    public int MaxLocalSlots { get; set; }
}

// Minimal call-site descriptor — expanded during lowering
public sealed record CallSite(string MethodName, TypeKind ReturnType, int ArgCount);
```

### 3.3 Why this design

- **Block-structured**: the `LoweringPrepPass` currently assigns labels and manages loop scope via a stack—that work is intrinsic to the IR, not a separate pass. Each `BasicBlock` carries its label implicitly.
- **SSA by construction**: every `Instr` returns a `Value`. Consumers reference that value directly. No ring analysis, no `ConsumedFromPcs` metadata. The ring analysis becomes an optimization pass for the VM backend only.
- **Heap constants as module-level sideband**: the current `HeapConstantMetadata` collected during `UopGeneration` (keyed under `NodeId.Empty`) becomes `Module.HeapConstants`. No instruction-level indirection needed.
- **Phi explicit**: `Phi` selects among `Value[] Incoming`. No heuristic detection, no `PhiSourcePcs`/`PhiAltPcs` juggling.
- **Result types from operators**: IR instruction result types are derived from their operands and operator kind where possible (`BinOp` resolves via `Op.ResultType(Left.Kind, Right.Kind)`, `UnaryOp` inherits `Operand.Kind`). For `Call`, the result type comes from the resolved member in the call-site table (section 9.2). `Op.ResultType` is a `static TypeKind OpKind.ResultType(TypeKind, TypeKind)` extension/helper on `OpKind`, defined alongside `OpKind` under `Poly/Interpretation/Ir/` at implementation time; the standard cases are: integer ops return `Int64`, floating ops return `Float64`, comparison ops return `Boolean`. This makes the IR self-describing without requiring a separate type environment.

## 4. AST → IR (virtual method on Node base)

### 4.1 Design rationale

Syntax and Interpretation are in the same project (single `Poly.csproj`). And the pipeline is always **AST → IR → backend**. Every AST node has exactly one IR emit method, and backends never touch AST code. No `IIrEmitter` marker interface, no optional participation — every `Node` subtype overrides a virtual `Emit` method inherited from the base. If a node has no IR to emit (e.g. a pure annotation), it returns `null`.

This means:
- **No registration.** Adding a new node type _requires_ you to implement `Emit`; the compiler won't let you forget.
- **No `is` check.** The pass calls `node.Emit(ctx)` — every node has one.
- **No ceremony.** The base method provides a sensible default (return `null`).

### 4.2 Virtual method on `Node`

```csharp
// Poly/Syntax/Node.cs — one method on the abstract base
public abstract record Node {
    public NodeId Id { get; init; } = NodeId.NewId();
    public virtual IEnumerable<Node?> Children => [];

    /// <summary>Emit this node into the IR module.
    /// Returns the Value produced, or null if the node emits nothing
    /// (e.g. block-scoped variable declarations in most backends).</summary>
    public virtual Value? Emit(EmissionContext ctx) => null;
}
```

Every concrete node overrides it:

```csharp
// Poly/Syntax/Nodes/Add.cs
public sealed record Add(Node LeftHandValue, Node RightHandValue, NodeId Id)
    : BinaryExpression(LeftHandValue, RightHandValue, Id)
{
    public override Value? Emit(EmissionContext ctx)
    {
        var left = ctx.EmitChild(LeftHandValue);
        var right = ctx.EmitChild(RightHandValue);
        return ctx.Emit(new BinOp(OpKind.Add, left!, right!, Id));
    }
}
```

The `override` keyword is mandatory — if someone adds a new node type and doesn't override `Emit`, the compiler produces a warning on the empty base behavior. This contrasts with an interface approach where a missing implementation silently means "doesn't participate."

### 4.3 `EmissionContext`

Shared runtime for all `Emit` calls:

```csharp
public sealed class EmissionContext {
    public Module Module { get; }
    public BasicBlock CurrentBlock { get; set; }
    public AnalysisResult Analysis { get; }

    // Scoping
    public Scope Scope { get; }       // TopLevel | Lambda | Loop

    // Emit a child node: dispatches to node.Emit(this).
    // Returns the Value produced, or null for void nodes.
    public Value? EmitChild(Node child) {
        var actual = Analysis.GetNodeReplacement(child) ?? child;
        return actual.Emit(this);
    }

    // Append an instruction to CurrentBlock and return its result value.
    public Value Emit(Instr instr);

    // Create a new block and set it as the split target of CurrentBlock.
    public BasicBlock SplitBlock(string suffix);

    // Declare a mutable local slot (for variables). Returns slot index.
    public int DeclareLocal(string name, TypeKind kind);
}
```

### 4.4 `GenerationPass` — single pass, internal steps

The `GenerationPass` is registered as one `INodeAnalyzer` but internally runs both IR emission and IR transform passes as ordered steps. This avoids splitting IR work across multiple `INodeAnalyzer` implementations (which would abuse metadata to pass the `Module` around) while keeping each step as a pure `Module → Module` function that can be tested in isolation.

```csharp
public sealed class GenerationPass : INodeAnalyzer {
    // Per-step toggles for testing and incremental compilation
    public bool EnableSsa { get; init; } = true;
    public bool EnableConstFolding { get; init; } = true;
    public bool EnableInlining { get; init; } = true;

    public void Analyze(AnalysisContext context, Node node) {
        // ── Step 1: Emit IR from AST via node.Emit(ctx) ──
        var module = new Module();
        var block = new BasicBlock("entry");
        module.Blocks.Add(block);

        var ctx = new EmissionContext(module, block, context.GetResult());
        var result = node.Emit(ctx);

        if (block.Terminator is null)
            block.Terminator = new Ret(result);

        // ── Step 2: Transform passes on the Module ──
        // Each step is a pure Module → Module function.
        // Steps are ordered; backend-specific passes
        // (e.g. RingAnalysis) are added later by the backend.
        if (EnableSsa)         SsaTransform.Run(module);
        if (EnableConstFolding) ConstantFolding.Run(module);
        if (EnableInlining)    InlinePass.Run(module);

        // ── Step 3: Stash Module for backend retrieval ──
        context.SetMetadata(node, new ModuleMetadata(module));
    }
}
```

**Design rationale for the single-pass approach:**

| Concern | How it's addressed |
|---------|-------------------|
| **Step ordering** | Steps are called in order within `Analyze()`. No external coordination needed. |
| **Per-step control** | Boolean toggles (`EnableSsa`) allow tests to skip individual steps without modifying the pipeline. |
| **Backend-specific steps** | `GenerationPass` runs the *canonical* passes. A backend that needs extra steps (e.g. `RingAnalysis` for the VM) either adds them as a separate `INodeAnalyzer` that runs *after* `GenerationPass`, or calls them on the `Module` in a wrapper. Only backend-specific passes need this treatment — the core passes (SSA, const-fold, inline) are universal. |
| **Telemetry** | The analyzer's built-in telemetry treats `GenerationPass` as one entry. For more granular timing, the pass itself can emit `AnalysisTelemetry` events per step. This is a future concern — the current telemetry granularity is "pass-level" and that's sufficient. |
| **Testing** | Each step (`SsaTransform.Run`, `ConstantFolding.Run`, `InlinePass.Run`) is a public static method on its class. Tests call them directly without any `INodeAnalyzer` infrastructure. |

**Separate analyzers vs. single pass — when to revisit this decision:**

If a future backend needs a substantially different pass order (e.g. a backend that inlines before constant folding), the canonical steps should be extracted into an `IrPassManager`:

```csharp
// Future — only when a second backend demands a different order
public sealed class IrPassManager {
    private readonly List<IIrPass> _passes = new();
    public IrPassManager Add(IIrPass pass) { _passes.Add(pass); return this; }
    public Module Run(Module module) {
        foreach (var pass in _passes) pass.Transform(module);
        return module;
    }
}
```

Until that need is concrete, the single-pass approach keeps things simpler.

### 4.5 Full pipeline

```
AST → [TypeResolve] → [ConstFold] → [SideEffect] → [GenerationPass] → Module
                                                              │
                                         node.Emit(ctx) per node (step 1)
                                               SsaTransform (step 2)
                                          ConstantFolding (step 2)
                                              InlinePass (step 2)
```

### 4.6 Notable `Emit` implementations

| Node | Emit logic |
|------|-----------|
| `Add`, `Sub`, … | Emit `BinOp` with left/right children |
| `Constant` | Emit `Const`; non-numeric values go to `Module.HeapConstants` |
| `Variable` | Emit `LoadLocal` (slot from scope) |
| `Assignment` | Emit `StoreLocal` + reload for expression value |
| `IfStatement` | Emit `CondBranch` to then/else blocks, `Phi` at merge |
| `WhileLoop` | Create header/body/latch/exit blocks, `CondBranch` from header |
| `ForLoop` | Emit initializer, create cond/body/increment/exit blocks |
| `Lambda` | Create a child scope, emit body, `Ret` |
| `Invoke` | Emit `Call` (external or closure dispatch) |
| `Block` | Emit each child; insert `StoreLocal` for block-local variables |

## 5. IR Passes

IR passes are `Module → Module` transforms. They are not separate `INodeAnalyzer` implementations — they are called as internal steps within `GenerationPass.Analyze()`. This keeps the analyzer pipeline focused on AST-level analysis while IR transforms are pure functions on `Module`.

The canonical pass order:

```
[IrEmission]  node.Emit(ctx) per AST node  → Module
     │
     ▼
[SsaTransform]       StoreLocal/LoadLocal → explicit Phi + SSA values
     │
     ▼
[ConstantFolding]    BinOp(Const, Const) → Const
     │
     ▼
[Inlining]           Call(inlinable) → inline body
```

All passes run in `GenerationPass.Analyze()` (section 4.4). Backend-specific passes (e.g. `RingAnalysis` for the VM backend) run later, outside `GenerationPass`, as separate `INodeAnalyzer` instances or adapter steps.

### 5.1 SSA Construction Pass

The SSA pass is the most algorithmically significant transform in the pipeline. It takes the initial IR (which uses `LoadLocal`/`StoreLocal` for mutable slots) and converts it to pure SSA form where every `Value` has exactly one definition and an arbitrary number of uses.

#### 5.1.1 Prerequisites

The `Module` must be in a well-formed state before SSA runs:

1. **Every `BasicBlock` has a terminator** — `Goto`, `CondBranch`, `Ret`, or `Throw`. Dead-end blocks (no terminator) are rejected.
2. **All local slot accesses are explicit** — `LoadLocal(slot)` and `StoreLocal(slot, val)`. The slot indices are dense per `Module.MaxLocalSlots`.
3. **No implicit dataflow** — all value dependencies are through `Value` references. The only mutable state is local variables via `LoadLocal`/`StoreLocal`.

The pass runs **immediately after IR emission**, before constant folding and inlining. The canonical pass order is `IrEmission → SsaTransform → ConstantFolding → Inlining` (see section 5). SSA runs first so the subsequent optimization passes can rely on explicit def-use chains and `Phi` nodes rather than walking mutable slot writes. Constant folding and inlining then operate on SSA form, producing equivalent but smaller SSA for downstream consumers.

#### 5.1.2 Algorithm overview (Cytron et al., 1991)

The standard SSA construction proceeds in four steps:

```
Step 1: CFG construction        — build the control-flow graph from block terminators
Step 2: Dominator tree          — compute immediate dominators (Lengauer-Tarjan)
Step 3: Dominance frontiers     — compute DF(b) for every block
Step 4: Phi insertion           — insert Phi at dominance frontiers for each local slot
Step 5: Renaming               — replace LoadLocal/StoreLocal with SSA Value references
```

#### 5.1.3 Step 1: CFG construction

Because terminators reference `BasicBlock` objects directly, CFG construction is a single pass:

```csharp
public sealed record Cfg(
    BasicBlock Entry,
    Dictionary<BasicBlock, List<BasicBlock>> Predecessors,
    Dictionary<BasicBlock, List<BasicBlock>> Successors
);

public static Cfg BuildCfg(Module module) {
    var preds = new Dictionary<BasicBlock, List<BasicBlock>>();
    var succs = new Dictionary<BasicBlock, List<BasicBlock>>();

    // Ensure every block has an entry in both maps
    foreach (var block in module.Blocks) {
        preds[block] = new List<BasicBlock>();
        succs[block] = new List<BasicBlock>();
    }

    foreach (var block in module.Blocks) {
        switch (block.Terminator) {
            case Goto g:
                succs[block].Add(g.Target);
                preds[g.Target].Add(block);
                break;
            case CondBranch cb:
                succs[block].Add(cb.ThenTarget);
                succs[block].Add(cb.ElseTarget);
                preds[cb.ThenTarget].Add(block);
                preds[cb.ElseTarget].Add(block);
                break;
            case Ret or Throw:
                // No successors — terminal block
                break;
        }
    }

    return new Cfg(module.Blocks[0], preds, succs);
}
```

The result maps every block to its predecessors; successors are derivable from the terminator at any time but cached here for convenience.

**Edges from `CondBranch` are always resolved here. There is no label indirection.** This is the fundamental improvement over the current `Lowering.Assemble()` which resolves labels via integer IDs and `labelPositions` dictionaries. Here the target is a `BasicBlock` reference — it cannot dangle, and the CFG is always consistent with the IR.

#### 5.1.4 Step 2: Dominator tree (Lengauer-Tarjan)

Lengauer-Tarjan finds immediate dominators in near-linear time (`O(E α(V))`). The standard implementation is ~60 lines:

```csharp
public static Dictionary<BasicBlock, BasicBlock> ComputeDominators(Cfg cfg) {
    // ── DFS numbering ──
    var semi = new Dictionary<BasicBlock, int>();
    var parent = new Dictionary<BasicBlock, BasicBlock>();
    var vertex = new List<BasicBlock>();
    var bucket = new Dictionary<BasicBlock, List<BasicBlock>>();
    var idom = new Dictionary<BasicBlock, BasicBlock>();

    int counter = 0;
    void Dfs(BasicBlock b, BasicBlock? p) {
        semi[b] = counter++;
        parent[b] = p!;
        vertex.Add(b);
        foreach (var s in cfg.Successors[b])
            if (!semi.ContainsKey(s))
                Dfs(s, b);
    }
    Dfs(cfg.Entry, null);

    // ── Semi-dominator computation (compress/link) ──
    var ancestor = new Dictionary<BasicBlock, BasicBlock>();
    var best = new Dictionary<BasicBlock, BasicBlock>();
    foreach (var b in vertex) { ancestor[b] = b; best[b] = b; }

    BasicBlock Compress(BasicBlock v) { /* path compression */ }
    BasicBlock Eval(BasicBlock v) { /* return vertex with minimal semi */ }
    void Link(BasicBlock v, BasicBlock w) { ancestor[w] = v; }

    for (int i = vertex.Count - 1; i > 0; i--) {
        var w = vertex[i];
        foreach (var p in cfg.Predecessors[w]) {
            var u = Eval(p);
            if (semi[u] < semi[w])
                semi[w] = semi[u];  // using int field, repurpose for candidate
        }
        bucket[semi[w]] ??= new List<BasicBlock>();
        bucket[semi[w]].Add(w);
        if (parent[w] is { } p) {
            Link(p, w);
            foreach (var v in bucket.GetValueOrDefault(semi[p], [])) {
                var u = Eval(v);
                idom[v] = semi[u] < semi[p] ? u : p;
            }
            bucket[semi[p]]?.Clear();
        }
    }
    for (int i = 1; i < vertex.Count; i++) {
        var w = vertex[i];
        if (idom[w] != parent[w])
            idom[w] = idom[idom[w]];
    }
    idom[cfg.Entry] = cfg.Entry;

    return idom;  // idom[b] = immediate dominator of b
}
```

The output `idom` map gives the immediate dominator tree: `b`'s parent in the dominator tree is `idom[b]`. The entry block dominates itself by convention and has no parent.

#### 5.1.5 Step 3: Dominance frontiers

The dominance frontier `DF(b)` is the set of blocks `x` such that:
- `b` dominates a predecessor of `x`, and
- `b` does not strictly dominate `x`.

```csharp
public static Dictionary<BasicBlock, List<BasicBlock>> ComputeFrontiers(Cfg cfg, Dictionary<BasicBlock, BasicBlock> idom) {
    var frontiers = new Dictionary<BasicBlock, List<BasicBlock>>();
    foreach (var b in cfg.Successors.Keys)
        frontiers[b] = new List<BasicBlock>();

    bool Dominates(BasicBlock a, BasicBlock b) {
        // Walk up the dominator tree
        while (b != a && idom[b] != b) b = idom[b];
        return b == a;
    }

    foreach (var b in cfg.Successors.Keys) {
        if (cfg.Predecessors[b].Count < 2) continue;
        foreach (var p in cfg.Predecessors[b]) {
            var runner = p;
            while (runner != idom[b]) {
                frontiers[runner].Add(b);
                runner = idom[runner];
            }
        }
    }
    return frontiers;
}
```

Each entry `frontiers[b]` is the set of blocks where `b`'s dominance ends — where control flow from outside `b`'s domain converges. These are exactly the positions where `Phi` instructions are needed.

#### 5.1.6 Step 4: Phi insertion

For each mutable local slot, we find every block that writes to it (`StoreLocal(slot, _)`) and insert `Phi(slot)` at the dominance frontiers of those blocks.

```csharp
public static void InsertPhis(Module module, Cfg cfg, Dictionary<BasicBlock, List<BasicBlock>> frontiers) {
    // Discover which slots are defined in which blocks
    var defSites = new Dictionary<int, List<BasicBlock>>();  // slot → list of blocks
    for (int bi = 0; bi < module.Blocks.Count; bi++) {
        var block = module.Blocks[bi];
        foreach (var instr in block.Instructions) {
            if (instr is StoreLocal sl) {
                defSites.GetOrAdd(sl.SlotIndex, _ => new()).Add(block);
            }
        }
    }

    // For each slot with multiple definitions, insert Phi at frontier blocks
    foreach (var (slot, defBlocks) in defSites) {
        if (defBlocks.Count < 2) continue;  // single-def slot needs no phi

        var worklist = new Queue<BasicBlock>(defBlocks);
        var visited = new HashSet<BasicBlock>();
        var inserted = new HashSet<BasicBlock>();  // prevent double-phi per slot

        while (worklist.Count > 0) {
            var b = worklist.Dequeue();
            foreach (var f in frontiers[b]) {
                if (!inserted.Add(f)) continue;

                // Insert Phi at the *start* of the frontier block
                // (before any non-Phi instructions)
                var phiInstrs = f.Instructions.TakeWhile(i => i is Phi).Count();
                var incoming = cfg.Predecessors[f]
                    .Select(p => (Value?)null)    // placeholder; filled during renaming
                    .ToArray();
                f.Instructions.Insert(phiInstrs, new Phi(slot, incoming, null));

                if (visited.Add(f))
                    worklist.Enqueue(f);
            }
        }
    }
}
```

After this pass, every join point that merges multiple definitions of a local variable has an explicit `Phi` with placeholder incoming values. The number of operands equals the number of predecessor blocks.

#### 5.1.7 Step 5: Renaming

The renaming pass walks the dominator tree in DFS order, maintaining a stack of current SSA values per slot. Every `LoadLocal(slot)` is replaced with the top-of-stack value; every `StoreLocal(slot, val)` defines a new SSA version and pushes it.

```csharp
public static void Rename(Module module, Cfg cfg, Dictionary<BasicBlock, BasicBlock> idom) {
    // ── Per-slot value stack ──
    var stacks = new Dictionary<int, Stack<Value>>();
    Value? CurrentValue(int slot) =>
        stacks.TryGetValue(slot, out var s) && s.Count > 0 ? s.Peek() : null;

    // ── Step 0: Seed parameter bindings from the entry block ──
    // `Parameter(slot, kind)` defines the initial SSA value for a function
    // argument. We register it before walking so the first LoadLocal(slot)
    // in the entry block resolves to the parameter's Value — and so the
    // parameter's value is available to fill successor Phi incomings.
    // We then *remove* the Parameter instruction from the entry block: it
    // has served its purpose (defining %0) and backends do not emit µops
    // for it; the VM loads parameters directly from the frame.
    foreach (var instr in cfg.Entry.Instructions) {
        if (instr is Parameter p) {
            var val = new Value(p, p.Kind, 0);
            stacks.GetOrAdd(p.SlotIndex, _ => new()).Push(val);
        }
    }
    cfg.Entry.Instructions.RemoveAll(i => i is Parameter);

    void Walk(BasicBlock block) {
        // Save stack heights for restoration on backtrack
        var savedHeights = stacks.ToDictionary(kv => kv.Key, kv => kv.Value.Count);

        // Process Phi instructions: assign new SSA values for *slot-level* Phis.
        // Value-level Phis (SlotIndex=null, emitted by IfStatement/WhileLoop)
        // are left untouched; they already carry their incoming values from emission.
        foreach (var instr in block.Instructions) {
            if (instr is Phi phi && phi.SlotIndex is { } slotIdx) {
                var newVal = new Value(phi, phi.Incoming[0]?.Kind ?? TypeKind.Int64, 0);
                // Incoming values are filled when each predecessor finishes its Walk
                stacks.GetOrAdd(slotIdx, _ => new()).Push(newVal);
            }
        }

        // Process non-Phi instructions: replace LoadLocal, handle StoreLocal
        var replacements = new List<(int Index, Instr Replacement)>();
        for (int i = 0; i < block.Instructions.Count; i++) {
            switch (block.Instructions[i]) {
                case LoadLocal ll:
                    var cur = CurrentValue(ll.SlotIndex);
                    if (cur is not null) {
                        // LoadLocal becomes a "use" — replace with current value.
                        // We remove the instruction entirely; consumers reference
                        // cur directly instead of through this instruction.
                        replacements.Add((i, null));  // mark for removal
                    }
                    break;

                case StoreLocal sl:
                    stacks.GetOrAdd(sl.SlotIndex, _ => new()).Push(sl.Val);
                    replacements.Add((i, null));  // StoreLocal removed; side effect is now the SSA stack
                    break;

                case Phi:
                    // Slot-level Phis already handled above;
                    // value-level Phis (SlotIndex=null) are left untouched.
                    break;
            }
        }

        // Apply replacements: remove LoadLocal/StoreLocal, leave Phi
        foreach (var (idx, replacement) in replacements.OrderByDescending(r => r.Index)) {
            if (replacement is null)
                block.Instructions.RemoveAt(idx);
            else
                block.Instructions[idx] = replacement;
        }

        // Recurse into dominator-tree children (blocks dominated by this one)
        foreach (var child in module.Blocks) {
            if (idom.GetValueOrDefault(child) == block && child != block)
                Walk(child);
        }

        // Fill Phi incoming values for successor blocks
        // At this point the stacks hold the current SSA value for each slot
        foreach (var succ in cfg.Successors[block]) {
            foreach (var instr in succ.Instructions) {
                if (instr is Phi phi && phi.SlotIndex is { } slotIdx && phi.Incoming.Length > 0) {
                    var predIndex = cfg.Predecessors[succ].IndexOf(block);
                    if (predIndex >= 0)
                        phi.Incoming[predIndex] = CurrentValue(slotIdx)!;
                }
            }
        }

        // Restore stacks
        foreach (var (slot, height) in savedHeights) {
            var s = stacks[slot];
            while (s.Count > height) s.Pop();
        }
    }

    Walk(cfg.Entry);
}
```

> **Implementation note: value-rewriting.** When a `LoadLocal` is removed above, other instructions that held a `Value` referencing that `LoadLocal`'s definition must be updated to reference the current SSA value from the rename stack (the `cur` in `CurrentValue`). This requires either:
> - **Use-lists**: maintain a `Dictionary<Value, List<(Instr, int operandIndex)>>` side-table on `Module` mapping each `Value` to every instruction+operand-index that references it. After `LoadLocal` removal, walk the use-list and rewrite each consumer's operand to the replacement `Value`. (Recommended for the first implementation.)
> - **Identity instructions**: keep `LoadLocal` as a "mov" identity that simply passes through the current SSA value. A later copy-propagation pass (`BinOp(Identity, x)` → `BinOp(x)` folds them away. Simpler to implement but leaves noise in the IR that downstream passes must skip. (Acceptable fallback.)
>
> The pseudocode above shows the *intent* (remove LoadLocal, consumers reference the renamed Value directly); the use-list machinery is ≈30 lines of code in practice and does not change the algorithm's structure.

After renaming:
- `LoadLocal` instructions are **removed**. All uses of that local now reference the appropriate SSA `Value` directly.
- `StoreLocal` instructions are **removed**. The stored `Value` is now the defining instruction for the new SSA version.
- **Slot-level** `Phi` instructions (`SlotIndex != null`, inserted by `InsertPhis`) remain and produce a new `Value` for the renamed slot. Their `Incoming` arrays are filled inline during `Walk`.
- **Value-level** `Phi` instructions (`SlotIndex == null`, emitted by `IfStatement.Emit` / `WhileLoop.Emit`) are **untouched** by the SSA pass; their `Incoming` values were filled at emission time and remain valid because the instructions those Values reference (`BinOp`, etc.) are not removed.
- The `Module.MaxLocalSlots` can be reset to 0 — no mutable slots remain.

`Phi.Incoming` arrays for slot-level Phis are filled **during** the rename walk. When `Walk(block)` finishes processing a block (including its dominated children), it iterates over each successor block and, for each slot-level Phi in that successor, sets the incoming value corresponding to the `block` predecessor. This works because:
1. The `stacks` hold the current SSA value for every slot at the block's exit point.
2. The successor's predecessor list includes the current block, giving the correct index into `Phi.Incoming`.
3. The dominator-tree DFS traversal ensures that when a join block is visited, all its predecessors have already filled their contributions (since a join block's predecessors are visited before the join in DFS order over the dominator tree).

#### 5.1.8 Tracing through the worked example

Starting from the pre-SSA IR in section 7.2 (note the `Parameter` instruction in the entry block):

```
entry block:
  %0 = Parameter(slot=0, Int64)       // function argument x
  %1 = LoadLocal(slot=0)
  %2 = Const(0)
  %3 = BinOp(Gt, %1, %2)
  CondBranch(%3, then, else)

then_block:
  %4 = LoadLocal(slot=0)
  %5 = Const(1)
  %6 = BinOp(Add, %4, %5)
  Goto(merge)

else_block:
  %7 = LoadLocal(slot=0)
  %8 = Const(1)
  %9 = BinOp(Sub, %7, %8)
  Goto(merge)

merge_block:
  %10 = Ret(?)                        // placeholder; no result yet
```

**CFG:** entry → {then, else} → merge. Predecessors: then={entry}, else={entry}, merge={then, else}.

**Dominator tree:** entry dominates all blocks. idom[then]=entry, idom[else]=entry, idom[merge]=entry.

**Dominance frontiers:** DF(entry) = {merge}, DF(then) = {merge}, DF(else) = {merge}.

**Phi insertion:** slot 0 is *loaded* in then/else but never *stored* anywhere (x is read-only in this example). A `Phi` is therefore **not** inserted at merge — `InsertPhis` only triggers on slots with 2+ `StoreLocal` def sites. In the more general case where x is assigned in both branches, a `Phi(slot=0)` would be inserted at the start of `merge_block`. We'll trace that variant below by assuming each branch assigns x.

For the example as written (no stores), the rename walk proceeds:

**Step 0 (parameter seeding):** Walk the entry block. `Parameter(slot=0, Int64)` defines `%0`. Push `%0` onto `stacks[0]`. Remove the `Parameter` instruction from the entry block. `stacks[0] = [%0]`.

**Step 5 (rename walk, DFS over dominator tree: entry → then → else → merge):**

- **entry:** The `LoadLocal(slot=0)` at index 0 resolves to `CurrentValue(0) = %0` (from the seeded stack). It's marked for removal; all uses (the `BinOp(Gt)`) now reference `%0` directly. Exits with `stacks[0] = [%0]`.
- **then:** `LoadLocal(slot=0)` resolves to `%0`. Marked for removal. `BinOp(Add)` references `%0`. Exits with `stacks[0] = [%0]`.
- **else:** Same as then. `BinOp(Sub)` references `%0`. Exits with `stacks[0] = [%0]`.
- **merge:** No *slot-level* Phi is inserted by `InsertPhis` (slot 0 has no stores). The *value-level* `Phi(null, [%6, %9])` emitted by `IfStatement.Emit()` (see section 7.2) is untouched by the SSA pass — it already carries the correct incoming values (`%6` from then, `%9` from else) and those Values (`BinOp(Add)`, `BinOp(Sub)`) are not removed during renaming.

For the **store-bearing variant** (`if (x > 0) x = x+1 else x = x-1`), slot 0 is stored in then and else, so a `Phi(slot=0)` is inserted at merge. The rename walk produces:

```
entry block:
  %0 = BinOp(Gt, %p0, %c0)        ; Parameter seeded as %p0; LoadLocal eliminated
  CondBranch(%0, then_block, else_block)
  ; %p0 is the function argument, referenced directly in all uses below

then_block:
  %1 = Const(1)
  %2 = BinOp(Add, %p0, %1)        ; LoadLocal eliminated — uses %p0 directly
  ; (StoreLocal(slot=0, %2) would be removed; %2 becomes the new stack top)
  Goto(merge)

else_block:
  %3 = Const(1)
  %4 = BinOp(Sub, %p0, %3)        ; LoadLocal eliminated — uses %p0 directly
  ; (StoreLocal(slot=0, %4) would be removed; %4 becomes the new stack top)
  Goto(merge)

merge_block:
  %5 = Phi(0, [%2, %4])           ; slot-level Phi (SlotIndex=0) filled inline during rename walk
  Ret(%5)
```

Note the parameter `%p0` flows into the condition and both branch bodies — the LoadLocal indirection is gone. The Phi at merge selects the value stored in the then-branch vs. the else-branch, which the back-end `UopLoweringVisitor` resolves via `PhiMarker` + `ConsumedFromPcs`.

#### 5.1.9 Edge cases

| Edge case | Handling |
|-----------|----------|
| **Unreachable blocks** | Blocks not reachable from `cfg.Entry` via the successor walk are skipped. They are preserved in the `Module` (for diagnostics) but produce no SSA values. A separate `DeadBlockEliminationPass` can remove them. |
| **Single-definition slots** | Slots with exactly one `StoreLocal` need no `Phi`. The single definition dominates all uses (by SSA property). |
| **Phi with identical incoming values** | When all incoming values are the same `Phi` is redundant. A `TrivialPhiEliminationPass` can fold `%7 = Phi([%0, %0])` → `%0`. |
| **Critical edges** | An edge from a block with multiple successors to a block with multiple predecessors "critical." The Phi insertion algorithm handles this naturally — each predecessor pair contributes one incoming value. No edge splitting is required for correctness, though edge splitting may improve optimization. |
| **Uninitialized slots** | If a `LoadLocal(slot)` is reached without a preceding `StoreLocal`, the renaming pass finds an empty stack. In this case, `CurrentValue` returns `null`, and the `LoadLocal` remains — it's treated as reading an undefined value. A `UndefinedVariableEliminationPass` can replace these with `Const(0)` or error diagnostics. |
| **Back-edges (loops)** | The dominance frontier of a loop header includes the header itself (because the back-edge predecessor is dominated by the header but the header is not). This causes a `Phi` to be inserted at the header, which is the standard loop-variant SSA pattern. The renaming pass handles this naturally: when visiting the header, the Phi's placeholder value is pushed; after visiting the loop body, the back-edge propagates the loop-carried value. |
| **Exception-handling edges** | `TryCatchFinally` / `TryBody` nodes generate implicit control-flow edges: any instruction in a `try` body can transfer control to a `catch` or `finally` block. These edges must be added to the CFG **before** SSA construction (otherwise phi placement will miss definitions reaching catch blocks). The `Emit` for `TryCatchFinally` should create these edges explicitly as additional successor relationships on each instruction block within the try body, or add them as a pre-pass over the `Module` before `SsaTransform.Run()`. |

#### 5.1.10 Testing strategy for the SSA pass

```csharp
[Test]
public async Task Ssa_StraightLine_NoPhis() {
    var module = Emit(new Block([
        new Assignment(new Variable("x"), new Constant(1)),
        new Variable("x")
    ]));
    // One block, one slot, one store before the load — no join point
    SsaTransform.Run(module);
    await Assert.That(module.Blocks[0].Instructions.OfType<Phi>()).IsEmpty();
    await Assert.That(module.Blocks[0].Instructions.OfType<LoadLocal>()).IsEmpty();
}

[Test]
public async Task Ssa_IfElse_PhiAtMerge() {
    // if (cond) x = 1 else x = 2; return x
    var ast = /* ... */;
    var module = Emit(ast);
    SsaTransform.Run(module);
    var merge = module.Blocks.Last();
    var phis = merge.Instructions.OfType<Phi>().ToArray();
    await Assert.That(phis).HasCount().EqualTo(1);
    await Assert.That(phis[0].Incoming).HasCount().EqualTo(2);
    // Incoming[0] = Const(1) from then_block
    // Incoming[1] = Const(2) from else_block
}

[Test]
public async Task Ssa_Loop_PhiAtHeader() {
    // while (x < 10) x = x + 1; return x
    var ast = /* ... */;
    var module = Emit(ast);
    SsaTransform.Run(module);
    var header = module.Blocks[1];  // loop header
    await Assert.That(header.Instructions.OfType<Phi>()).HasCount().EqualTo(1);
}

[Test]
public async Task Ssa_CrossValidateWithVm() {
    // Compile with and without SSA, execute both, compare results
    var ast = /* complex expression with branches and loops */;
    var module = Emit(ast);
    SsaTransform.Run(module);

    var lowered = new UopLoweringVisitor().Lower(module);
    var program = ProgramCompiler.Compile(lowered);
    var state = new VmState(program);
    Vm.Execute(state);
    var ssaResult = state.Stack.Pop();

    // Compare against non-SSA pipeline (old or new without SSA)
    // ...
    await Assert.That(ssaResult).IsEqualTo(expectedResult);
}
```

#### 5.1.11 Performance characteristics

| Concern | Mitigation |
|---------|-----------|
| Lengauer-Tarjan complexity | O(E α(V)) — near-linear. For typical control flow (V ~ block count), this is faster than a sort. |
| Phi insertion worklist | Each block is visited at most once per local variable. Worst case: O(S × B) where S = local slots and B = blocks. |
| Renaming walk | Single DFS over the dominator tree. Each instruction is visited once. |
| Phi incoming filling (inline) | Each block iterates its successors' Phi instructions once. Linear in total Phi operands. Typically small. |
| Overall pass cost | Expected 1.5–3× the cost of a single tree walk, depending on CFG complexity. For a 100-block module, well under 1ms. |

The SSA pass is designed to be **optional** for the VM backend (the `UopLoweringVisitor` can work with or without SSA, since `LoadLocal`/`StoreLocal` map directly to µop `LoadSlot`/`StoreSlot`). It becomes essential for passes that need def-use chains: constant propagation, dead code elimination, inlining, and vectorization.

### 5.2 Constant Folding Pass

Walks each block's instructions; for `BinOp` where both operands are `Const`, computes the result and replaces the instruction with a new `Const`. Propagates the new `Value` to all users (requires a use-list on `Value`, stored in `Module` as a side table).

### 5.3 Inline Pass

Heuristic: inline `Call` targets whose callee module has ≤N instructions. Inlines into the caller block, handling argument mapping and return-value φ.

### 5.4 Ring Analysis Pass (VM-backend-specific)

Takes the SSA IR and computes eval-stack ring depths for each block+instruction. Attaches `RingMetadata` as a side table on `Module`. This pass exists only for the VM backend; other backends (C# source) ignore it.

## 6. Backends (IR → Output)

Backends are **visitors**, never a monolithic `IBackend.Compile()`.

```csharp
public abstract class Visitor {
    protected Module Module { get; }

    public virtual Value? VisitBlock(BasicBlock block, int index);
    public abstract void VisitInstr(int blockIdx, int instrIdx, Instr instr);
    public abstract void VisitTerminator(int blockIdx, Terminator term);
}

public sealed class UopLoweringVisitor : Visitor {
    public LoweringResult Lower(Module module) { /* visit blocks → emit µops */ }
}

public sealed class ExpressionTreeVisitor : Visitor {
    public LambdaExpression Compile(Module module) { /* emit Expression trees */ }
}

public sealed class CSharpSourceVisitor : Visitor {
    public string Generate(Module module) { /* emit C# text */ }
}
```

### 6.1 VM Backend (`UopLoweringVisitor`)

This is the direct replacement for the current `Lowering.Lower()` + `ProgramCompiler.Compile()`:

1. **Block ordering**: topologically sort blocks (dominator-tree order). Assign each block a contiguous range of µop PCs.
2. **Instruction emission**: for each `Instr`, emit the corresponding µop (`BinOp` → `BinOp`, `Phi` → `PhiMarker` + resolved `ConsumedFromPcs` patch, `AllocHeap` → `LoadHeapConst`, etc.).
3. **Ring analysis**: run `RingAnalysisPass` to compute `ConsumedFromPcs` and `PhiSourcePcs`/`PhiAltPcs`. Since φ is already explicit in the IR, the ring analysis is simpler than today's heuristic.
4. **Label resolution**: block-relative offsets become absolute µop PCs.
5. Produces a `LoweringResult` that feeds into the existing `ProgramCompiler`.

Result**: the existing VM pipeline (ring allocation, expression tree compilation, `VmProgram`) is preserved without modification. Only the input changes.

### 6.2 Expression Tree Backend (`ExpressionTreeVisitor`)

Replaces `LinqExpressionGenerator` in the long term (but does not need to replace it during migration). Walks the IR and emits `System.Linq.Expressions.Expression`:

- `Const` → `Expression.Constant(value)`
- `BinOp` → `Expression.Add(l, r)` etc.
- `Phi` → resolves to the incoming value selected by the predecessor path (or a conditional expression for ternary merges).
- `Goto` → `Expression.Goto(label)`
- `CondBranch` → `Expression.IfThenElse(cond, then, else)`
- `Ret` → `Expression.Return(label, value)`

### 6.3 C# Source Backend (`CSharpSourceVisitor`)

Emits readable C# source files. Maps `TypeKind` to CLR types, `OpKind` to operators, blocks to labeled statements with `goto`. Useful for debugging, auditing, or ahead-of-time compilation for environments that don't support expression trees.

## 7. Worked Example

Expression: `if (x > 0) x + 1 else x - 1`

### 7.1 AST (simplified)

```
IfStatement {
  Condition = GreaterThan(Variable("x"), Constant(0)),
  Then = Add(Variable("x"), Constant(1)),
  Else = Subtract(Variable("x"), Constant(1))
}
```

### 7.2 IR after emission (before SSA)

```
entry block:
  %0 = Parameter(slot=0, Int64)       // function argument x
  %1 = LoadLocal(slot=0)
  %2 = Const(0)
  %3 = BinOp(Gt, %1, %2)
  CondBranch(%3, then_block, else_block)

then_block:
  %4 = LoadLocal(slot=0)
  %5 = Const(1)
  %6 = BinOp(Add, %4, %5)
  Goto(merge_block)

else_block:
  %7 = LoadLocal(slot=0)
  %8 = Const(1)
  %9 = BinOp(Sub, %7, %8)
  Goto(merge_block)

merge_block:
  ; φ selects %6 (from then) or %9 (from else)
  ; This is a value-level Phi (SlotIndex=null) emitted by IfStatement.Emit().
  %10 = Phi(null, [%6, %9])
  %11 = Ret(%10)
```

### 7.3 IR after SSA pass

```
entry block:
  %0 = BinOp(Gt, %p0, %c0)       ; Parameter %p0 was seeded and removed; %c0 = Const(0)
  CondBranch(%0, then_block, else_block)
  ; %p0 is the function-argument value, referenced directly in uses below

then_block:
  %1 = Const(1)
  %2 = BinOp(Add, %p0, %1)       ; LoadLocal eliminated — uses %p0 directly
  Goto(merge_block)

else_block:
  %3 = Const(1)
  %4 = BinOp(Sub, %p0, %3)       ; LoadLocal eliminated — uses %p0 directly
  Goto(merge_block)

merge_block:
  %5 = Phi(null, [%2, %4])       ; value-level Phi from emission — Incoming filled at emission time
  Ret(%5)
```

Note**: `%p0` flows directly into all three blocks because the parameter-seeding step (Step 0 of `Rename`) defined it from the entry-block `Parameter` instruction and pushed it onto the rename stack before the walk began.

### 7.4 VM µop output (after `UopLoweringVisitor`, SSA disabled)

The `UopLoweringVisitor` maps `LoadLocal → LoadSlot`, `Const → LoadConst`, `BinOp → BinOp`, etc. With SSA disabled (the default for the VM backend), `LoadLocal` instructions survive the pipeline and produce µop `LoadSlot`:

```
pc0:  LoadSlot(0)           ; load x        ; entry block
pc1:  LoadConst(0)
pc2:  BinOp(Gt)
pc3:  BranchIfFalse(pc8)                   ; → else_block
pc4:  LoadSlot(0)           ; then_block
pc5:  LoadConst(1)
pc6:  BinOp(Add)
pc7:  Jump(pc12)                           ; → merge_block
pc8:  LoadSlot(0)           ; else_block
pc9:  LoadConst(1)
pc10: BinOp(Sub)
pc11: Jump(pc12)                           ; → merge_block
pc12: PhiMarker([pc6], pc7) ; merge_block  ; φ helper (sets ConsumedFromPcs on next pop)
pc13: ReturnOp
```

### 7.5 Ring-allocated expression tree (via `ProgramCompiler`)

Identical to what the current pipeline produces—proving the IR is a drop-in replacement.

### 7.6 C# Source output

```csharp
long entry(long x) {
    long _t0, _t1, _t2, _t3;
    _t0 = x > 0 ? 1L : 0L;
    if (_t0 == 0L) goto else_block;
    _t1 = x + 1L;
    goto merge_block;
else_block:
    _t2 = x - 1L;
    goto merge_block;
merge_block:
    _t3 = phi(_t1, _t2); // resolved at codegen: _t0 ? _t1 : _t2
    return _t3;
}
```

## 8. Closure Handling

Closures are represented in the IR as follows:

- Each lambda closure carries a `captures` list (`Value[]`).
- `AllocClosure(funcIndex, captures)` allocates a closure object on the heap with the capture array.
- `LoadUpvalue(idx)` reads a capture from the enclosing scope's closure. This is only valid inside a lambda body.
- The SSA pass does **not** eliminate captures—they remain as explicit upvalue operations.

At the VM backend, these map to the existing:
- `AllocClosure` µop + `Closure` class (`Poly/Interpretation/Vm/Closure.cs`)
- `LoadCapture`/`StoreCapture` µops (which read from `Closure.Captures`)
- `HandleAllocClosure`/`HandleLoadUpvalue`/`HandleStoreUpvalue` in `Vm.cs`

For the Expression tree backend, closures map to `Expression.Lambda` with captured variable scoping (the existing `LinqExpressionGenerator.CompileLambda` pattern).

## 9. Heap Constants & External Call Sites

### 9.1 Heap constants

Emitted as `AllocHeap(handleIndex)` instructions. During module construction, the emitter registers non-numeric constants (strings, CLR objects) in `Module.HeapConstants` at a known index. `AllocHeap` references that index:

```csharp
// During emission:
int handle = ctx.Module.HeapConstants.Count;
ctx.Module.HeapConstants.Add("Alice");
ctx.Emit(new AllocHeap(handle, node.Id));
```

The `UopLoweringVisitor` maps `AllocHeap` to `LoadHeapConst` µops.

### 9.2 External call sites

`Call` with `IsExternal = true` carries a `CallSiteIndex` into a module-level call site table (analogous to `VmProgram.CallSites`). The call site table is resolved during IR generation from `AnalysisResult.GetResolvedMember()`.

## 10. Incremental Compilation

**Deferred.** `GenerationPass` re-runs end-to-end per function on every analysis cycle. The `Analyzer`'s existing incremental analysis API (`Analyzer.Analyze(Node root, AnalysisResult priorAnalysis, IEnumerable<Node> invalidatedNodes)`) still skips AST-level analysis passes for unchanged subtrees, but once the IR for a function is invalidated, the entire function's `Module` is rebuilt and SSA / const-fold / inline passes re-run from scratch.

Reasons to defer incremental SSA:

- SSA construction is a **whole-function** property (dominator tree, dominance frontiers, phi placement all depend on the entire CFG). Partial SSA on "only changed blocks and their affected successors" requires dominator-tree invalidation and phi-fixup logic that is roughly the complexity of the SSA pass itself, with no clear first consumer.
- The `Module.HeapConstants` table and `SourceNodeId` ranges referenced by the previous draft of this section do not yet exist as fields on `BasicBlock` / `Module`; introducing them now would be speculative.

When profiling shows a function re-compile cost worth optimizing, revisit with a concrete target (e.g. "make a single-expression edit in a >1k-block function compile in <2ms"). Implementation order: (a) add `SourceNodeId` ranges to `BasicBlock`; (b) reuse unchanged `BasicBlock`s when no AST node in the function subtree was invalidated; (c) re-run SSA / const-fold / inline only for functions whose `Module` was rebuilt; (d) consider dominator-tree invalidation only if (c) is still too slow.

## 11. Migration Strategy

### Phase 1: IR types + virtual `Emit` on `Node` base (parallel)

- Add all IR types under `Poly/Interpretation/Ir/`.
- Add virtual `Emit` method to `Poly/Syntax/Node.cs` (base returns `null`).
- Add `EmissionContext` and `GenerationPass` in `Poly/Interpretation/Ir/`, registered via `.UseIrGeneration()`.
- Override `Emit` on `Add`. Write tests that produce IR from a standalone node and verify the IR structure.
- **No existing code changes**. Old `UopGenerationPass` untouched.

### Phase 2: UopLoweringVisitor (adapter)

- Implement `UopLoweringVisitor` that walks a `Module` and produces a `LoweringResult`.
- Test: AST → `Emit` → `UopLoweringVisitor` → `ProgramCompiler.Compile` → `Vm.Execute` → assert result equals old pipeline.
- This proves the IR can drive the existing VM.

### Phase 3: Override `Emit` on remaining nodes (incremental)

Node by node, add `override Value? Emit(EmissionContext ctx)` to each AST type:

1. `Constant`, `Variable`, `Parameter` (trivial)
2. `Assignment`
3. `Block` (manages scope and block merging)
4. `IfStatement` (introduces explicit φ at merge)
5. `WhileLoop`, `DoWhileLoop`, `ForLoop`
6. `Lambda`, `Invoke` (closure handling)
7. `Member`, `IndexAccess`, `New`, `NewArray`
8. `Add`, `Subtract`, `Multiply`, `Divide` … (all operators)
9. `Throw`, `TryCatchFinally`
10. `Await`, `SuspendNode`, `Break`, `Continue`

After each node, run the cross-validation test suite. A failure means the node's `Emit` is wrong — fix before proceeding.

### Phase 4: Flip default

- Change `AnalyzerBuilder` extensions so `.UseUopGeneration()` runs `GenerationPass` + `UopLoweringVisitor`.
- Keep old `UopGenerationPass` behind a compatibility flag.
- Run entire test suite. If green, mark old passes `[Obsolete]`.

### Phase 5: Backend expansion

- Add `ExpressionTreeVisitor` → compare output to `LinqExpressionGenerator` on every expression type.
- Add `CSharpSourceVisitor` → write golden-file tests.
- Add `RingAnalysisPass` (optional, VM-backend-specific).

## 12. Testing Strategy

### Cross-validation tests (mandatory)

For every test case in `Poly.Tests/Interpretation/VmCorrectnessTests.cs`:

```csharp
[Test]
public async Task BinaryAdd_CrossValidate() {
    var node = new Add(new Constant(3), new Constant(4));

    // Old pipeline
    var oldResult = OldPipeline.Execute(node);

    // New pipeline
    var ir = IrPipeline.Emit(node);
    var lowered = new UopLoweringVisitor().Lower(ir);
    var program = ProgramCompiler.Compile(lowered);
    var state = new VmState(program);
    Vm.Execute(state);
    var newResult = state.Stack.Pop();

    await Assert.That(newResult).IsEqualTo(oldResult);
}
```

### IR-level tests

```csharp
[Test]
public async Task IfStatement_ProducesCorrectBlocks() {
    var ast = /* if (x > 0) x + 1 else x - 1 */;
    var module = EmitModule(ast);

    // The IR should have 2 branch terminators and 1 phi
    await Assert.That(module.Blocks.Count).IsEqualTo(4);  // entry, then, else, merge
    var phis = module.Blocks.SelectMany(b => b.Instructions).OfType<Phi>();
    await Assert.That(phis.Count()).IsGreaterThan(0);
}
```

### Benchmark tests

```csharp
[Benchmark]
public void OldPipeline_Compile() { /* old pipeline */ }
[Benchmark]
public void NewPipeline_Compile_NoOpt() { /* new pipeline, no passes */ }
[Benchmark]
public void NewPipeline_Compile_FullOpt() { /* new pipeline, all passes */ }
```

Goal: verify the new pipeline's compile-time overhead is within 2× of the old single-walk approach, with headroom for optimization.

## 13. Performance Considerations

| Concern | Mitigation |
|---------|-----------|
| IR construction = second tree walk | Emitters are invoked during the analysis pass itself (not a second walk). `GenerationPass` runs as the last `INodeAnalyzer` in the pipeline. |
| SSA construction is O(n²) in pathological CFGs | Use standard dominance-frontier algorithm (O(n log n)). The CFG size equals the number of blocks, which is ≤ AST node count. |
| Ring analysis is an extra pass | It's O(µops) and replaces the ring heuristic in `Lowering.Assemble()`. Net cost is approximately zero. |
| Block-structured IR allocates many small objects | Object pooling for `BasicBlock`, `Instr`, and `Value`. But don't optimize prematurely—first measure. |
| Backend visitors duplicate loop over instructions | Visitors share the same block traversal; only the per-instruction switch differs. This is comparable to the current per-backend compilation. |

## 14. Risks & Mitigations

| Risk | Mitigation |
|------|-----------|
| IR design constrains future features | Start small. Only add `TypeKind` variants when a concrete backend needs them. Extend via new `Instr` subtypes (open record hierarchy). |
| φ handling in VM backend introduces regressions | The φ is already in the IR explicitly; the `UopLoweringVisitor` maps it to `PhiMarker` + resolved `ConsumedFromPcs`. Cross-validate against old pipeline for every test. |
| Closure handling is fragile | Closures in the current system are well-tested (`Closure.cs`, `AllocClosure`/`LoadCapture`/`StoreCapture` µops). The IR preserves this exactly. |
| Team doesn't adopt the new pipeline | Keep old pipeline working indefinitely. Only new `Emit` overrides see the new API. Migration is opt-in per node type. |

## 15. Implementation Order (Recommended)

1. Scaffold `Poly/Interpretation/Ir/` types and `Module`.
2. Add virtual `Value? Emit(EmissionContext ctx)` to `Poly/Syntax/Node.cs`, implement `EmissionContext` and `GenerationPass`.
3. Override `Emit` on `Add`. Cross-validate against existing µop output.
4. Implement `UopLoweringVisitor` (IR → µops). Use existing `ProgramCompiler` unchanged.
5. Override `Emit` on `Constant`, `Variable`, `Parameter`. Cross-validate.
6. Override on `IfStatement` (introduces explicit φ). Cross-validate `BranchIfFalse`/`Jump`/φ output.
7. Override on `Block`, `Assignment`. Cross-validate.
8. Override on `WhileLoop`, `DoWhileLoop`, `ForLoop`. Cross-validate.
9. Override on `Lambda`, `Invoke` + closure support. Cross-validate.
10. Override on remaining nodes (member, index, throw, try-catch, operators, etc.).
11. Run entire test suite. Flip default. Deprecate old passes.
12. Add `ExpressionTreeVisitor` and `CSharpSourceVisitor` as optional backends.

Each step maintains a passing test suite. The plan is designed so that any step can be deferred or reverted.

## 16. References

- New files created:
  - `Poly/Interpretation/Ir/*.cs` — IR types, `EmissionContext`, `GenerationPass`
  - `Poly/Interpretation/Ir/Visitors/*.cs` — `Visitor` base, `UopLoweringVisitor`, `ExpressionTreeVisitor`, `CSharpSourceVisitor`
  - `Poly/Interpretation/Ir/Passes/*.cs` — SSA construction, constant folding, inlining, ring analysis
  - `Poly.Tests/Interpretation/IrCompilationTests.cs` — cross-validation tests
- Files modified:
  - `Poly/Syntax/Node.cs` — add virtual `Emit` method to base
  - `Poly/Interpretation/Analysis/LoweringPrep/UopGenerationPass.cs` (700-line switch, target for replacement)
  - `Poly/Interpretation/Vm/Lowering.cs` (ring-based φ heuristic in `Assemble()`)
  - `Poly/Interpretation/Vm/ProgramCompiler.cs` (ring allocation and expression tree compilation, preserved)
  - `Poly/Interpretation/Vm/CompilationContext.cs` (ring register locals, preserved)
  - `Poly/Interpretation/LinqExpressions/LinqExpressionGenerator.cs` (Expression tree backend, eventually replaced by `ExpressionTreeVisitor`)
  - `Poly/Interpretation/Vm/Closure.cs` (closure model, preserved)
  - `Poly/Interpretation/Analysis/LoweringPrep/LoweringPrepPass.cs` (label assignment + depth computation, partially subsumed by block-structured IR)
  - Every node under `Poly/Syntax/Nodes/*.cs` — add `override Emit` implementation to each
- Test files:
  - `Poly.Tests/Interpretation/VmCorrectnessTests.cs` (authoritative cross-validation source)
  - `Poly.Tests/Interpretation/InstructionProfilerTests.cs`
  - `Poly.Tests/Interpretation/VmDebuggerTests.cs`