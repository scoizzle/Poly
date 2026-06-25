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
- **Typed, but minimally**: IR types are a small enum (`Word`, `Boolean`, `Handle`, `Void`), kept CLR-agnostic. `Word` represents one machine word (64-bit signed integer; maps to `long` in CLR, `i64` in WASM, etc.). Float and integer operations are distinguished via `OpKind`, not types. The CLR adaptation layer maps `Handle` to `object?`. This minimal set is intentional — new kinds (e.g. `Pointer`, `Struct`) are added only when a concrete backend requires them and cannot express the semantics through the existing four. Backends own final type layout.
- **Scoped locals fallback**: for variable reassignment (`x = x + 1`), the IR allows mutable local slots accessed via `LoadLocal`/`StoreLocal` as an escape from SSA purity. The SSA construction pass converts these to SSA values with φ nodes at join points.

### 3.2 Core types

```csharp
// ── Values ──────────────────────────────────────────────────────────
// Intentionally minimal — only four kinds. Backends perform final type layout.
// New kinds added only when a concrete backend cannot express the semantics
// through the existing four (Word, Boolean, Handle, Void).
public enum TypeKind { Word, Boolean, Handle, Void }

/// <summary>Opaque handle to a value produced by an IR instruction.
/// Carries its type and a reference back to the defining instruction.
/// Index is the SSA version number (0 = first definition, 1 = second, etc.)
/// assigned during the renaming pass; before SSA it is always 0.</summary>
public sealed record Value(Instr Definition, TypeKind Kind, int Index);

// ── Instructions ────────────────────────────────────────────────────
public abstract record Instr(TypeKind ResultType, NodeId? Source);

public sealed record Const(long Value, TypeKind Kind, NodeId? Source) : Instr(Kind, Source);
public sealed record BinOp(OpKind Op, Value Left, Value Right, NodeId? Source) : Instr(Op.ResultType(Left.Kind, Right.Kind), Source);  // instance call on OpKind
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
    public List<Module> ExportedFunctions { get; } = new();   // nested function bodies (lambdas)
    public List<object?> HeapConstants { get; } = new();     // indexed by handle
    public List<CallSite> CallSites { get; } = new();        // indexed by CallSiteIndex; mirrors VmProgram.CallSites
    public List<CaptureLayout> CaptureLayouts { get; } = new();  // closure capture layouts (§8.1)
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
- **Result types from operators**: IR instruction result types are derived from their operands and operator kind where possible (`BinOp` resolves via `Op.ResultType(Left.Kind, Right.Kind)`, `UnaryOp` inherits `Operand.Kind`). For `Call`, the result type comes from the resolved member in the call-site table (section 9.2). `Op.ResultType` is a helper method associated with `OpKind`, defined alongside it under `Poly/Ir/` at implementation time. The current encoding as a static extension on the enum is fine for the initial set (arithmetic ops → `Word`, comparisons → `Boolean`). If the operator set grows (e.g. `FAdd` for float), `OpKind` can be promoted to a `readonly record struct` with an instance method — the call sites are unchanged. Float and integer operations are distinguished at the `OpKind` level (`Add` vs `FAdd` in future float support), not at the type level — the IR already maps both to `Word` because the VM stores everything in 64-bit slots. This keeps the IR self-describing without requiring a separate type environment.

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

    // Create a new block and split the current block. Sets CurrentBlock to the
    // new block. The *previous* block is left without a terminator — the caller
    // (node.Emit) is responsible for setting it (e.g. CondBranch, Goto).
    // Throws InvalidOperationException if CurrentBlock already has a terminator
    // (which would mean the preceding emit logic already terminated the block,
    // making the split ambiguous).
    public BasicBlock SplitBlock(string suffix);

    // Declare a mutable local slot (for variables). Returns slot index.
    public int DeclareLocal(string name, TypeKind kind);
}
```

### 4.4 `GenerationPass` — single pass, internal steps

The `GenerationPass` is registered as one `INodeAnalyzer` but internally runs both IR emission and IR transform passes as ordered steps. This avoids splitting IR work across multiple `INodeAnalyzer` implementations (which would abuse metadata to pass the `Module` around) while keeping each step as a pure `Module → Module` function that can be tested in isolation.

```csharp
public sealed class GenerationPass : INodeAnalyzer {
    // Per-step toggles for testing and incremental compilation.
    // EnableSsa is false by default — the VM backend works without SSA.
    // SsaTransform, ConstantFolding, Inliner are in Poly/Ir/Passes/;
    // they are added to the pipeline once implemented.
    public bool EnableSsa { get; init; } = false;
    public bool EnableConstFolding { get; init; } = false;
    public bool EnableInlining { get; init; } = false;

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
                                    ExceptionEdgeLowering (step 2, no-op by default)
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
[IrEmission]           node.Emit(ctx) per AST node  → Module
     │
     ▼
[ExceptionEdgeLowering]  Materialize implicit try/catch CFG edges
     │
     ▼
[SsaTransform]           StoreLocal/LoadLocal → explicit Phi + SSA values
     │
     ▼
[ConstantFolding]        BinOp(Const, Const) → Const
     │
     ▼
[Inlining]               Call(inlinable) → inline body
```

All passes run in `GenerationPass.Analyze()` (section 4.4). Backend-specific passes (e.g. `RingAnalysis` for the VM backend) run later, outside `GenerationPass`, as separate `INodeAnalyzer` instances or adapter steps. `ExceptionEdgeLowering` is a no-op when no `TryMarker` instructions exist (the common case).

### 5.1 SSA Construction Pass

The SSA pass is the most algorithmically significant transform in the pipeline. It takes the initial IR (which uses `LoadLocal`/`StoreLocal` for mutable slots) and converts it to pure SSA form where every `Value` has exactly one definition and an arbitrary number of uses.

#### 5.1.1 Prerequisites

The `Module` must be in a well-formed state before SSA runs:

1. **Every `BasicBlock` has a terminator** — `Goto`, `CondBranch`, `Ret`, or `Throw`. Dead-end blocks (no terminator) are rejected.
2. **All local slot accesses are explicit** — `LoadLocal(slot)` and `StoreLocal(slot, val)`. The slot indices are dense per `Module.MaxLocalSlots`.
3. **No implicit dataflow** — all value dependencies are through `Value` references. The only mutable state is local variables via `LoadLocal`/`StoreLocal`.

The pass runs **immediately after IR emission**, before constant folding and inlining. The canonical pass order is `IrEmission → ExceptionEdgeLowering → SsaTransform → ConstantFolding → Inlining` (see section 5). SSA runs first so the subsequent optimization passes can rely on explicit def-use chains and `Phi` nodes rather than walking mutable slot writes. Constant folding and inlining then operate on SSA form, producing equivalent but smaller SSA for downstream consumers.

#### 5.1.1a Exception-edge lowering pre-pass

`TryCatchFinally` / `TryBody` nodes generate implicit control-flow edges: any instruction in a `try` body can transfer control to a `catch` or `finally` block. These edges are not visible in the block-terminator graph — they must be materialized before SSA construction, otherwise phi placement will miss definitions reaching catch blocks.

A dedicated pre-pass walks the `Module` before `BuildCfg()`:

1. Find all blocks annotated with a `TryMarker` sentinel instruction (emitted by `TryCatchFinally.Emit`).
2. For each such block, determine its catch/finally target blocks from the `TryMarker`.
3. Populate `Module.ExceptionEdges : Dictionary<BasicBlock, List<BasicBlock>>` mapping each try-body block to its handler blocks.
4. `BuildCfg` then merges `ExceptionEdges` into the `Successors`/`Predecessors` maps.

The pre-pass is a separate step in `SsaTransform.Run()` before `BuildCfg`, not a separate `INodeAnalyzer`. It is a no-op when no `TryMarker` instructions exist.

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
> - **Use-lists** (recommended): maintain a `Dictionary<Value, List<(Instr, int operandIndex)>>` side-table on `Module` mapping each `Value` to every instruction+operand-index that references it. After `LoadLocal` removal, walk the use-list and rewrite each consumer's operand to the replacement `Value`. The use-list itself is populated during IR emission — every `EmitChild` records the dependency when the child's `Value` appears as an operand. This is ≈30 lines of code and does not change the algorithm's structure.
> - **Identity instructions** (fallback): keep `LoadLocal` as a "mov" identity that simply passes through the current SSA value. A later copy-propagation pass folds them away. Simpler to implement but leaves noise in the IR that downstream passes must skip.
>
> The rename pseudocode above shows the *intent* (remove LoadLocal, consumers reference the renamed Value directly). Whichever strategy is chosen, the `replacements` list must also trigger a rewrite pass on the block's instructions and successor Phis so that no dangling `Value` references survive.

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
| **Exception-handling edges** | `TryCatchFinally` / `TryBody` nodes generate implicit control-flow edges: any instruction in a `try` body can transfer control to a `catch` or `finally` block. These edges must be added to the CFG **before** SSA construction (otherwise phi placement will miss definitions reaching catch blocks). The `Emit` for `TryCatchFinally` emits a `TryMarker` sentinel instruction identifying the target handler blocks. The exception-edge lowering pre-pass (§5.1.1a) then materializes these as explicit successor edges in `Module.ExceptionEdges` before `BuildCfg`. **Deferred**: the first implementation may throw `NotSupportedException` from `TryCatchFinally.Emit`. |

#### 5.1.10 Testing strategy for the SSA pass

```csharp
[Test]
public async Task Ssa_StraightLine_NoPhis() {
    // WHY: Straight-line code (one definition, one use, no branches) must
    // produce zero Phi nodes. If Phi appears here, the insert-phi pass is
    // inserting at non-join points — a sign that dominance frontier
    // computation is considering all slots rather than just multi-def slots.
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
    // WHY: An if/else with variable assignment in both branches requires
    // exactly one Phi at the merge block with two incoming values (one per
    // predecessor). This validates InsertPhis positions the Phi correctly
    // and the rename walk fills the Incoming array in the right predecessor
    // order.
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
    // WHY: A loop with a loop-carried variable (x = x + 1) requires a Phi
    // at the loop header — the first incoming value is the initial value
    // before the loop, the second is the value carried by the back-edge.
    // This tests that the dominance frontier of the loop body correctly
    // includes the header block (which causes InsertPhis to place a Phi there).
    // while (x < 10) x = x + 1; return x
    var ast = /* ... */;
    var module = Emit(ast);
    SsaTransform.Run(module);
    var header = module.Blocks[1];  // loop header
    await Assert.That(header.Instructions.OfType<Phi>()).HasCount().EqualTo(1);
}

[Test]
public async Task Ssa_CrossValidateWithVm() {
    // WHY: SSA transformation must preserve program semantics. The only
    // way to prove this is to compile the SSA IR to the VM and compare
    // its output against the non-SSA pipeline. If the result differs, the
    // SSA pass introduced a semantic error (e.g., a Phi incoming value
    // was filled from the wrong predecessor, or LoadLocal removal
    // substituted the wrong SSA version).
    // Compile with and without SSA, execute both, compare results
    var ast = /* complex expression with branches and loops */;
    var module = Emit(ast);
    SsaTransform.Run(module);

    var lowered = new UopCompiler().Compile(module);
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

Takes the SSA IR and computes eval-stack ring depths for each block+instruction. Attaches `RingMetadata` as a side table on `Module`. This pass exists only for the VM backend.

## 6. Backends (IR → Output)

Backends are self-contained compilers that consume a `Module` and produce output. The production backend is the VM µop compiler. The other output forms (`LinqExpressionGenerator`, `CSharpGenerator`, Mermaid) continue to operate on the **AST** via their existing paths — they are not ported to the IR unless a concrete consumer later demands it.

```csharp
public sealed class UopCompiler {          // IR → µops (only production backend)
    public LoweringResult Compile(Module module);
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

### 7.5 C# translation (illustrative)

The IR can be rendered as C# for debugging. This is **not a maintained backend** — shown here for intuition only:

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
- `StoreUpvalue(idx, val)` writes to a capture from within a lambda body.

### 8.1 Capture layout

To avoid the backend having to recompute which slots are captured and in what order, `Module` carries a capture layout table:

```csharp
public sealed record CaptureLayout(int FuncIndex, List<CapturedSlot> Slots);
public sealed record CapturedSlot(int OuterSlotIndex, TypeKind Kind);
```

`Module.CaptureLayouts : List<CaptureLayout>` is populated during IR emission alongside each `AllocClosure`. The VM backend reads it to build the closure's `Captures` array.

### 8.2 SSA treatment of captured slots

The SSA pass must **not** eliminate captured slots — they are aliased mutable state shared among potentially multiple closures and the outer function. Renaming a captured slot would produce a local SSA name that no other closure can see, breaking mutation.

Concretely:
- Slots marked as captured (via `Scope.IsCaptured(slot)`) are exempt from SSA renaming.
- `LoadLocal`/`StoreLocal` for captured slots **remain** in the IR after the SSA pass — they are not removed.
- `LoadUpvalue`/`StoreUpvalue` are emitted by `Variable.Emit` when the variable is accessed from within a lambda that captures it. These are also left untouched by the SSA pass.
- The `UopCompiler` maps both `LoadLocal` (for captured slots) and `LoadUpvalue` to µop `LoadCapture` — the distinction is only relevant during `Lambda.Emit` for the capture index.

At the VM backend, these map to the existing:
- `AllocClosure` µop + `Closure` class (`Poly/Interpretation/Vm/Closure.cs`)
- `LoadCapture`/`StoreCapture` µops (which read from `Closure.Captures`)
- `HandleAllocClosure`/`HandleLoadUpvalue`/`HandleStoreUpvalue` in `Vm.cs`

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

> **Decision record:** The rationale for deferring incremental IR compilation is documented in `docs/decisions/2026-06-XX-incremental-ir-compilation.md` (to be created when the topic is next revisited). The trigger for revisiting: concrete profiling data showing a per-function IR rebuild is a bottleneck for interactive editing of large functions.

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

### Phase 5: *(reserved)*

No further backend phases planned. The AST-based `LinqExpressionGenerator`, `CSharpGenerator`, and Mermaid output continue to work unchanged; they are not ported to the IR unless a concrete consumer demands it.

## 12. Testing Strategy

### Cross-validation tests (mandatory)

For every test case in `Poly.Tests/Interpretation/VmCorrectnessTests.cs`:

```csharp
[Test]
public async Task BinaryAdd_CrossValidate() {
    // WHY: The foundational correctness invariant — both pipelines produce
    // identical results for the same input. Every node type added to the
    // new pipeline must have a cross-validation test; this is the template
    // that all others follow. If this fails, either the emitter or the
    // UopCompiler has a bug.
    var node = new Add(new Constant(3), new Constant(4));

    // Old pipeline — build analyzer with old passes, execute manually
    var oldAnalyzer = new AnalyzerBuilder()
        .UseTypeAndMemberResolver().UseVariableScopeValidator()
        .UseLoweringPreparation().UseUopGeneration()
        .Build();
    var oldResult = oldAnalyzer.Analyze(node);
    var oldLowered = Lowering.Lower(node, oldResult);
    var oldProgram = ProgramCompiler.Compile(oldLowered);
    using var oldState = new VmState(oldProgram);
    Vm.Execute(oldState);
    var oldValue = oldState.Stack.Pop();

    // New pipeline — use IrPipeline helper (defined in Step 3)
    var newValue = IrPipeline.Execute(node);

    await Assert.That(newValue).IsEqualTo(oldValue);
}
```

### IR-level tests

```csharp
[Test]
public async Task IfStatement_ProducesCorrectBlocks() {
    // WHY: Structural invariant — an if/else expression must produce exactly
    // 4 blocks (entry, then, else, merge) with a CondBranch in the entry
    // and a Phi at the merge. A different block count means the emitter
    // is creating or skipping blocks (e.g., merging the then block into
    // the entry, or omitting the merge for single-expression bodies).
    // This is the cheapest possible check: block count + terminator type,
    // no IR payload inspection needed.
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

> **Note:** This step creates ~25 files and can be split into Step 1a (core types: `TypeKind`, `OpKind`, `Value`, `Instr`, `BasicBlock`, `Module`, `CallSite`) and Step 1b (all instruction and terminator records) if a smaller review surface is preferred. Both sub-steps have the same success criteria and no existing code changes.

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
| `Poly/Ir/Module.cs` | `sealed class Module { List<BasicBlock> Blocks; List<Module> ExportedFunctions; List<object?> HeapConstants; List<CallSite> CallSites; List<CaptureLayout> CaptureLayouts; int MaxLocalSlots; }` |
| `Poly/Ir/CallSite.cs` | `sealed record CallSite(string MethodName, TypeKind ReturnType, int ArgCount)` |

**Files to modify:** None.

**Tests to write** (`Poly.Tests/Ir/IrTypeTests.cs`):

```csharp
[Test]
public async Task CanCreateModule() {
    // WHY: Module is the top-level IR container. If it cannot be constructed
    // with a named block, nothing in the IR pipeline works. This is the
    // degenerate-base-case smoke test — catch class-loading failures,
    // constructor signature changes, or missing List<T> initialization.
    var module = new Module();
    var block = new BasicBlock("entry");
    module.Blocks.Add(block);
    await Assert.That(module.Blocks).HasCount().EqualTo(1);
}

[Test]
public async Task CanCreateInstructions() {
    // WHY: Every instruction subtype must be constructible and must carry
    // its ResultType correctly. Const(42, Word) is the simplest instruction
    // and exercises the Instr base-class constructor chain, the record's
    // positional parameter binding, and the TypeKind enum. If Const cannot
    // be instantiated, no IR module can be built.
    var instr = new Const(42, TypeKind.Word, null);
    await Assert.That(instr.Value).IsEqualTo(42);
    await Assert.That(instr.ResultType).IsEqualTo(TypeKind.Word);
}

[Test]
public async Task OpKindResultType_ComparisonsReturnBoolean() {
    // WHY: The type system's most important classification rule: comparison
    // operators (Gt, Lt, Eq, etc.) always produce Boolean regardless of
    // their operand types. If this were to return Word, consumers of
    // comparison results (e.g., CondBranch) would accept an integer rather
    // than a boolean, allowing nonsensical branch conditions without
    // type feedback.
    var result = OpKind.Gt.ResultType(TypeKind.Word, TypeKind.Word);
    await Assert.That(result).IsEqualTo(TypeKind.Boolean);
}

[Test]
public async Task OpKindResultType_ArithmeticReturnsWord() {
    // WHY: The inverse of the above: arithmetic operators (Add, Sub, etc.)
    // always produce Word. If this were to return Boolean or Void,
    // arithmetic expressions could not chain (e.g., (a + b) * c would
    // produce a non-Word intermediate that BinOp rejects).
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

**Test helper to create:**

| File | Contents |
|------|----------|
| `Poly.Tests/TestHelpers/IrPipeline.cs` | `static class IrPipeline { static Module Emit(Node node); static object Execute(Node node); }` — convenience wrappers: build analyzer with `.UseIrGeneration()`, extract `ModuleMetadata`, call `new UopCompiler().Compile(module)`, compile & execute via VM. Returns the result from the VM stack. Design note: `Execute` relies on `UopCompiler` which does not yet exist in Steps 1–2; it should be added as a stub or marked `[Obsolete("Available after Step 3")]` until the `UopCompiler` is implemented. |

**Files to modify:**

| File | Change |
|------|--------|
| `Poly/Syntax/Node.cs` | Add `public virtual Value? Emit(EmissionContext ctx) => null;` to the abstract `Node` record |

**Tests to write** (`Poly.Tests/Ir/GenerationPassTests.cs`):

```csharp
[Test]
public async Task GenerationPass_ProducesEmptyModule_ForUnknownNode() {
    // WHY: Before any Emit override exists, the pass must still produce a
    // valid (empty) Module. This validates that GenerationPass.Analyze()
    // correctly handles the base-case: node.Emit returns null, the pass
    // creates a Ret(null) terminator, and ModuleMetadata is stashed.
    // Regression: if the pass crashes for a node with no overridden Emit,
    // no cross-validation can proceed.
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
    // WHY: During migration the old pipeline must continue working even
    // when the new pass is registered (but not yet the default). This
    // guarantees that adding .UseIrGeneration() to an analyzer builder
    // does not corrupt the analysis context or interfere with metadata
    // that old passes consume. If this broke, the parallel-operation
    // migration strategy would be infeasible.
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

### Step 3: Override `Emit` on `Constant` and `Add` — first cross-validation

**Goal:** The first real `Emit` overrides work end-to-end: AST node → IR → `UopCompiler` → `LoweringResult` → `ProgramCompiler` → `Vm.Execute` → correct result. `Constant` must come first because `Add` depends on its children emitting valid IR.

**Files to create:**

| File | Contents |
|------|----------|
| `Poly/Interpretation/Ir/Backends/UopCompiler.cs` | Walks blocks in dominator-tree order, emits µops per instruction. Minimal implementation: `Const` → `LoadConst`, `BinOp` → `BinOp`, `Ret` → `ReturnOp`. Everything else throws `NotSupportedException` (added in later steps). |

> **Note:** `IrPipeline` test helper was created in Step 2. It is functional starting in Step 3 (the `UopCompiler` now exists).

**Files to modify:**

| File | Change |
|------|--------|
| `Poly/Syntax/Nodes/Constant.cs` | `override Emit`: emit `Const(value, kind, Id)`. For `int`/`long` values, kind is `Word`. For `bool`, convert to `0L` or `1L`. For `double`, store as `BitConverter.DoubleToInt64Bits(value)`. For `string` and other reference types, register in `Module.HeapConstants` and emit `AllocHeap(handle, Id)`. |
| `Poly/Syntax/Nodes/Add.cs` | `override Emit`: emit children, emit `new BinOp(OpKind.Add, left!, right!, Id)` |

> **`Parameter` vs `LoadArg`:** The IR uses `Parameter` (a definition instruction in the entry block) to declare a function argument's initial SSA value. The `UopCompiler` maps `Parameter` to µop `LoadArg`. However, after the SSA rename pass (§5.1.7), `Parameter` instructions are **removed** from the entry block — the rename stack seeds the initial value from `Parameter` and consumers reference that SSA value directly. The `UopCompiler` must still emit `LoadArg` for function entry parameters (slot indices < parameter count) even when SSA is enabled, because the `LoadArg` µop is the only entry point for passing arguments into the VM frame. This is handled by having `UopCompiler` check `Module.MaxLocalSlots` against a separate parameter count, or by tracking which slots are parameters during emission.

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
    // WHY: The first end-to-end proof that the new pipeline produces the
    // same result as the old one for a trivial expression. Every subsequent
    // node type's cross-validation test follows this exact pattern.
    // Failure here means either Constant.Emit, Add.Emit, or UopCompiler
    // has a fundamental bug — no later test can pass until this does.
    var node = new Add(new Constant(3), new Constant(4));

    // Old pipeline — build analyzer with old passes, execute manually
    var oldAnalyzer = new AnalyzerBuilder()
        .UseTypeAndMemberResolver().UseVariableScopeValidator()
        .UseLoweringPreparation().UseUopGeneration()
        .Build();
    var oldResult = oldAnalyzer.Analyze(node);
    var oldLowered = Lowering.Lower(node, oldResult);
    var oldProgram = ProgramCompiler.Compile(oldLowered);
    using var oldState = new VmState(oldProgram);
    Vm.Execute(oldState);
    var oldValue = oldState.Stack.Pop();

    // New pipeline
    var newAnalyzer = new AnalyzerBuilder()
        .UseTypeAndMemberResolver().UseVariableScopeValidator()
        .UseIrGeneration()
        .Build();
    var newResult = newAnalyzer.Analyze(node);
    var module = newResult.GetMetadata<ModuleMetadata>(node).Module;
    var newLowered = new UopCompiler().Compile(module);
    var newProgram = ProgramCompiler.Compile(newLowered);
    using var newState = new VmState(newProgram);
    Vm.Execute(newState);
    var newValue = newState.Stack.Pop();

    await Assert.That(newValue).IsEqualTo(oldValue);  // both produce 7
}

[Test]
public async Task Add_ProducesCorrectIr() {
    // WHY: Validate the IR shape independently of the VM backend.
    // An Add with two integer constants must produce exactly 3 instructions
    // (Const, Const, BinOp) in a single block. If extra instructions appear
    // (e.g., LoadLocal for intermediate temps) or the BinOp is missing,
    // the emitter is producing incorrect IR even if cross-validation somehow
    // happens to pass.
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
- `Const("hello")` produces `AllocHeap` and registers in `Module.HeapConstants`.
- Only `Add` and `Constant` have `Emit` overrides; all other node types still go through the old pipeline.

---

### Step 4: `UopCompiler` — complete µop mapping table + block ordering

**Goal:** The `UopCompiler` has a dispatch entry for every IR instruction type defined in §3.2. Types not yet reachable throw `NotSupportedException` with a message identifying which step adds them. This ensures the dispatch structure is in place before the emitters arrive. Block ordering (dominator-tree DFS) and label resolution (PC assignment) are implemented and tested with a synthetic multi-block module.

> **`LoweringResult` contract:** The `UopCompiler.Compile(module)` returns a `LoweringResult` — the existing type from `Poly/Interpretation/Vm/Lowering.cs`. It carries `List<Instruction> Instructions` (the µop sequence) and call-site metadata. `ProgramCompiler.Compile` already consumes `LoweringResult`. No new output type is needed. For testing, wrap the result: `new ProgramCompiler().Compile(new UopCompiler().Compile(module))` produces a `VmProgram`.

**Files to modify:**

| File | Change |
|------|--------|
| `Poly/Interpretation/Ir/Backends/UopCompiler.cs` | Complete the µop mapping table for all instruction types. Dispatch entries for the 14 types not yet reachable throw `NotSupportedException("Reachable from Step N")`. Add block-ordering logic: dominator-tree DFS determines block order; assign contiguous PC ranges per block; resolve `Goto.Target` and `CondBranch.ThenTarget`/`ElseTarget` to absolute PCs. |

**Complete µop mapping table:**

| IR instruction | µop emitted | Notes |
|---------------|-------------|-------|
| `Const` | `LoadConst` | |
| `BinOp` | `BinOp` | Map `OpKind` to µop operator enum |
| `UnaryOp` | `UnaryOp` (Neg/Not) | |
| `LoadLocal` | `LoadSlot(slotIndex)` | |
| `StoreLocal` | `StoreSlot(slotIndex)` + `ConsumedFromPcs` | |
| `Parameter` | `LoadArg(slotIndex)` | Loads function argument into eval stack. Required even with SSA — rename eliminates `Parameter` but `LoadArg` is the only entry point. |
| `Call` (IsExternal=true) | `Call(callSiteIndex)` | Resolve from `CallSites` table |
| `Call` (IsExternal=false) | `CallClosure` + closure dispatch | |
| `AllocClosure` | `AllocClosure` µop | |
| `LoadUpvalue` | `LoadCapture(upvalueIndex)` | |
| `StoreUpvalue` | `StoreCapture(upvalueIndex)` | |
| `AllocHeap` | `LoadHeapConst(handleIndex)` | |
| `Phi` (slot-level) | `PhiMarker([altPcs], sourcePc)` | `altPcs` = PCs of producing instructions; `sourcePc` = PC of incoming Jump. Resolved via ring analyzer. |
| `Phi` (value-level) | `PhiMarker([altPcs], sourcePc)` | Same |
| `Goto` | `Jump(targetPc)` | |
| `CondBranch` | `BranchIfFalse(targetPc)` | Condition on top of eval stack |
| `Ret` | `ReturnOp` | |
| `Throw` | `ThrowOp` | |

**Tests to add:**

```csharp
[Test]
public async Task UopCompiler_Const_EmitsLoadConst() {
    // WHY: The UopCompiler's lowest-level mapping must work correctly —
    // a Const IR instruction must become a LoadConst µop with the correct
    // value. If this fails, the compiler cannot even handle literals,
    // making every backend test unresolvable.
    var module = BuildModule(new Const(42, TypeKind.Word, null));
    var result = new UopCompiler().Compile(module);
    await Assert.That(result.Instructions[0]).IsTypeOf<LoadConst>();
    await Assert.That(((LoadConst)result.Instructions[0]).Value).IsEqualTo(42);
}

[Test]
public async Task UopCompiler_BlockOrdering_PreservesDominatorOrder() {
    // WHY: Multi-block CFGs must be emitted in dominator-tree DFS order
    // (entry, then, else, merge) — not source order or reverse-post-order.
    // Wrong ordering breaks the VM's predecessor resolution for Phi
    // markers and branch-target PC offsets. This test constructs a
    // synthetic 4-block CFG and asserts the µop PC sequence matches
    // dominator-tree order.
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
    // WHY: Integer constants are the most common leaf node. Constant.Emit
    // must produce a Const instruction (not AllocHeap — that's for strings
    // and reference types). If it produces the wrong instruction type, the
    // VM backend would emit a LoadHeapConst instead of LoadConst, failing
    // to push the value onto the eval stack.
    var module = IrPipeline.Emit(new Constant(42));
    await Assert.That(module.Blocks[0].Instructions[0]).IsTypeOf<Const>();
}

[Test]
public async Task Constant_String_EmitsAllocHeap() {
    // WHY: Non-numeric constants (strings, CLR objects) must follow a
    // completely different code path: register in Module.HeapConstants
    // and emit AllocHeap(handleIndex). This validates that Constant.Emit
    // correctly distinguishes numeric from reference types at the IR level.
    // If a string constant were emitted as Const, the VM would try to
    // store it as a long in the eval stack — data corruption.
    var module = IrPipeline.Emit(new Constant("hello"));
    var alloc = module.Blocks[0].Instructions[0];
    await Assert.That(alloc).IsTypeOf<AllocHeap>();
    await Assert.That(module.HeapConstants[(int)((AllocHeap)alloc).HandleIndex])
        .IsEqualTo("hello");
}

[Test]
public async Task Variable_CrossValidate() {
    // WHY: Variable references must resolve to the correct local slot and
    // produce a LoadLocal or (in lambdas) LoadUpvalue. Cross-validating
    // against the old pipeline catches slot-index mismatches, scope-
    // resolution bugs, and missing capture detection.
    // Build a lambda: (x) => x + 1
    // Old pipeline vs new pipeline — both return x+1 for a given x
}

[Test]
public async Task Parameter_EmitsParameterInstruction() {
    // WHY: Function parameters must be declared as Parameter instructions
    // in the entry block with the correct slot index and TypeKind. If
    // parameters are missing from the IR, the SSA pass cannot seed their
    // initial values and the rename walk will treat LoadLocal(slot) for
    // the argument as an uninitialized read — producing wrong results.
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
    // WHY: Conditional branching is the first node type that produces a
    // multi-block CFG with a Phi merge. If this cross-validation fails,
    // the CondBranch emitter, block-splitting logic, or Phi instruction
    // has a bug — and every subsequent control-flow construct (loops,
    // exception handling) will also be broken. This is the most important
    // single cross-validation test in the plan.
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
    // WHY: Validates the control-flow topology independently of execution.
    // An if/else expression must produce exactly 4 blocks (entry, then,
    // else, merge) with a CondBranch at entry and a Phi at merge. If the
    // block count is wrong or the terminators are missing, the UopCompiler
    // will misorder or mislabel blocks, causing runtime branch errors.
    var module = IrPipeline.Emit(ifAst);
    await Assert.That(module.Blocks).HasCount().EqualTo(4);  // entry, then, else, merge
    await Assert.That(module.Blocks[0].Terminator).IsTypeOf<CondBranch>();
    await Assert.That(module.Blocks[3].Instructions).Any(i => i is Phi);
}

[Test]
public async Task IfStatement_PhiIncomingCount_MatchesPredecessorCount() {
    // WHY: Every Phi's Incoming array must have exactly one entry per
    // predecessor block. If a predecessor is missing, the Phi selects
    // a null/undefined value at runtime. If an extra entry exists, the
    // Incoming array is misaligned. This is the minimal invariant check
    // for Phi correctness — no need to inspect individual values.
    // For an if/else, the merge block has 2 predecessors (then, else).
    // The value-level Phi at merge must have exactly 2 incoming values.
    var module = IrPipeline.Emit(ifAst);
    var phis = module.Blocks.SelectMany(b => b.Instructions).OfType<Phi>();
    foreach (var phi in phis)
        await Assert.That(phi.Incoming).HasCount().EqualTo(2);
}

[Test]
public async Task IfStatement_NoElse_EmitsUnitForElseBranch() {
    // WHY: An if without an else clause (void-valued) must still produce
    // a valid merge Phi — but the else side contributes no meaningful
    // value. This tests that ElseBody.Emit returning null is handled
    // correctly (e.g., by substituting a unit/void constant instead of
    // crashing or leaving the Incoming slot null, which would cause a
    // NullReferenceException in the UopCompiler).
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
    // WHY: A block with multiple statements must produce the last statement's
    // value as its result (the "block expression" semantics). If Block.Emit
    // returns the first child's value instead, or crashes after emitting
    // non-last children, compound expressions inside blocks produce wrong
    // results. This is the simplest multi-statement test: two statements,
    // the first being an assignment (void), the second an addition.
    // { x = 1; x + 2 }  → result is 3
}

[Test]
public async Task Block_DeclaresLocalVariable() {
    // WHY: Local variable declarations within a block must allocate a slot
    // and update Module.MaxLocalSlots. If MaxLocalSlots remains 0 after
    // emitting a block with a variable, DeclareLocal is not being called
    // or Module.MaxLocalSlots is not being incremented — which would cause
    // all local-variable uses in that block to read from slot 0 (the first
    // parameter) instead of their own slot.
    // { var y = 5; y * 2 }  → declares slot, stores 5, loads y, multiplies
    var module = IrPipeline.Emit(blockAst);
    await Assert.That(module.MaxLocalSlots).IsGreaterThan(0);
}

[Test]
public async Task Assignment_CrossValidate() {
    // WHY: Variable assignment is the primary source of mutable state.
    // Assignment.Emit must produce StoreLocal (to write the value) followed
    // by a reload (to make the assignment an expression). If the reload is
    // missing, the assignment's value would be Void and chained expressions
    // like (x = 5) + 1 would fail. Cross-validation catches this.
    // x = 42; return x
    // Both pipelines produce 42
}

[Test]
public async Task Assignment_Chain_CrossValidate() {
    // WHY: Chained assignment (a = b = 10) requires the RHS assignment to
    // produce the value 10 as its expression result, which then becomes the
    // value stored to the LHS. If Assignment.Emit fails to forward the
    // stored value, a = b = 10 stores null/0 to a while correctly assigning
    // b = 10. Cross-validation would show a=0, b=10 instead of a=10, b=10.
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

**Goal:** All three loop constructs (`WhileLoop`, `DoWhileLoop`, `ForLoop`) produce correct block-structured IR with `Phi` at loop headers. The canonical loop pattern uses 4 blocks per loop (entry, header, body, exit) where the last instruction in the body serves as the latch (jumps back to header). The header block contains the `CondBranch` that either enters the body or exits the loop.

**Files to modify:**

| File | Change |
|------|--------|
| `Poly/Syntax/Nodes/WhileLoop.cs` | `override Emit`: create header/body/latch/exit blocks (4 blocks). Emit `CondBranch` at header. Body emits children; last instruction of body is `Goto(header)` (the latch). Exit is after the loop. |
| `Poly/Syntax/Nodes/DoWhileLoop.cs` | `override Emit`: body block first, then condition block with `CondBranch` back to body. |
| `Poly/Syntax/Nodes/ForLoop.cs` | `override Emit`: initializer in entry (wrapped in a new scope for the loop variable), then same as `WhileLoop` with increment block at the bottom of the body before the latch. |
| `Poly/Syntax/Nodes/Break.cs` | `override Emit`: `Goto` to the exit block of the nearest enclosing loop (tracked via `EmissionContext.Scope`). |
| `Poly/Syntax/Nodes/Continue.cs` | `override Emit`: `Goto` to the latch block of the nearest enclosing loop. |

**EmissionContext additions:**

- `Scope` tracks loop context: `LoopInfo? CurrentLoop` with `BasicBlock ExitBlock`, `BasicBlock LatchBlock`. Set when entering a loop, restored on exit. `Break`/`Continue` read from `CurrentLoop`.

**Tests to write:**

```csharp
[Test]
public async Task WhileLoop_CrossValidate() {
    // WHY: Loops are the defining test of the block-structured CFG
    // (header/body/latch/exit) and the most common source of SSA
    // construction bugs (back-edge Phi placement). If this simplest
    // while-loop fails, all loop forms are broken. Cross-validation
    // is essential because the IR shape can look correct while the
    // latch-to-header branch targets the wrong block offset.
    // var i = 0; while (i < 5) i = i + 1; return i
    // Both pipelines produce 5
}

[Test]
public async Task WhileLoop_ProducesCorrectBlocks() {
    // WHY: A while-loop with a mutable loop variable must produce exactly
    // 4 blocks (entry, header, body, exit) with a CondBranch at the header.
    // If the emitter creates extra blocks (e.g., a separate latch block)
    // or omits the header, the UopCompiler's block-ordering pass will
    // assign incorrect PC offsets and branch targets will be misaligned.
    var module = IrPipeline.Emit(whileAst);
    await Assert.That(module.Blocks).HasCount().EqualTo(4);  // entry, header, body, exit    await Assert.That(module.Blocks[1].Terminator).IsTypeOf<CondBranch>();
}

[Test]
public async Task ForLoop_CrossValidate() {
    // WHY: ForLoop is the most complex loop node — it synthesizes an
    // initializer, condition check, body, and increment into a single
    // 5-block CFG (entry, initializer, header, body+latch, exit).
    // The increment must execute after each body iteration before the
    // condition check. If ForLoop.Emit places the increment in the wrong
    // block, the sum will be wrong (e.g., 0 instead of 45 because the
    // increment never runs).
    // var sum = 0; for (var i = 0; i < 10; i = i + 1) sum = sum + i; return sum
    // Both pipelines produce 45
}

[Test]
public async Task Break_ExitsLoopEarly() {
    // WHY: Break must transfer control from inside the loop body directly
    // to the exit block, bypassing the latch and header. If Break.Emit
    // targets the wrong block (e.g., the latch instead of the exit), the
    // loop would continue rather than terminate, producing an infinite
    // loop in the worst case. Cross-validation catches block-target errors.
    // var i = 0; while (true) { if (i >= 5) break; i = i + 1; } return i
    // Both pipelines produce 5
}

[Test]
public async Task Continue_SkipsToNextIteration() {
    // WHY: Continue must transfer control to the latch (which jumps to
    // the header) without executing the rest of the body. If Continue.Emit
    // targets the header directly, the loop-variable increment (in the
    // latch) would be skipped, causing an infinite loop. This test sums
    // 1+3+4 = 8, skipping i=2 via continue — a correct result proves the
    // increment still runs after the continue.
    // var sum = 0; for (var i = 0; i < 5; i = i + 1) { if (i == 2) continue; sum = sum + i; } return sum
    // Both pipelines produce 1+3+4 = 8 (skips 2)
}

[Test]
public async Task WhileLoop_Ssa_PhiAtHeader() {
    // WHY: Loop-carried variables produce Phi nodes at the loop header,
    // with one incoming from the pre-header (the initial value) and one
    // from the latch (the loop-carried value). If the Phi is missing or
    // placed in the wrong block, the SSA-transformed program will read
    // the initial value on every iteration instead of the updated one,
    // producing an infinite loop (the exit condition never changes).
    // var i = 0; while (i < 5) i = i + 1; return i
    var module = IrPipeline.Emit(whileAst);
    SsaTransform.Run(module);
    var header = module.Blocks[1];  // header block
    var phis = header.Instructions.OfType<Phi>();
    await Assert.That(phis).HasCount().EqualTo(1);  // Phi for loop variable i
    await Assert.That(phis.First().SlotIndex).IsEqualTo(0);
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

**Files to create (capture layout):**

| File | Contents |
|------|----------|
| `Poly/Ir/CaptureLayout.cs` | `sealed record CaptureLayout(int FuncIndex, List<CapturedSlot> Slots);` and `sealed record CapturedSlot(int OuterSlotIndex, TypeKind Kind);` |

**EmissionContext additions:**

- `int DeclareParameter(string name, TypeKind kind)` — assigns a slot in the parameter range (before local slots). Returns the slot index. Slots 0..N-1 are reserved for parameters.
- `int RegisterCallSite(string methodName, TypeKind returnType, int argCount)` — appends to `Module.CallSites` and returns the index (`CallSiteIndex`). Called by `Invoke.Emit` for known method references.
- `Module.ExportedFunctions` — when a `Lambda` is emitted, its body module is added to `ExportedFunctions` and assigned an index.
- **`CaptureLayouts` population** — `Lambda.Emit` appends a `CaptureLayout` to `Module.CaptureLayouts` describing which outer slots are captured and in what order. The `FuncIndex` matches the index into `Module.ExportedFunctions`, so the backend can pair a closure allocation with the correct layout.
- **Capture tracking** — `Scope` tracks which local slots are captured by inner lambdas. `bool IsCaptured(int slot)` returns true if the slot is in a capture list of a nested lambda. `Variable.Emit` checks `IsCaptured`: if true, emits `LoadUpvalue(captureIndex)` instead of `LoadLocal(slotIndex)`. The capture index is the position in the lambda's captures array. When an outer variable is captured, `Lambda.Emit` copies its current value into the closure's capture array (`AllocClosure(funcIndex, captures)`).

**UopCompiler additions:**

- `AllocClosure` → `AllocClosure` µop (maps to existing `Closure` class)
- `Call` (IsExternal=false) → closure dispatch via `CallClosure` µop

**Tests to write:**

```csharp
[Test]
public async Task Lambda_Identity_CrossValidate() {
    // WHY: The simplest possible lambda (no captures) validates that
    // AllocClosure, ExportedFunctions, and Call with IsExternal=false
    // work together. If the identity lambda returns the wrong value,
    // the closure-allocation or closure-dispatch path is broken — and
    // no more complex lambda test can distinguish between capture vs.
    // dispatch bugs.
    // var f = (x) => x; return f(42)
    // Both pipelines produce 42
}

[Test]
public async Task Lambda_CapturesOuterVariable() {
    // WHY: Captured variables require LoadUpvalue to read from the
    // closure's capture array rather than LoadLocal from the frame.
    // If Lambda.Emit omits a capture from the AllocClosure's capture
    // list, or Variable.Emit fails to switch to LoadUpvalue, the
    // captured value would be read from the outer function's recycled
    // slot (stale data) instead of the closure's snapshot.
    // var a = 10; var f = (x) => a + x; return f(5)
    // Both pipelines produce 15
}

[Test]
public async Task Lambda_Closure_AllocClosureGenerated() {
    // WHY: If a lambda has captures, AllocClosure must appear in the
    // IR. If it's absent, the emitter skipped closure allocation and
    // the captured variables would be read from the outer frame —
    // which works by accident in simple cases but breaks when the
    // lambda outlives the outer scope (e.g., returned from a function).
    var module = IrPipeline.Emit(lambdaWithCaptureAst);
    await Assert.That(module.Blocks[0].Instructions).Any(i => i is AllocClosure);
}

[Test]
public async Task Invoke_ExternalCall_CrossValidate() {
    // WHY: External method calls (IsExternal=true) require call-site
    // resolution via CallSites table and µop Call(callSiteIndex).
    // If the call-site index is wrong, the VM dispatches to the wrong
    // method. If the return type is wrong, the eval stack gets a
    // mis-typed value that crashes on subsequent operations.
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
| **Operators** | `Subtract`, `Multiply`, `Divide`, `Modulo`, `GreaterThan`, `GreaterThanOrEqual`, `LessThan`, `LessThanOrEqual`, `Equal`, `NotEqual`, `And`, `Or`, `Not`, `Negate` | All follow the `Add` pattern: emit children, emit `BinOp` or `UnaryOp` with correct `OpKind`. Use parameterized `[TestCase]` to avoid ~15 individual tests. |
| **Member access** | `MemberAccess`, `ElementAccess` | Map to `LoadField` / `LoadIndex` µops via `Call` with resolved member metadata |
| **Object creation** | `New`, `NewArray` | `Call` to constructor; `NewArray` uses `AllocArray` µop |
| **Exception handling** | `Throw`, `TryCatchFinally` | `Throw` emits `Throw` terminator. `TryCatchFinally` creates try/catch/finally blocks with implicit CFG edges (see §5.1.9). Exception edges are stored as a side-table on `Module`: `Dictionary<BasicBlock, List<BasicBlock>> ExceptionEdges` mapping each block in a `try` body to its catch/finally blocks. The CFG construction pass in §5.1.3 merges these into the `Successors` map before SSA. **First implementation: `TryCatchFinally` may throw `NotSupportedException` — exception handling is the most complex node and can be deferred.** |
| **Async/control flow** | `Await`, `SuspendNode`, `Break`, `Continue` | `Await` maps to a `Call` to `Task<T>.GetAwaiter().GetResult()` (IsExternal=true, resolved CallSite). No dedicated `Await` IR instruction — reuse `Call`. `Break`/`Continue` already done in Step 8. `SuspendNode` emits `SuspendOp`. |

**Await lowering note:** `Await` maps to a `Call` to `Task<T>.GetAwaiter().GetResult()` (IsExternal=true, resolved CallSite). No dedicated `Await` IR instruction. This is the blocking approach matching the current VM behavior (synchronous `GetAwaiter().GetResult()`). A future async state-machine lowering would add a dedicated `Await(Value awaitable)` instruction + async rewrite pass — the deferred decision is tracked in `docs/decisions/2026-XX-XX-async-lowering.md` (to be created when async support moves past the blocking model).

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
| `Poly/Interpretation/Ir/VmLoweringPass.cs` | `sealed class VmLoweringPass : INodeAnalyzer` — registered after `GenerationPass`. Reads `ModuleMetadata` from `AnalysisContext`, runs `RingAnalyzer.Run(module)` (computes eval-stack ring depths for `ConsumedFromPcs`), then calls `new UopCompiler().Compile(module)`. Produces a `LoweringResult` and stashes it as `LoweringResultMetadata` (a new `IAnalysisMetadata` subtype) for `ProgramCompiler` to consume. This replaces the role that `UopGenerationPass` + `Lowering.Assemble()` play in the old pipeline. |

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
| `Poly/Ir/CaptureLayout.cs` | `sealed record CaptureLayout` + `CapturedSlot` (§8.1) |
| `Poly/Ir/Passes/SsaTransform.cs` | SSA construction (§5.1) — includes `ExceptionEdgeLowering` pre-pass |
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
| `Poly.Tests/TestHelpers/IrPipeline.cs` | `static class IrPipeline { static Module Emit(Node); static object Execute(Node); }` — convenience wrappers for IR pipeline (Step 2, stubbed; Step 3, functional) |
| `Poly.Tests/Ir/IrTypeTests.cs` | Type creation and `OpKind.ResultType` tests (Step 1) |
| `Poly.Tests/Ir/GenerationPassTests.cs` | `GenerationPass` smoke tests (Step 2) |
| `Poly.Tests/Ir/IrCrossValidationTests.cs` | Cross-validation: old vs new pipeline per node type (Steps 3–10) |
| `Poly.Tests/Ir/SsaTests.cs` | SSA construction tests (§5.1.10) |
| `Poly.Tests/Interpretation/VmCorrectnessTests.cs` | Authoritative cross-validation source (existing, extended) |