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
- **Scoped locals fallback**: for variable reassignment (`x = x + 1`), the IR allows mutable `IrLocal` slots as an escape from SSA purity. The SSA construction pass converts these to SSA values with φ nodes at join points.

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
public sealed record BinOp(OpKind Op, Value Left, Value Right, NodeId? Source) : Instr(TypeKind.Int64, Source);
public sealed record UnaryOp(UnaryOpKind Op, Value Operand, NodeId? Source) : Instr(TypeKind.Int64, Source);
public sealed record LoadLocal(int SlotIndex, TypeKind Kind, NodeId? Source) : Instr(Kind, Source);
public sealed record StoreLocal(int SlotIndex, Value Val, NodeId? Source) : Instr(TypeKind.Void, Source);
public sealed record Call(Value Target, Value[] Args, int ArgCount, bool IsExternal, NodeId? Source) : Instr(TypeKind.Int64, Source);
public sealed record AllocClosure(int FuncIndex, Value[] Captures, NodeId? Source) : Instr(TypeKind.Handle, Source);
public sealed record LoadUpvalue(int UpvalueIndex, NodeId? Source) : Instr(TypeKind.Int64, Source);
public sealed record StoreUpvalue(int UpvalueIndex, Value Val, NodeId? Source) : Instr(TypeKind.Void, Source);
public sealed record AllocHeap(int HandleIndex, NodeId? Source) : Instr(TypeKind.Handle, Source);
public sealed record Phi(Value[] Incoming, NodeId? Source) : Instr(Incoming[0].Kind, Source);

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
    public int MaxLocalSlots { get; set; }
}
```

### 3.3 Why this design

- **Block-structured**: the `LoweringPrepPass` currently assigns labels and manages loop scope via a stack—that work is intrinsic to the IR, not a separate pass. Each `BasicBlock` carries its label implicitly.
- **SSA by construction**: every `Instr` returns a `Value`. Consumers reference that value directly. No ring analysis, no `ConsumedFromPcs` metadata. The ring analysis becomes an optimization pass for the VM backend only.
- **Heap constants as module-level sideband**: the current `HeapConstantMetadata` collected during `UopGeneration` (keyed under `NodeId.Empty`) becomes `Module.HeapConstants`. No instruction-level indirection needed.
- **Phi explicit**: `Phi` selects among `Value[] Incoming`. No heuristic detection, no `PhiSourcePcs`/`PhiAltPcs` juggling.

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

### 4.4 `GenerationPass`

The pipeline entry point. Registered via `.UseIrGeneration()`:

```csharp
public sealed class GenerationPass : INodeAnalyzer {
    public void Analyze(AnalysisContext context, Node node) {
        var module = new Module();
        var block = new BasicBlock("entry");
        module.Blocks.Add(block);

        var ctx = new EmissionContext(module, block, context.GetResult());
        var result = node.Emit(ctx);

        if (block.Terminator is null)
            block.Terminator = new Ret(result);
    }
}
```

No `is` check. No interface. The method call `node.Emit(ctx)` dispatches polymorphically. All nodes have `Emit` — the base returns `null`, concrete types override.

### 4.5 Full pipeline in one pass

```
AST → [TypeResolve] → [ConstFold] → [SideEffect] → [GenerationPass] → Module
                                                              ↑
                                              node.Emit(ctx) per node
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

Passes are simple `Module → Module` transforms. They run in order:

```
AST → [GenerationPass] → Module → [ConstFold] → [SSA Build] → [Inline] → [Lower for Backend]
                            ↑
                node.Emit(ctx) per node
```

### 5.1 SSA Construction Pass

Operates on the `Module` CFG:

1. Builds the dominator tree from block adjacency (terminators reference `BasicBlock` directly, so CFG construction is a simple `foreach block → follow its terminator's targets`).
2. Inserts `Phi` at dominance frontiers for each `StoreLocal`/`LoadLocal` pair.
3. Renames `LoadLocal`/`StoreLocal` to direct `Value` references, replacing old instructions in-place.

Result**: no `LoadLocal` or `StoreLocal` remain (when the pass succeeds). The IR is pure SSA.

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
  %0 = LoadLocal(slot=0)              // load x
  %1 = Const(0)
  %2 = BinOp(Gt, %0, %1)
  CondBranch(%2, then_block, else_block)

then_block:
  %3 = LoadLocal(slot=0)
  %4 = Const(1)
  %5 = BinOp(Add, %3, %4)
  Goto(merge_block)

else_block:
  %6 = LoadLocal(slot=0)
  %7 = Const(1)
  %8 = BinOp(Sub, %6, %7)
  Goto(merge_block)

merge_block:
  ; φ selects %5 (from then) or %8 (from else)
  %9 = Phi([%5, %8])
  %10 = Ret(%9)
```

### 7.3 IR after SSA pass

```
entry block:
  %0 = Const(x_value)        ; from SSA renaming; no more LoadLocal
  %1 = Const(0)
  %2 = BinOp(Gt, %0, %1)
  CondBranch(%2, then_block, else_block)

then_block:
  %3 = Const(1)
  %4 = BinOp(Add, %0, %3)
  Goto(merge_block)

else_block:
  %5 = Const(1)
  %6 = BinOp(Sub, %0, %5)
  Goto(merge_block)

merge_block:
  %7 = Phi([%4, %6])
  Ret(%7)
```

Note**: `%0` is used in all three blocks—no reload is needed because SSA gave every definition a unique name.

### 7.4 VM µop output (after `UopLoweringVisitor`)

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

The `Analyzer` already supports incremental analysis (`Analyzer.Analyze(Node root, AnalysisResult priorAnalysis, IEnumerable<Node> invalidatedNodes)`). The IR pipeline extends this:

1. Add a `priorModule` parameter to `GenerationPass`. If provided, and if no AST node in a function's subtree was invalidated, reuse the prior IR blocks for that function.
2. The IR blocks carry a `SourceNodeId` range. On recompilation, only blocks whose source range overlaps an invalidated node are rebuilt.
3. Downstream passes (SSA, const-fold, ring analysis) only need to re-run on changed blocks and their affected successors (reached via CFG reachability).

This avoids full recompilation when editing a single expression.

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