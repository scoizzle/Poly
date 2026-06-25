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
- Expose backends as IR-to-output compilers that never touch AST node code.
- Handle closures, heap constants, external call sites, and incremental compilation—all omitted from the previous plan.
- Maintain full backwards compatibility during migration; co-exist with old pipeline until parity is proven.

## 3. Canonical IR Design

### 3.0 Role in the Neurosymbolic Platform

The IR is the structural layer between Poly's domain-level expression and its execution backends. It is the **lowest level that is semantically complete**: every IR `Module` has a deterministic execution result, but the model is not burdened with execution-model concerns (ring allocation, PC offsets, `ConsumedFromPcs` arrays). Models default to authoring at the domain level (`DomainModeling`); the IR is the compiler's lowering target and an opt-in escape hatch for performance-critical paths.

For the full three-level expression model, deterministic lowering pipeline, and the neurosymbolic role of the IR, see `docs/ARCHITECTURE.md` §3. For the platform vision this serves, see `docs/decisions/2026-05-31-neurosymbolic-platform-vision.md`.

The remainder of this section (§3.1–§3.3) describes the IR's structural design. §4 describes how AST nodes lower to IR via `node.Emit(ctx)`. §5 describes the IR-level optimization passes. §6 describes the backends that project IR into executable or human-readable forms.

### 3.1 Guiding principles

- **Single canonical pipeline**: AST → IR → backend. The IR is the only intermediate representation. Each AST node emits exactly one IR fragment. Backends never see AST nodes.
- **Block-structured CFG**: every block has a label, an ordered list of instructions, and a terminator (branch, jump, return, throw). No flat instruction list with label markers.
- **SSA values**: each instruction produces zero or one `Value`. Consumers reference that value directly—no implicit stack or ring. This makes dataflow explicit.
- **Typed, but minimally**: IR types are a small enum (`Word`, `Boolean`, `Handle`, `Void`), kept CLR-agnostic. `Word` represents one machine word (64-bit signed integer; maps to `long` in CLR, `i64` in WASM, etc.). Float and integer operations are distinguished via `OpKind`, not types. The CLR adaptation layer maps `Handle` to `object?`.
- **Scoped locals fallback**: for variable reassignment (`x = x + 1`), the IR allows mutable local slots accessed via `LoadLocal`/`StoreLocal` as an escape from SSA purity. The SSA construction pass converts these to SSA values with φ nodes at join points.

### 3.2 Core types

```csharp
// ── Values ──────────────────────────────────────────────────────────
public enum TypeKind { Word, Boolean, Handle, Void }

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
    : Instr(TypeKind.Word /* resolved from call-site table */, Source);
// CallSiteIndex: index into Module.CallSites (analogous to VmProgram.CallSites).
// Resolved during IR generation from AnalysisResult.GetResolvedMember().
public sealed record AllocClosure(int FuncIndex, Value[] Captures, NodeId? Source) : Instr(TypeKind.Handle, Source);
public sealed record LoadUpvalue(int UpvalueIndex, NodeId? Source) : Instr(TypeKind.Word, Source);
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
- **Result types from operators**: IR instruction result types are derived from their operands and operator kind where possible (`BinOp` resolves via `Op.ResultType(Left.Kind, Right.Kind)`, `UnaryOp` inherits `Operand.Kind`). For `Call`, the result type comes from the resolved member in the call-site table (section 9.2). `Op.ResultType` is a `static TypeKind OpKind.ResultType(TypeKind, TypeKind)` extension/helper on `OpKind`, defined alongside `OpKind` under `Poly/Ir/` at implementation time; the standard cases are: arithmetic ops return `Word`, comparison ops return `Boolean`. Float and integer operations are distinguished at the `OpKind` level (`Add` vs `FAdd` in future float support), not at the type level — the IR already maps both to `Word` because the VM stores everything in 64-bit slots. This makes the IR self-describing without requiring a separate type environment.

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
        if (EnableInlining)    Inliner.Run(module);

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
| **Testing** | Each step (`SsaTransform.Run`, `ConstantFolding.Run`, `Inliner.Run`) is a public static method on its class. Tests call them directly without any `INodeAnalyzer` infrastructure. |

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
                                              Inliner (step 2)
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
                var newVal = new Value(phi, phi.Incoming[0]?.Kind ?? TypeKind.Word, 0);
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
  %0 = Parameter(slot=0, Word)       // function argument x
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

**Step 0 (parameter seeding):** Walk the entry block. `Parameter(slot=0, Word)` defines `%0`. Push `%0` onto `stacks[0]`. Remove the `Parameter` instruction from the entry block. `stacks[0] = [%0]`.

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

Note the parameter `%p0` flows into the condition and both branch bodies — the LoadLocal indirection is gone. The Phi at merge selects the value stored in the then-branch vs. the else-branch, which the back-end `UopCompiler` resolves via `PhiMarker` + `ConsumedFromPcs`.

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

    var lowered = new UopCompiler().Lower(module);
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

The SSA pass is designed to be **optional** for the VM backend (the `UopCompiler` can work with or without SSA, since `LoadLocal`/`StoreLocal` map directly to µop `LoadSlot`/`StoreSlot`). It becomes essential for passes that need def-use chains: constant propagation, dead code elimination, inlining, and vectorization.

### 5.2 Constant Folding Pass

Walks each block's instructions; for `BinOp` where both operands are `Const`, computes the result and replaces the instruction with a new `Const`. Propagates the new `Value` to all users (requires a use-list on `Value`, stored in `Module` as a side table).

### 5.3 Inline Pass

Heuristic: inline `Call` targets whose callee module has ≤N instructions. Inlines into the caller block, handling argument mapping and return-value φ.

### 5.4 Ring Analysis Pass (VM-backend-specific)

Takes the SSA IR and computes eval-stack ring depths for each block+instruction. Attaches `RingMetadata` as a side table on `Module`. This pass exists only for the VM backend; other backends (C# source) ignore it.

## 6. Backends (IR → Output)

Backends are self-contained compilers that consume a `Module` and produce output. No shared base class — each backend owns its own traversal.

```csharp
public sealed class UopCompiler {
    public LoweringResult Compile(Module module) { /* blocks → µops */ }
}

public sealed class ExpressionCompiler {
    public LambdaExpression Compile(Module module) { /* IR → Expression trees */ }
}

public sealed class CSharpCodeGenerator {
    public string Generate(Module module) { /* IR → C# text */ }
}
```

### 6.1 VM Backend (`UopCompiler`)

This is the direct replacement for the current `Lowering.Lower()` + `ProgramCompiler.Compile()`:

1. **Block ordering**: topologically sort blocks (dominator-tree order). Assign each block a contiguous range of µop PCs.
2. **Instruction emission**: for each `Instr`, emit the corresponding µop (`BinOp` → `BinOp`, `Phi` → `PhiMarker` + resolved `ConsumedFromPcs` patch, `AllocHeap` → `LoadHeapConst`, etc.).
3. **Ring analysis**: run `RingAnalyzer` to compute `ConsumedFromPcs` and `PhiSourcePcs`/`PhiAltPcs`. Since φ is already explicit in the IR, the ring analysis is simpler than today's heuristic.
4. **Label resolution**: block-relative offsets become absolute µop PCs.
5. Produces a `LoweringResult` that feeds into the existing `ProgramCompiler`.

Result**: the existing VM pipeline (ring allocation, expression tree compilation, `VmProgram`) is preserved without modification. Only the input changes.

### 6.2 Expression Tree Backend (`ExpressionCompiler`)

Replaces `LinqExpressionGenerator` in the long term (but does not need to replace it during migration). Walks the IR and emits `System.Linq.Expressions.Expression`:

- `Const` → `Expression.Constant(value)`
- `BinOp` → `Expression.Add(l, r)` etc.
- `Phi` → resolves to the incoming value selected by the predecessor path (or a conditional expression for ternary merges).
- `Goto` → `Expression.Goto(label)`
- `CondBranch` → `Expression.IfThenElse(cond, then, else)`
- `Ret` → `Expression.Return(label, value)`

### 6.3 C# Source Backend (`CSharpCodeGenerator`)

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
  %0 = Parameter(slot=0, Word)       // function argument x
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

### 7.4 VM µop output (after `UopCompiler`, SSA disabled)

The `UopCompiler` maps `LoadLocal → LoadSlot`, `Const → LoadConst`, `BinOp → BinOp`, etc. With SSA disabled (the default for the VM backend), `LoadLocal` instructions survive the pipeline and produce µop `LoadSlot`:

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

The `UopCompiler` maps `AllocHeap` to `LoadHeapConst` µops.

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

- Add all IR types under `Poly/Ir/`.
- Add virtual `Emit` method to `Poly/Syntax/Node.cs` (base returns `null`).
- Add `EmissionContext` and `GenerationPass` in `Poly/Interpretation/Ir/`, registered via `.UseIrGeneration()`.
- Override `Emit` on `Add`. Write tests that produce IR from a standalone node and verify the IR structure.
- **No existing code changes**. Old `UopGenerationPass` untouched.

### Phase 2: UopCompiler (adapter)

- Implement `UopCompiler` that walks a `Module` and produces a `LoweringResult`.
- Test: AST → `Emit` → `UopCompiler` → `ProgramCompiler.Compile` → `Vm.Execute` → assert result equals old pipeline.
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

- Change `AnalyzerBuilder` extensions so `.UseUopGeneration()` runs `GenerationPass` + `UopCompiler`.
- Keep old `UopGenerationPass` behind a compatibility flag.
- Run entire test suite. If green, mark old passes `[Obsolete]`.

### Phase 5: Backend expansion

- Add `ExpressionCompiler` → compare output to `LinqExpressionGenerator` on every expression type.
- Add `CSharpCodeGenerator` → write golden-file tests.
- Add `RingAnalyzer` (optional, VM-backend-specific).

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
    var lowered = new UopCompiler().Lower(ir);
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
    var module = IrPipeline.Emit(ast);

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
| Backend compilers iterate over instructions | Each backend owns its own block traversal; only the per-instruction dispatch differs. This is comparable to the current per-backend compilation. |

## 14. Risks & Mitigations

| Risk | Mitigation |
|------|-----------|
| IR design constrains future features | Start small. Only add `TypeKind` variants when a concrete backend needs them. Extend via new `Instr` subtypes (open record hierarchy). |
| φ handling in VM backend introduces regressions | The φ is already in the IR explicitly; the `UopCompiler` maps it to `PhiMarker` + resolved `ConsumedFromPcs`. Cross-validate against old pipeline for every test. |
| Closure handling is fragile | Closures in the current system are well-tested (`Closure.cs`, `AllocClosure`/`LoadCapture`/`StoreCapture` µops). The IR preserves this exactly. |
| Team doesn't adopt the new pipeline | Keep old pipeline working indefinitely. Only new `Emit` overrides see the new API. Migration is opt-in per node type. |

## 15. Implementation Order (Detailed)

Each step maintains a passing test suite. The plan is designed so that any step can be deferred or reverted without breaking prior steps. Every section references the files and types listed in §16 (References).

---

### Step 1: Scaffold IR types and `Module`

**Goal:** The IR type hierarchy compiles and can be instantiated in tests. No existing code is modified.

**Files to create:**

| File | Contents |
|------|----------|
| `Poly/Ir/TypeKind.cs` | `enum TypeKind { Word, Boolean, Handle, Void }` |
| `Poly/Ir/OpKind.cs` | `enum OpKind { Add, Sub, Mul, Div, Mod, Gt, Gte, Lt, Lte, Eq, Neq }` with `static TypeKind ResultType(TypeKind left, TypeKind right)` — returns `Word` for arithmetic, `Boolean` for comparisons |
| `Poly/Ir/UnaryOpKind.cs` | `enum UnaryOpKind { Neg, Not }` |
| `Poly/Ir/Value.cs` | `sealed record Value(Instr Definition, TypeKind Kind, int Index)` |
| `Poly/Ir/Instr.cs` | `abstract record Instr(TypeKind ResultType, NodeId? Source)` |
| `Poly/Ir/Instructions/Const.cs` | `sealed record Const(long Value, TypeKind Kind, NodeId? Source) : Instr(...)` |
| `Poly/Ir/Instructions/BinOp.cs` | `sealed record BinOp(OpKind Op, Value Left, Value Right, NodeId? Source) : Instr(...)` |
| `Poly/Ir/Instructions/UnaryOp.cs` | `sealed record UnaryOp(UnaryOpKind Op, Value Operand, NodeId? Source) : Instr(...)` |
| `Poly/Ir/Instructions/LoadLocal.cs` | `sealed record LoadLocal(int SlotIndex, TypeKind Kind, NodeId? Source) : Instr(...)` |
| `Poly/Ir/Instructions/StoreLocal.cs` | `sealed record StoreLocal(int SlotIndex, Value Val, NodeId? Source) : Instr(...)` |
| `Poly/Ir/Instructions/Parameter.cs` | `sealed record Parameter(int SlotIndex, TypeKind Kind, NodeId? Source) : Instr(...)` |
| `Poly/Ir/Instructions/Call.cs` | `sealed record Call(Value Target, Value[] Args, int ArgCount, bool IsExternal, int CallSiteIndex, NodeId? Source) : Instr(...)` |
| `Poly/Ir/Instructions/AllocClosure.cs` | `sealed record AllocClosure(int FuncIndex, Value[] Captures, NodeId? Source) : Instr(...)` |
| `Poly/Ir/Instructions/LoadUpvalue.cs` | `sealed record LoadUpvalue(int UpvalueIndex, NodeId? Source) : Instr(...)` |
| `Poly/Ir/Instructions/StoreUpvalue.cs` | `sealed record StoreUpvalue(int UpvalueIndex, Value Val, NodeId? Source) : Instr(...)` |
| `Poly/Ir/Instructions/AllocHeap.cs` | `sealed record AllocHeap(int HandleIndex, NodeId? Source) : Instr(...)` |
| `Poly/Ir/Instructions/Phi.cs` | `sealed record Phi(int? SlotIndex, Value[] Incoming, NodeId? Source) : Instr(...)` |
| `Poly/Ir/Terminators/Terminator.cs` | `abstract record Terminator(NodeId? Source)` |
| `Poly/Ir/Terminators/Goto.cs` | `sealed record Goto(BasicBlock Target) : Terminator(null)` |
| `Poly/Ir/Terminators/CondBranch.cs` | `sealed record CondBranch(Value Condition, BasicBlock ThenTarget, BasicBlock ElseTarget) : Terminator(null)` |
| `Poly/Ir/Terminators/Ret.cs` | `sealed record Ret(Value? Result) : Terminator(null)` |
| `Poly/Ir/Terminators/Throw.cs` | `sealed record Throw(Value Exception) : Terminator(null)` |
| `Poly/Ir/BasicBlock.cs` | `sealed class BasicBlock { string Name; List<Instr> Instructions; Terminator? Terminator; }` |
| `Poly/Ir/Module.cs` | `sealed class Module { List<BasicBlock> Blocks; List<BasicBlock> ExportedFunctions; List<object?> HeapConstants; List<CallSite> CallSites; int MaxLocalSlots; }` |
| `Poly/Ir/CallSite.cs` | `sealed record CallSite(string MethodName, TypeKind ReturnType, int ArgCount)` |

**Files to modify:** None.

**Tests to write** (`Poly.Tests/Ir/IrTypeTests.cs`):

```csharp
[Test]
public async Task CanCreateModule() {
    var module = new Module();
    var block = new BasicBlock("entry");
    module.Blocks.Add(block);
    await Assert.That(module.Blocks).HasCount().EqualTo(1);
}

[Test]
public async Task CanCreateInstructions() {
    var instr = new Const(42, TypeKind.Word, null);
    await Assert.That(instr.Value).IsEqualTo(42);
    await Assert.That(instr.ResultType).IsEqualTo(TypeKind.Word);
}

[Test]
public async Task OpKindResultType_ComparisonsReturnBoolean() {
    var result = OpKind.Gt.ResultType(TypeKind.Word, TypeKind.Word);
    await Assert.That(result).IsEqualTo(TypeKind.Boolean);
}

[Test]
public async Task OpKindResultType_ArithmeticReturnsWord() {
    var result = OpKind.Add.ResultType(TypeKind.Word, TypeKind.Word);
    await Assert.That(result).IsEqualTo(TypeKind.Word);
}
```

**Success criteria:**
- `dotnet build Poly/Poly.csproj` passes.
- All IR type tests pass.
- Existing test suite is unaffected (no old code changed).

---

### Step 2: Virtual `Emit` on `Node` + `EmissionContext` + `GenerationPass`

**Goal:** The `Node` base carries a virtual `Emit` method. `GenerationPass` is registered as an `INodeAnalyzer` that calls `node.Emit(ctx)` and stashes the resulting `Module` in `AnalysisContext` metadata. No node overrides `Emit` yet — the pass produces an empty module for any AST.

**Files to create:**

| File | Contents |
|------|----------|
| `Poly/Interpretation/Ir/EmissionContext.cs` | `sealed class EmissionContext { Module Module; BasicBlock CurrentBlock; AnalysisResult Analysis; Scope Scope; Value? EmitChild(Node); Value Emit(Instr); BasicBlock SplitBlock(string); int DeclareLocal(string, TypeKind); }` |
| `Poly/Interpretation/Ir/GenerationPass.cs` | `sealed class GenerationPass : INodeAnalyzer { bool EnableSsa, EnableConstFolding, EnableInlining; void Analyze(AnalysisContext, Node); }` — emits IR via `node.Emit(ctx)`, calls `SsaTransform.Run(module)` / `ConstantFolding.Run(module)` / `Inliner.Run(module)` if enabled, stashes `ModuleMetadata` |
| `Poly/Interpretation/Ir/ModuleMetadata.cs` | `sealed record ModuleMetadata(Module Module) : IAnalysisMetadata` |
| `Poly/Interpretation/Ir/GenerationPassExtensions.cs` | `static AnalyzerBuilder UseIrGeneration(this AnalyzerBuilder, GenerationPass? pass = null)` — registers `GenerationPass` in the analyzer pipeline |

**Files to modify:**

| File | Change |
|------|--------|
| `Poly/Syntax/Node.cs` | Add `public virtual Value? Emit(EmissionContext ctx) => null;` to the abstract `Node` record |

**Tests to write** (`Poly.Tests/Ir/GenerationPassTests.cs`):

```csharp
[Test]
public async Task GenerationPass_ProducesEmptyModule_ForUnknownNode() {
    var node = new Constant(42);  // no Emit override yet — base returns null
    var analyzer = new AnalyzerBuilder().UseIrGeneration().Build();
    var result = analyzer.Analyze(node);
    var module = result.GetMetadata<ModuleMetadata>(node).Module;
    // Entry block has a Ret(null) because node.Emit returned null
    await Assert.That(module.Blocks).HasCount().EqualTo(1);
    await Assert.That(module.Blocks[0].Terminator).IsTypeOf<Ret>();
}

[Test]
public async Task GenerationPass_DoesNotAffectOldPipeline() {
    // Old UopGenerationPass still works; new pass is registered but not default
    var node = new Add(new Constant(3), new Constant(4));
    var oldAnalyzer = new AnalyzerBuilder()
        .UseTypeResolver().UseMemberResolver().UseVariableScopeValidator()
        .UseLoweringPreparation().UseUopGeneration()
        .Build();
    var oldResult = oldAnalyzer.Analyze(node);
    // Old pipeline produces valid lowering result
    // (verify by compiling & executing via existing test helpers)
}
```

**Success criteria:**
- `dotnet build` passes.
- `GenerationPass` registers and runs without error.
- No AST node overrides `Emit` yet; the pass produces an empty module for all nodes.
- Old pipeline tests pass unchanged.

---

### Step 3: Override `Emit` on `Add` — first cross-validation

**Goal:** The first real `Emit` override is working end-to-end: AST node → IR → `UopCompiler` → `LoweringResult` → `ProgramCompiler` → `Vm.Execute` → correct result.

**Files to create:**

| File | Contents |
|------|----------|
| `Poly/Interpretation/Ir/Backends/UopCompiler.cs` | Walks blocks in dominator-tree order, emits µops per instruction. Minimal implementation: `Const` → `LoadConst`, `BinOp` → `BinOp`, `Ret` → `ReturnOp`. Everything else throws `NotSupportedException` (added in later steps). |
| `Poly.Tests/TestHelpers/IrPipeline.cs` | `static class IrPipeline { static Module Emit(Node node); static object Execute(Node node); }` — convenience wrappers: build analyzer with `.UseIrGeneration()`, extract `ModuleMetadata`, lower via `UopCompiler`, compile & execute via VM. Returns the result from the VM stack. |

**Files to modify:**

| File | Change |
|------|--------|
| `Poly/Syntax/Nodes/Add.cs` | Add `public override Value? Emit(EmissionContext ctx)`: emit children, emit `new BinOp(OpKind.Add, left!, right!, Id)` |

**UopCompiler** minimal µop mapping for this step:

| IR instruction | µop emitted |
|---------------|-------------|
| `Const` | `new LoadConst { Value = instr.Value, SourceNodeId = instr.Source }` |
| `BinOp` | `new BinOp { Op = instr.Op.ToUopKind(), SourceNodeId = instr.Source }` with `ConsumedFromPcs` set from value references |
| `Ret` | `new ReturnOp { SourceNodeId = term.Source }` with `ConsumedFromPcs` for the result value |

The compiler maintains a `Dictionary<Value, int>` mapping each IR `Value` to the µop PC that produced it, used to compute `ConsumedFromPcs` arrays.

**Tests to write** (`Poly.Tests/Ir/IrCrossValidationTests.cs`):

```csharp
[Test]
public async Task Add_TwoIntegers_CrossValidate() {
    var node = new Add(new Constant(3), new Constant(4));

    // Old pipeline
    var oldResult = OldPipeline.Execute(node);   // existing helper

    // New pipeline
    var newResult = IrPipeline.Execute(node);

    await Assert.That(newResult).IsEqualTo(oldResult);  // both produce 7
}

[Test]
public async Task Add_ProducesCorrectIr() {
    var node = new Add(new Constant(3), new Constant(4));
    var module = IrPipeline.Emit(node);

    await Assert.That(module.Blocks).HasCount().EqualTo(1);
    var instrs = module.Blocks[0].Instructions;
    await Assert.That(instrs).HasCount().EqualTo(3);  // Const(3), Const(4), BinOp(Add)
    await Assert.That(instrs[2]).IsTypeOf<BinOp>();
}
```

**Success criteria:**
- `Add(Const(3), Const(4))` produces correct result (7) via both old and new pipeline.
- IR structure has 1 block with 3 instructions (2 `Const`, 1 `BinOp`).
- `UopCompiler` produces a `LoweringResult` that `ProgramCompiler` accepts.
- Only `Add` has an `Emit` override; all other node types still go through the old pipeline.

---

### Step 4: `UopCompiler` — complete µop mapping table + block ordering

**Goal:** The `UopCompiler` has a dispatch entry for every IR instruction type defined in §3.2, even types not yet reachable (which throw `NotSupportedException`). This ensures that when later steps introduce `IfStatement` (Step 6), `Lambda` (Step 9), etc., the compiler is already structured to accept them — only the throwing stubs need to be replaced with real emission. Block ordering (dominator-tree DFS) and label resolution (PC assignment) are implemented and tested with a synthetic multi-block module.

**Files to modify:**

| File | Change |
|------|--------|
| `Poly/Interpretation/Ir/Backends/UopCompiler.cs` | Complete the µop mapping table for all instruction types defined in §3.2, even if they throw `NotSupportedException` for now. Add block-ordering logic: dominator-tree DFS determines block order; assign contiguous PC ranges per block; resolve `Goto.Target` and `CondBranch.ThenTarget`/`ElseTarget` to absolute PCs. |

**Complete µop mapping table:**

| IR instruction | µop emitted | Notes |
|---------------|-------------|-------|
| `Const` | `LoadConst` | |
| `BinOp` | `BinOp` | Map `OpKind` to µop operator enum |
| `UnaryOp` | `UnaryOp` (Neg/Not) | |
| `LoadLocal` | `LoadSlot(slotIndex)` | |
| `StoreLocal` | `StoreSlot(slotIndex)` + `ConsumedFromPcs` | |
| `Parameter` | *skipped* (no µop) | SSA-only; VM loads params from frame |
| `Call` (IsExternal=true) | `Call(callSiteIndex)` | Resolve from `CallSites` table |
| `Call` (IsExternal=false) | `CallClosure` + closure dispatch | |
| `AllocClosure` | `AllocClosure` µop | |
| `LoadUpvalue` | `LoadCapture(upvalueIndex)` | |
| `StoreUpvalue` | `StoreCapture(upvalueIndex)` | |
| `AllocHeap` | `LoadHeapConst(handleIndex)` | |
| `Phi` (slot-level) | `PhiMarker([altPcs], sourcePc)` | Resolved via ring analysis |
| `Phi` (value-level) | `PhiMarker([altPcs], sourcePc)` | Same; the IR already gives incoming values |
| `Goto` | `Jump(targetPc)` | |
| `CondBranch` | `BranchIfFalse(targetPc)` | Condition on top of eval stack |
| `Ret` | `ReturnOp` | |
| `Throw` | `ThrowOp` | |

**Tests to add:**

```csharp
[Test]
public async Task UopCompiler_Const_EmitsLoadConst() {
    var module = BuildModule(new Const(42, TypeKind.Word, null));
    var result = new UopCompiler().Lower(module);
    await Assert.That(result.Instructions[0]).IsTypeOf<LoadConst>();
    await Assert.That(((LoadConst)result.Instructions[0]).Value).IsEqualTo(42);
}

[Test]
public async Task UopCompiler_BlockOrdering_PreservesDominatorOrder() {
    // Build a module with entry→then→merge, entry→else→merge
    // Verify µop order is entry, then, else, merge (dominator-tree DFS)
}
```

**Success criteria:**
- All existing `Add` cross-validation tests still pass.
- `UopCompiler` produces correct µop sequences for all currently-emitted instruction types.
- Multi-block CFG ordering test passes.

---

### Step 5: Override `Emit` on `Constant`, `Variable`, `Parameter`

**Goal:** The three leaf-node types emit correct IR. Numeric constants produce `Const`; non-numeric constants register in `HeapConstants` and produce `AllocHeap`. `Variable` produces `LoadLocal`. `Parameter` (function parameter declarations) produce `Parameter` instructions.

**Files to modify:**

| File | Change |
|------|--------|
| `Poly/Syntax/Nodes/Constant.cs` | `override Emit`: if value is numeric (`long`, `double`, `bool`), emit `Const`; otherwise register in `ctx.Module.HeapConstants` and emit `AllocHeap(handle, Id)` |
| `Poly/Syntax/Nodes/Variable.cs` | `override Emit`: resolve slot index from `ctx` scope (or `AnalysisResult`), emit `LoadLocal(slot, kind, Id)` |
| `Poly/Syntax/Nodes/Parameter.cs` (if separate) | `override Emit`: emit `Parameter(slot, kind, Id)` — defines the initial SSA value for this function argument |

**EmissionContext additions:**

- `DeclareLocal(string name, TypeKind kind)` → returns `int` slot index. Maintains a `Dictionary<string, int>` name→slot mapping and increments `Module.MaxLocalSlots`.
- `Scope` must track slot bindings per scope level. On scope exit, slots are released.

**Tests to write:**

```csharp
[Test]
public async Task Constant_Integer_EmitsConst() {
    var module = IrPipeline.Emit(new Constant(42));
    await Assert.That(module.Blocks[0].Instructions[0]).IsTypeOf<Const>();
}

[Test]
public async Task Constant_String_EmitsAllocHeap() {
    var module = IrPipeline.Emit(new Constant("hello"));
    var alloc = module.Blocks[0].Instructions[0];
    await Assert.That(alloc).IsTypeOf<AllocHeap>();
    await Assert.That(module.HeapConstants[(int)((AllocHeap)alloc).HandleIndex])
        .IsEqualTo("hello");
}

[Test]
public async Task Variable_CrossValidate() {
    // Build a lambda: (x) => x + 1
    // Old pipeline vs new pipeline — both return x+1 for a given x
}

[Test]
public async Task Parameter_EmitsParameterInstruction() {
    // Verify entry block contains Parameter(slot=0, Word)
    var module = EmitFunction("(x) => x");
    await Assert.That(module.Blocks[0].Instructions[0]).IsTypeOf<Parameter>();
}
```

**Success criteria:**
- Add, Constant, Variable, and Parameter all emit correct IR.
- Cross-validation for expressions involving these node types passes.
- Non-numeric constants round-trip through `HeapConstants`.

---

### Step 6: Override `Emit` on `IfStatement` — φ milestone

**Goal:** Conditional branching works end-to-end. This is the most important step because it validates the block-structured IR, Phi insertion, and the φ handling in `UopCompiler`.

**Files to modify:**

| File | Change |
|------|--------|
| `Poly/Syntax/Nodes/IfStatement.cs` | `override Emit`: emit condition → `CondBranch` to then/else blocks; emit then body; emit else body (or skip if absent); create merge block; insert value-level `Phi(null, [thenResult, elseResult])` at merge if the if-statement is an expression |

**EmissionContext additions:**

- `SplitBlock(string suffix)` → creates a new `BasicBlock`, sets `CurrentBlock.Terminator = new Goto(newBlock)`, sets `CurrentBlock = newBlock`, adds newBlock to `Module.Blocks`, returns newBlock.
- `CurrentBlock.Terminator` can be set directly by the emitter (for `CondBranch`, `Ret`, `Throw`).

**Emit logic for `IfStatement`:**

```
// 1. Emit condition in current block
var cond = ctx.EmitChild(Condition);

// 2. Create then/else/merge blocks
var thenBlock = ctx.SplitBlock("then");
// emit ThenBody into thenBlock
var thenResult = ctx.EmitChild(ThenBody);
var elseBlock = ctx.SplitBlock("else");
// emit ElseBody into elseBlock (if exists; else emit unit/null)
var elseResult = ctx.EmitChild(ElseBody);
var mergeBlock = ctx.SplitBlock("merge");

// 3. Set terminators
// entry block → CondBranch(cond, thenBlock, elseBlock)
// thenBlock → Goto(mergeBlock)
// elseBlock → Goto(mergeBlock)

// 4. Emit Phi at merge if expression-valued
var phi = new Phi(null, [thenResult!, elseResult!], Id);
ctx.Emit(phi);
```

**UopCompiler additions:**

- `CondBranch` → `BranchIfFalse(targetPc)`: condition is on eval stack; if false, branch to else target.
- `Goto` → `Jump(targetPc)`.
- `Phi` (value-level) → emit `PhiMarker([altPcs], sourcePc)` where `altPcs` are the PCs within the predecessor blocks that produce the incoming values, and `sourcePc` is the PC of the Phi instruction itself (so the ring allocator knows *which* branch's result to pop).

**Tests to write:**

```csharp
[Test]
public async Task IfStatement_Expression_CrossValidate() {
    // if (x > 0) x + 1 else x - 1
    // Compile with old pipeline, compile with new pipeline, compare results
    var ast = new IfStatement(
        new GreaterThan(new Variable("x"), new Constant(0)),
        new Add(new Variable("x"), new Constant(1)),
        new Subtract(new Variable("x"), new Constant(1))
    );
    // Cross-validate for x=5 (→ 6) and x=-3 (→ -4)
}

[Test]
public async Task IfStatement_ProducesCorrectBlocks() {
    var module = IrPipeline.Emit(ifAst);
    await Assert.That(module.Blocks).HasCount().EqualTo(4);  // entry, then, else, merge
    await Assert.That(module.Blocks[0].Terminator).IsTypeOf<CondBranch>();
    await Assert.That(module.Blocks[3].Instructions).Any(i => i is Phi);
}

[Test]
public async Task IfStatement_NoElse_EmitsUnitForElseBranch() {
    // if (x > 0) x + 1   (no else clause; returns unit/void)
    // Cross-validate: the merge Phi should use a unit/void value for the else path
}
```

**Success criteria:**
- `if`/`else` expressions produce correct results via both pipelines.
- Block count is 4 (entry, then, else, merge).
- `CondBranch`, `Goto`, `Phi` all appear in correct positions.
- VM execution of the lowered output matches the old pipeline.

---

### Step 7: Override `Emit` on `Block`, `Assignment`

**Goal:** Multi-statement blocks and variable mutation work. This step introduces scope management and `StoreLocal`.

**Files to modify:**

| File | Change |
|------|--------|
| `Poly/Syntax/Nodes/Block.cs` | `override Emit`: for each child, emit child; if child is a block-scoped variable declaration, call `ctx.DeclareLocal` and emit `StoreLocal`; if the block is an expression block, the last child's result is the block's result |
| `Poly/Syntax/Nodes/Assignment.cs` | `override Emit`: emit RHS value; emit `StoreLocal(slot, value)` for the LHS variable; emit a `LoadLocal(slot)` to reload for expression value (or return the stored value directly if the backend supports it) |

**EmissionContext additions:**

- `Scope` — a stack of scope frames. `EnterScope()` / `ExitScope()`. Each frame tracks name → slot bindings. `DeclareLocal` adds to the current frame.
- `ResolveLocal(string name)` → returns `(int slot, TypeKind kind)` by walking scope frames from innermost outward. Throws if undefined.

**Tests to write:**

```csharp
[Test]
public async Task Block_MultipleStatements_LastExpressionIsResult() {
    // { x = 1; x + 2 }  → result is 3
}

[Test]
public async Task Block_DeclaresLocalVariable() {
    // { var y = 5; y * 2 }  → declares slot, stores 5, loads y, multiplies
    var module = IrPipeline.Emit(blockAst);
    await Assert.That(module.MaxLocalSlots).IsGreaterThan(0);
}

[Test]
public async Task Assignment_CrossValidate() {
    // x = 42; return x
    // Both pipelines produce 42
}

[Test]
public async Task Assignment_Chain_CrossValidate() {
    // a = b = 10; return a + b
    // Both pipelines produce 20
}
```

**Success criteria:**
- `Block` with multiple statements produces correct IR with `StoreLocal`/`LoadLocal`.
- `Assignment` to variables works and cross-validates.
- Scope nesting works: inner block shadows outer variable.

---

### Step 8: Override `Emit` on loop nodes

**Goal:** All three loop constructs (`WhileLoop`, `DoWhileLoop`, `ForLoop`) produce correct block-structured IR with `Phi` at loop headers.

**Files to modify:**

| File | Change |
|------|--------|
| `Poly/Syntax/Nodes/WhileLoop.cs` | `override Emit`: create header/body/latch/exit blocks. Emit `CondBranch` at header. Body emits children. Latch jumps back to header. Exit is after the loop. |
| `Poly/Syntax/Nodes/DoWhileLoop.cs` | `override Emit`: body block first, then condition block with `CondBranch` back to body. |
| `Poly/Syntax/Nodes/ForLoop.cs` | `override Emit`: initializer in entry, then same as `WhileLoop` with increment block. |
| `Poly/Syntax/Nodes/Break.cs` | `override Emit`: `Goto` to the exit block of the nearest enclosing loop (tracked via `EmissionContext.Scope`). |
| `Poly/Syntax/Nodes/Continue.cs` | `override Emit`: `Goto` to the latch block of the nearest enclosing loop. |

**EmissionContext additions:**

- `Scope` tracks loop context: `LoopInfo? CurrentLoop` with `BasicBlock ExitBlock`, `BasicBlock LatchBlock`. Set when entering a loop, restored on exit. `Break`/`Continue` read from `CurrentLoop`.

**Tests to write:**

```csharp
[Test]
public async Task WhileLoop_CrossValidate() {
    // var i = 0; while (i < 5) i = i + 1; return i
    // Both pipelines produce 5
}

[Test]
public async Task WhileLoop_ProducesCorrectBlocks() {
    var module = IrPipeline.Emit(whileAst);
    await Assert.That(module.Blocks).HasCount().EqualTo(4);  // entry, header, body, exit
    await Assert.That(module.Blocks[1].Terminator).IsTypeOf<CondBranch>();
}

[Test]
public async Task ForLoop_CrossValidate() {
    // var sum = 0; for (var i = 0; i < 10; i = i + 1) sum = sum + i; return sum
    // Both pipelines produce 45
}

[Test]
public async Task Break_ExitsLoopEarly() {
    // var i = 0; while (true) { if (i >= 5) break; i = i + 1; } return i
    // Both pipelines produce 5
}

[Test]
public async Task Continue_SkipsToNextIteration() {
    // var sum = 0; for (var i = 0; i < 5; i = i + 1) { if (i == 2) continue; sum = sum + i; } return sum
    // Both pipelines produce 1+3+4 = 8 (skips 2)
}
```

**Success criteria:**
- All loop constructs produce correct results.
- `Break` and `Continue` correctly target the enclosing loop's exit/latch blocks.
- Nested loops work (inner break only exits inner loop).
- SSA pass correctly handles loop-carried variables (Phi at header).

---

### Step 9: Override `Emit` on `Lambda`, `Invoke` + closure support

**Goal:** First-class functions and closures work. Lambdas allocate closures, invoke dispatches to the correct call target.

**Files to modify:**

| File | Change |
|------|--------|
| `Poly/Syntax/Nodes/Lambda.cs` | `override Emit`: record captures (variables from outer scopes referenced in body), emit `AllocClosure(funcIndex, captures)`, emit body into a new `Module` stored as a nested function |
| `Poly/Syntax/Nodes/Invoke.cs` | `override Emit`: resolve target (either a known method → `Call` with `IsExternal=true` and `CallSiteIndex`, or a closure value → `Call` with `IsExternal=false`) |
| `Poly/Syntax/Nodes/LoadUpvalue.cs` | (if exists as AST node; may be an internal lowering detail) → emit `LoadUpvalue(idx)` |
| `Poly/Syntax/Nodes/StoreUpvalue.cs` | (if exists) → emit `StoreUpvalue(idx, val)` |

**EmissionContext additions:**

- `Module ExportedFunctions` — when a `Lambda` is emitted, its body module is added to `ExportedFunctions` and assigned an index.
- `Capture tracking` — the `Scope` must track which variables from outer scopes are captured by inner lambdas. When a `Variable` in a lambda body references a variable from an outer scope, that variable is added to the lambda's capture list.

**UopCompiler additions:**

- `AllocClosure` → `AllocClosure` µop (maps to existing `Closure` class)
- `Call` (IsExternal=false) → closure dispatch via `CallClosure` µop

**Tests to write:**

```csharp
[Test]
public async Task Lambda_Identity_CrossValidate() {
    // var f = (x) => x; return f(42)
    // Both pipelines produce 42
}

[Test]
public async Task Lambda_CapturesOuterVariable() {
    // var a = 10; var f = (x) => a + x; return f(5)
    // Both pipelines produce 15
}

[Test]
public async Task Lambda_Closure_AllocClosureGenerated() {
    var module = IrPipeline.Emit(lambdaWithCaptureAst);
    await Assert.That(module.Blocks[0].Instructions).Any(i => i is AllocClosure);
}

[Test]
public async Task Invoke_ExternalCall_CrossValidate() {
    // Invoke a built-in method (e.g., Math.Abs(-5))
    // Both pipelines produce 5
}
```

**Success criteria:**
- Simple lambdas (no captures) work and cross-validate.
- Lambdas with captured outer variables produce correct results.
- `AllocClosure` appears in IR for closures.
- Closure dispatch via `CallClosure` works in the VM.

---

### Step 10: Override `Emit` on all remaining nodes

**Goal:** Every AST node type has an `Emit` override. The full node set is covered and cross-validated.

**Files to modify** (one file per node type):

| Batch | Nodes | Key considerations |
|-------|-------|--------------------|
| **Operators** | `Subtract`, `Multiply`, `Divide`, `Modulo`, `GreaterThan`, `GreaterThanOrEqual`, `LessThan`, `LessThanOrEqual`, `Equal`, `NotEqual`, `And`, `Or`, `Not`, `Negate` | All follow the `Add` pattern: emit children, emit `BinOp` or `UnaryOp` with correct `OpKind` |
| **Member access** | `MemberAccess`, `ElementAccess` | Map to `LoadField` / `LoadIndex` µops via `Call` with resolved member metadata |
| **Object creation** | `New`, `NewArray` | `Call` to constructor; `NewArray` uses `AllocArray` µop |
| **Exception handling** | `Throw`, `TryCatchFinally` | `Throw` emits `Throw` terminator. `TryCatchFinally` creates try/catch/finally blocks; add implicit exception edges to the CFG (see §5.1.9) |
| **Async/control flow** | `Await`, `SuspendNode`, `Break`, `Continue` | `Await` maps to existing `Await` handling (synchronous `GetAwaiter().GetResult()`). `Break`/`Continue` already done in Step 8. `SuspendNode` emits `SuspendOp`. |

**EmissionContext additions (as needed):**

- `EnterTryScope()` / `ExitTryScope()` — track active try regions for exception-edge generation.
- `CurrentExceptionHandlers` — list of `(BasicBlock catchBlock, BasicBlock finallyBlock)` for implicit edges.

**Tests to write:**

- For each operator: `Operator_CrossValidate` test.
- For `MemberAccess`: resolve a property of a known type, cross-validate.
- For `New`: construct an object, cross-validate (if the old pipeline supports it).
- For `Throw`: verify `Throw` terminator appears in IR.
- For `TryCatchFinally`: verify catch block is reachable, exception edges exist.

**Success criteria:**
- Every AST node type has an `override Emit` implementation.
- Cross-validation test exists for every node type.
- Full `Poly.Tests` suite passes with both old and new pipelines.
- No node type left without an `Emit` override (compiler warning enforced).

---

### Step 11: Flip default — new pipeline becomes the canonical path

**Goal:** The new IR pipeline is the default. Old `UopGenerationPass` is deprecated but retained under a compatibility flag.

**Files to modify:**

| File | Change |
|------|--------|
| `Poly/Interpretation/Analysis/AnalyzerBuilder.cs` or equivalent extension file | Change `.UseUopGeneration()` to register `GenerationPass` instead of `UopGenerationPass`. Add `.UseLegacyUopGeneration()` extension that registers the old pass for compatibility. |
| `Poly/Interpretation/Analysis/LoweringPrep/UopGenerationPass.cs` | Add `[Obsolete("Replaced by GenerationPass + UopCompiler. Use .UseLegacyUopGeneration() to opt into the old pipeline.")]` |
| `Poly/Interpretation/Vm/Lowering.cs` | Add comment noting that `Assemble()` is preserved for the legacy path; new path uses `UopCompiler`. No code change. |

**Files to create:**

| File | Contents |
|------|----------|
| `Poly/Interpretation/Ir/VmLoweringPass.cs` | `sealed class VmLoweringPass : INodeAnalyzer` — registered after `GenerationPass`. Reads `ModuleMetadata` from `AnalysisContext`, calls `new UopCompiler().Lower(module)`, produces a `LoweringResult` and stashes it as `LoweringResultMetadata` (a new `IAnalysisMetadata` subtype) for `ProgramCompiler` to consume. This replaces the role that `UopGenerationPass` + `Lowering.Assemble()` play in the old pipeline. |

**Actions:**

1. Change the default analyzer builder registration: `.UseUopGeneration()` now registers `GenerationPass` followed by `VmLoweringPass` (instead of `UopGenerationPass`).
2. Run full test suite: `dotnet run --project Poly.Tests/Poly.Tests.csproj`.
3. If any test fails, fix the corresponding `Emit` override before proceeding. Do **not** proceed with failures.
4. Once green, mark old passes `[Obsolete]`.
5. Add a CI-compatible smoke test that verifies the legacy compatibility flag still works: `AnalyzerBuilder.UseLegacyUopGeneration()` compiles and executes correctly.

**Success criteria:**
- Full test suite passes with new pipeline as default.
- No functionality regression.
- Legacy pipeline still accessible via `.UseLegacyUopGeneration()`.
- Old passes are `[Obsolete]` with a message pointing to the replacement.

---

### Step 12: Backend expansion — `ExpressionCompiler` + `CSharpCodeGenerator`

**Goal:** Two additional backends exist and produce correct output. `ExpressionCompiler` replaces `LinqExpressionGenerator` long-term. `CSharpCodeGenerator` enables debugging and auditing.

**Files to create:**

| File | Contents |
|------|----------|
| `Poly/Interpretation/Ir/Backends/ExpressionCompiler.cs` | Walks `Module`, emits `System.Linq.Expressions.Expression` trees. Maps each `Instr` to expression-tree nodes (§6.2). Compiles to `LambdaExpression` via `Expression.Lambda().Compile()`. |
| `Poly/Interpretation/Ir/Backends/CSharpCodeGenerator.cs` | Walks `Module`, emits C# source text (§6.3). Maps `TypeKind` → CLR types, `OpKind` → operators, blocks → labeled statements with `goto`. |
| `Poly.Tests/Ir/ExpressionCompilerTests.cs` | Golden-file tests: compare output of `ExpressionCompiler` against `LinqExpressionGenerator` for every expression type. |
| `Poly.Tests/Ir/CSharpCodeGeneratorTests.cs` | Golden-file tests: verify generated C# compiles (via Roslyn scripting or manual inspection). |
| `Poly/Interpretation/Ir/Passes/RingAnalyzer.cs` | VM-backend-specific pass: computes eval-stack ring depths and attaches `RingMetadata` (§5.4). |

**ExpressionCompiler** implementation notes:
- `Const` → `Expression.Constant(value)`
- `BinOp` → `Expression.Add(left, right)` etc. depending on `OpKind`
- `Phi` → `Expression.Condition(conditionExpr, thenValue, elseValue)` for ternary merges
- `Goto`/`CondBranch` → `Expression.Goto(label)` / `Expression.IfThenElse(cond, then, else)`
- The `ExpressionCompiler` is a drop-in test reference; `LinqExpressionGenerator` is not removed during this step.

**CSharpCodeGenerator** implementation notes:
- Entry block becomes method body with `long` locals.
- Each basic block becomes a labeled block: `block_0:`
- Terminators become `goto block_N;` or `if (...) goto block_N;`
- `Phi` becomes a ternary or switch expression selecting the correct incoming value based on which predecessor was taken.

**Tests to write:**

```csharp
[Test]
public async Task ExpressionCompiler_Add_MatchesLinqExpressionGenerator() {
    var ast = new Add(new Constant(3), new Constant(4));
    var module = IrPipeline.Emit(ast);

    var exprResult = new ExpressionCompiler().Compile(module);
    var linqResult = new LinqExpressionGenerator().Generate(ast);  // existing

    // Both produce System.Linq.Expressions.Expression<Func<long>>
    // Execute both and compare results
    var exprValue = exprResult.Compile().DynamicInvoke();
    var linqValue = linqResult.Compile().DynamicInvoke();
    await Assert.That(exprValue).IsEqualTo(linqValue);
}

[Test]
public async Task CSharpCodeGenerator_IfElse_GeneratesCompilableCode() {
    var ast = /* if (x > 0) x + 1 else x - 1 */;
    var module = IrPipeline.Emit(ast);
    var source = new CSharpCodeGenerator().Generate(module);

    // Verify the source contains expected keywords
    await Assert.That(source).Contains("goto");
    await Assert.That(source).Contains("if");
    // Optionally: compile via Roslyn and execute
}

[Test]
public async Task RingAnalysis_ComputesCorrectDepths() {
    var module = IrPipeline.Emit(/* branching expression */);
    RingAnalyzer.Run(module);
    // Verify RingMetadata is attached to Module
}
```

**Success criteria:**
- `ExpressionCompiler` produces equivalent output to `LinqExpressionGenerator` for all expression types.
- `CSharpCodeGenerator` generates syntactically valid C#.
- `RingAnalyzer` computes correct ring depths for multi-block CFGs.
- This step can be deferred indefinitely; it is additive, not a migration requirement.

---

**Cross-cutting rule for all steps:** After adding any `Emit` override, run `dotnet test` (or the TUnit equivalent). If a test fails, fix the `Emit` override before adding the next one. Never proceed to the next step with failing tests.

## 16. References

### New files created

| Directory / file | Contents |
|-----------------|----------|
| `Poly/Ir/TypeKind.cs` | `enum TypeKind` |
| `Poly/Ir/OpKind.cs` | `enum OpKind` with `static ResultType(TypeKind, TypeKind)` helper |
| `Poly/Ir/UnaryOpKind.cs` | `enum UnaryOpKind` |
| `Poly/Ir/Value.cs` | `sealed record Value` |
| `Poly/Ir/Instr.cs` | `abstract record Instr` base class |
| `Poly/Ir/BasicBlock.cs` | `sealed class BasicBlock` |
| `Poly/Ir/Module.cs` | `sealed class Module` |
| `Poly/Ir/CallSite.cs` | `sealed record CallSite` descriptor |
| `Poly/Ir/Instructions/Const.cs` | `sealed record Const` |
| `Poly/Ir/Instructions/BinOp.cs` | `sealed record BinOp` |
| `Poly/Ir/Instructions/UnaryOp.cs` | `sealed record UnaryOp` |
| `Poly/Ir/Instructions/LoadLocal.cs` | `sealed record LoadLocal` |
| `Poly/Ir/Instructions/StoreLocal.cs` | `sealed record StoreLocal` |
| `Poly/Ir/Instructions/Parameter.cs` | `sealed record Parameter` |
| `Poly/Ir/Instructions/Call.cs` | `sealed record Call` |
| `Poly/Ir/Instructions/AllocClosure.cs` | `sealed record AllocClosure` |
| `Poly/Ir/Instructions/LoadUpvalue.cs` | `sealed record LoadUpvalue` |
| `Poly/Ir/Instructions/StoreUpvalue.cs` | `sealed record StoreUpvalue` |
| `Poly/Ir/Instructions/AllocHeap.cs` | `sealed record AllocHeap` |
| `Poly/Ir/Instructions/Phi.cs` | `sealed record Phi` |
| `Poly/Ir/Terminators/Terminator.cs` | `abstract record Terminator` base |
| `Poly/Ir/Terminators/Goto.cs` | `sealed record Goto` |
| `Poly/Ir/Terminators/CondBranch.cs` | `sealed record CondBranch` |
| `Poly/Ir/Terminators/Ret.cs` | `sealed record Ret` |
| `Poly/Ir/Terminators/Throw.cs` | `sealed record Throw` |
| `Poly/Ir/EmissionContext.cs` | `sealed class EmissionContext` |
| `Poly/Ir/GenerationPass.cs` | `sealed class GenerationPass : INodeAnalyzer` |
| `Poly/Ir/GenerationPassExtensions.cs` | `.UseIrGeneration()` extension on `AnalyzerBuilder` |
| `Poly/Ir/ModuleMetadata.cs` | `sealed record ModuleMetadata : IAnalysisMetadata` |
| `Poly/Interpretation/Ir/VmLoweringPass.cs` | `sealed class VmLoweringPass : INodeAnalyzer` — wraps `UopCompiler` |
| `Poly/Interpretation/Ir/Backends/UopCompiler.cs` | IR → µop `LoweringResult` |
| `Poly/Interpretation/Ir/Backends/ExpressionCompiler.cs` | IR → `System.Linq.Expressions.Expression` (Step 12) |
| `Poly/Interpretation/Ir/Backends/CSharpCodeGenerator.cs` | IR → C# source text (Step 12) |
| `Poly/Ir/Passes/SsaTransform.cs` | SSA construction (§5.1) |
| `Poly/Ir/Passes/ConstantFolding.cs` | Constant folding (§5.2) |
| `Poly/Ir/Passes/Inliner.cs` | Inlining pass (§5.3) |
| `Poly/Interpretation/Ir/Passes/RingAnalyzer.cs` | Ring-depth computation (VM-backend-specific, §5.4) |

### Files modified

| File | Change |
|------|--------|
| `Poly/Syntax/Node.cs` | Add `virtual Value? Emit(EmissionContext ctx)` to abstract base |
| `Poly/Syntax/Nodes/Add.cs` | `override Emit` → `BinOp(OpKind.Add, left, right, Id)` |
| `Poly/Syntax/Nodes/Subtract.cs` | `override Emit` → `BinOp(OpKind.Sub, ...)` |
| `Poly/Syntax/Nodes/Multiply.cs` | `override Emit` → `BinOp(OpKind.Mul, ...)` |
| `Poly/Syntax/Nodes/Divide.cs` | `override Emit` → `BinOp(OpKind.Div, ...)` |
| `Poly/Syntax/Nodes/Constant.cs` | `override Emit` → `Const` or `AllocHeap` (non-numeric) |
| `Poly/Syntax/Nodes/Variable.cs` | `override Emit` → `LoadLocal(slot, kind, Id)` |
| `Poly/Syntax/Nodes/Assignment.cs` | `override Emit` → `StoreLocal` + reload |
| `Poly/Syntax/Nodes/Block.cs` | `override Emit` → emit children, scope management |
| `Poly/Syntax/Nodes/IfStatement.cs` | `override Emit` → `CondBranch` + then/else/merge blocks + `Phi` |
| `Poly/Syntax/Nodes/WhileLoop.cs` | `override Emit` → header/body/latch/exit blocks |
| `Poly/Syntax/Nodes/ForLoop.cs` | `override Emit` → init/cond/body/increment/exit blocks |
| `Poly/Syntax/Nodes/Lambda.cs` | `override Emit` → `AllocClosure` + nested function |
| `Poly/Syntax/Nodes/Invoke.cs` | `override Emit` → `Call` (external or closure) |
| *(remaining node types under `Poly/Syntax/Nodes/`)* | `override Emit` per type |
| `Poly/Interpretation/Analysis/LoweringPrep/UopGenerationPass.cs` | Deprecated after Step 11 (700-line switch replaced) |
| `Poly/Interpretation/Analysis/LoweringPrep/LoweringPrepPass.cs` | Partially subsumed by block-structured IR |
| `Poly/Interpretation/Vm/Lowering.cs` | Preserved for legacy path; ring heuristic superseded |
| `Poly/Interpretation/Vm/ProgramCompiler.cs` | Preserved — unchanged API |
| `Poly/Interpretation/Vm/CompilationContext.cs` | Preserved — ring register locals unchanged |
| `Poly/Interpretation/Vm/Closure.cs` | Preserved — closure model unchanged |

### Test files

| File | Contents |
|------|----------|
| `Poly.Tests/TestHelpers/IrPipeline.cs` | `static class IrPipeline { static Module Emit(Node); static object Execute(Node); }` — test helpers for IR pipeline |
| `Poly.Tests/Ir/IrTypeTests.cs` | Type creation and `OpKind.ResultType` tests (Step 1) |
| `Poly.Tests/Ir/GenerationPassTests.cs` | `GenerationPass` smoke tests (Step 2) |
| `Poly.Tests/Ir/IrCrossValidationTests.cs` | Cross-validation: old vs new pipeline per node type (Steps 3–10) |
| `Poly.Tests/Ir/SsaTests.cs` | SSA construction tests (§5.1.10) |
| `Poly.Tests/Ir/ExpressionCompilerTests.cs` | `ExpressionCompiler` vs `LinqExpressionGenerator` (Step 12) |
| `Poly.Tests/Ir/CSharpCodeGeneratorTests.cs` | `CSharpCodeGenerator` golden-file tests (Step 12) |
| `Poly.Tests/Interpretation/VmCorrectnessTests.cs` | Authoritative cross-validation source (existing, extended) |