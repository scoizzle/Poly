> **ARCHIVED (2026-07-10)** — Do not implement. Superseded by direct AST→VM-ABI (`DirectVmAbiEmitter`). See `docs/plans/archive/interpretation/README.md`.
>
> Original document follows for historical context only.

# Lowering as Analysis Passes

> **Status:** Spike / proposal  
> **Motivation:** Eliminate `ResolveProducers` CFG predecessor graph and
> the circular back-edge dependency that causes the TUnit shutdown hang.

Replace the monolithic `Lowering.cs` + `ResolveProducers` pipeline with three
analysis passes that prepare lowering metadata, plus a mechanical assembly step
in `Lowering.Lower` that flattens per-node µop fragments and computes
`ConsumedFromPcs` via a simple backward scan (no CFG graph).

---

## Motivation

`ResolveProducers` (ProgramCompiler.cs) uses a linear CFG walk to compute each
µop's `ConsumedFromPcs`. Loop headers create a circular dependency — the
back-edge predecessor's exit stack hasn't been computed yet in the linear pass,
so it defaults to `[]`, losing values below the loop.

Every attempted fix (skip null predecessors, two-pass, loop-header-only fixpoint)
causes the TUnit process to hang during shutdown. The hang appears to stem from
`ConsumedFromPcs` changes that alter expression trees in ways the .NET runtime
can't finalize around.

The root cause is architectural: **the producer tracking infers stack depth from
the µop control-flow graph, but the depth is statically knowable from the AST.**
Moving depth computation and µop production into analysis passes eliminates the
circular dependency entirely.

---

## Architecture

```
Analysis pipeline (INodeAnalyzer passes)
┌─────────────────────────────────────────────┐
│ StackDepthAnalysis → Node → (entry, exit)   │
│ LabelAssignment   → label IDs per CF node   │
│ UopGeneration     → µop fragments per node  │
└──────────────────────┬──────────────────────┘
                       │ metadata on AnalysisResult
                       ▼
Lowering.Lower (external assembler)
┌─────────────────────────────────────────────┐
│ Walk AST in execution order                 │
│   concatenate per-node µops                 │
│   resolve label IDs → flat positions        │
│   backward scan for ConsumedFromPcs         │
│ → LoweringResult (compiler-ready)           │
└──────────────────────┬──────────────────────┘
                       │
                       ▼
ProgramCompiler.Compile(LoweringResult)
  — No predecessor graph
  — No ResolveProducers call
  — Just emit expressions, compile delegate
```

---

## Pass 1: StackDepthAnalysis

**File:** `Poly/Interpretation/Analysis/LoweringPrep/StackDepthAnalysisPass.cs`

Walks the AST bottom-up via `AggregateChildren`. For each node, computes the
number of values it pushes onto the conceptual eval stack (entry depth) and
leaves on it (exit depth).

```csharp
record StackDepthMetadata(int EntryDepth, int ExitDepth) : IAnalysisMetadata;
```

### Net push/pop by node type

| Node | Entry→Exit | Notes |
|------|-----------|-------|
| `Constant`, `Variable`, `Parameter`, `ThisReference` | 0→1 | Pushes a single value |
| `Add`, `Subtract`, `Multiply`, `Divide`, `Modulo` | 2→1 | Pops 2, pushes 1 |
| `Equal`–`GreaterThanOrEqual` | 2→1 | Comparison → bool → 0/1 |
| `And`, `Or`, `Xor`, `ShiftLeft`, `ShiftRight` | 2→1 | Bitwise ops |
| `UnaryMinus`, `Not`, `BitwiseNot` | 1→1 | Pop 1, push 1 |
| `Assignment(dest, value)` | 0→1 | Value pushes 1, StoreSlot pops 1, LoadSlot pushes 1 |
| `Block(nodes)` | E→E' | Sum children, minus 1 per non-last child (PopOp) |
| `WhileLoop`, `DoWhileLoop`, `ForLoop` | E→E | Net zero — condition/body are self-contained |
| `IfStatement(cond, then, else)` | E→max(then.E, else.E) | Merge of branches |
| `Conditional(cond, ifTrue, ifFalse)` | 1→1 | One branch pushes its result |
| `Invoke`, `Call` | N→1 | Pops N args, pushes result |
| `Return(value)` | 1→0 | Pops value, no push |
| `NewArray(length)` | 1→1 | Pops length, pushes handle |
| `IndexAccess(array, index)` | 2→1 | Pops array+index, pushes value |
| `Member` (property) | 0→1 | Pushes property value |
| `Lambda(body, params)` | 0→1 | Pushes closure handle |
| `BreakStatement`, `ContinueStatement` | * | Control flow — no net push |
| `ThrowStatement(exc)` | 1→0 | Pops exception, doesn't return |

### Why this breaks the circular dependency

A `WhileLoop`'s condition starts executing at the same depth as the surrounding
context. The loop body is self-contained — all its internal pushes are consumed
before the Jump back. The µop at the loop header starts at
`EntryDepth(loop) = ExitDepth(parent)`.

The back edge's stack contribution is **the same depth** as the fallthrough
entry. No speculative forward-gaze into the CFG is needed.

### Implementation sketch

```csharp
internal sealed class StackDepthAnalysisPass : INodeAnalyzer {
    public void Analyze(AnalysisContext context, Node node) {
        if (!context.TryBeginAnalyzerVisit<StackDepthAnalysisPass>(node))
            return;

        var (entry, exit) = node switch {
            WhileLoop wl   => ComputeLoop(context, wl),
            IfStatement ifs => ComputeIf(context, ifs),
            Block b        => ComputeBlock(context, b),
            Assignment a   => ComputeAssignment(context, a),
            _              => this.AggregateChildren(context, node,
                (ctx, child) => ctx.GetMetadata<StackDepthMetadata>(child)!,
                Merge, identity: new StackDepthMetadata(0, 0))
        };

        context.SetMetadata(node, new StackDepthMetadata(entry, exit));
    }
}
```

---

## Pass 2: LabelAssignment

**File:** `Poly/Interpretation/Analysis/LoweringPrep/LabelAssignmentPass.cs`

Walks the AST top-down, assigns unique integer label IDs to control-flow structures.

```csharp
record WhileLoopLabelMetadata(int ContLabel, int EndLabel) : IAnalysisMetadata;
record IfLabelMetadata(int ElseLabel, int EndLabel) : IAnalysisMetadata;
record ConditionalLabelMetadata(int FalseLabel, int EndLabel) : IAnalysisMetadata;
record ForLoopLabelMetadata(int CondLabel, int EndLabel) : IAnalysisMetadata;
```

Maintains a `_nextLabel` counter and a stack of enclosing loop scopes for
resolving `BreakStatement` / `ContinueStatement`.

### Label assignment per construct

| Construct | Labels | Usage |
|-----------|--------|-------|
| `WhileLoop(cond, body)` | `ContLabel`, `EndLabel` | Cont = condition retry. End = loop exit. |
| `DoWhileLoop(body, cond)` | `ContLabel`, `EndLabel` | Cont = body entry. End = post-condition. |
| `ForLoop(init, cond, inc, body)` | `CondLabel`, `EndLabel` | Cond = condition check. End = exit. |
| `IfStatement(cond, then, else)` | `ElseLabel`, `EndLabel` | Else = else branch. End = merge. |
| `Conditional(cond, ifTrue, ifFalse)` | `FalseLabel`, `EndLabel` | False = ifFalse branch. End = merge. |
| `BreakStatement` | — | Resolved to enclosing loop's `EndLabel` |
| `ContinueStatement` | — | Resolved to enclosing loop's `ContLabel` |

Labels are integers; they are resolved to flat µop indices during assembly.

---

## Pass 3: UopGeneration

**File:** `Poly/Interpretation/Analysis/LoweringPrep/UopGenerationPass.cs`

The current `Emit*` methods refactored to store µops on each node as metadata
instead of appending to a flat list.

```csharp
record LoweredUopMetadata(List<Instruction> Uops) : IAnalysisMetadata;
```

### What changes vs the current `Lowering.cs`

| Current | New |
|---------|-----|
| `LowerCtx ctx` carries `Instructions`, `Parameters`, `Locals`, labels | `LowerCtx` replaced by `AnalysisContext` + pass-local `Dictionary<string,int>` state |
| `ctx.Instructions.Add(inst)` | `uops.Add(inst)` then `context.SetMetadata(node, new LoweredUopMetadata(uops))` |
| `DefineLabel`/`MarkLabel`/`_forwardRefs` in `LowerCtx` | Labels are Pass-2 IDs — left unresolved in µops |
| `EmitBlock` calls `PopOp` inline | Same logic, stored on the Block node |
| `EmitWhileLoop` calls `PopOp` + `Jump` | Same, with labels from `WhileLoopLabelMetadata` |

### Key invariants

- Each node's µop fragment is **independent** — no cross-node label references
  at generation time. Labels use Pass-2 integer IDs resolved globally during
  assembly.
- Variable slot indices are consistent across all fragments within a single
  Lambda scope. The pass tracks `Parameters` and `Locals` per Lambda (same
  `GetOrCreateLocalSlot` logic).
- Generated µops carry `SourceNodeId` for debugger mapping.

### Implementation sketch

```csharp
internal sealed class UopGenerationPass : INodeAnalyzer {
    private readonly Dictionary<string, int> _params = new();
    private readonly Dictionary<string, int> _locals = new();
    private int _currentArgSlots;

    public void Analyze(AnalysisContext context, Node node) {
        var uops = new List<Instruction>();

        switch (node) {
            case WhileLoop wl:
                var labels = context.GetMetadata<WhileLoopLabelMetadata>(wl)!;
                AnalyzeChildren(context, wl.Condition, uops);
                uops.Add(new BranchIfFalse(labels.EndLabel) { SourceNodeId = wl.Id });
                AnalyzeChildren(context, wl.Body, uops);
                uops.Add(new PopOp { SourceNodeId = wl.Id });
                uops.Add(new Jump(labels.ContLabel) { SourceNodeId = wl.Id });
                break;
            // ... other node types follow the same pattern
        }

        context.SetMetadata(node, new LoweredUopMetadata(uops));
    }

    private int GetOrCreateLocalSlot(string name) { /* same logic as current LowerCtx */ }
    private void AnalyzeChildren(AnalysisContext ctx, Node node, List<Instruction> uops) { /* ... */ }
}
```

---

## Assembly step (in `Lowering.Lower`)

**File:** `Poly/Interpretation/Vm/Lowering.cs`

Walks the AST in execution order, reading `LoweredUopMetadata` from each node.
Flattens into a single `List<Instruction>`, resolves labels, and computes
`ConsumedFromPcs` via a backward scan.

### Backward scan

Maintain a ring buffer (`List<int>`) of µop PCs that produced values on the
conceptual eval stack. At each µop with `PopCount = N`:

```
consumed[0..N-1] = ring[depth-N .. depth-1]
```

After adding the µop to the result:
- Pop the last N entries from the ring (the consumed values)
- Push the µop's own PC for each produced value (PushCount times)

No predecessor graph needed. The ring at a Jump (back edge) has the same
entries as the ring at the loop header (loop invariant), so the ring after
the loop is correct without any fixpoint iteration.

### Merge points (φ)

At `IfStatement` / `Conditional` merge points, the ring contents from the two
branches may differ at some depths. The assembly walk saves the ring before
the first branch, processes the then-branch, then restores the saved ring before
the else-branch. At the merge point, any depth where the two branch rings
differ gets a φ marker — resolved by the compiler via
`Instruction.PhiSourcePcs` / `PhiAltPcs` (unchanged from the current scheme).

### Label resolution

During the walk, `BranchIfFalse` and `Jump` µops reference unresolved label
IDs. The assembly records forward references `(instIdx, isBranch, labelId)`.
When a label target is reached (marked by a sentinel µop or implicit position),
its flat index is recorded. After the walk, all forward references are resolved
to flat µop indices.

### ReturnOp insertion

If the last µop in the result is not a `ReturnOp` / `ReturnFromCall`, one is
appended (same as the current `Lowering.Lower` behavior).

---

## `LoweringResult` carries compiler-ready metadata

The assembly step produces a richer `LoweringResult`:

```
LoweringResult:
  Instructions          // flat µop list (ConsumedFromPcs, φ info resolved)
  SourceRanges          // NodeId → SourceRange (debugger mapping)
  Constants             // heap-allocated values (closures, strings)
  CallSites             // indirect call descriptors
  FunctionEntries       // Lambda entry points
  MaxActiveLocalsDepth  // from StackDepthAnalysis
```

`ProgramCompiler.Compile` reads these off the result — no `ResolveProducers`,
no `CompilationContext` for producer tracking. Just iterate µops, emit LINQ
expressions, compile delegate.

---

## Changes by file

| File | Change |
|------|--------|
| **New:** `Poly/Interpretation/Analysis/LoweringPrep/StackDepthAnalysisPass.cs` | Pass 1 implementation |
| **New:** `Poly/Interpretation/Analysis/LoweringPrep/LabelAssignmentPass.cs` | Pass 2 implementation |
| **New:** `Poly/Interpretation/Analysis/LoweringPrep/UopGenerationPass.cs` | Pass 3 implementation |
| **New:** `Poly/Interpretation/Analysis/LoweringPrep/` metadata records | One file per metadata type, or inline in pass files |
| **Modified:** `Poly/Interpretation/Vm/Lowering.cs` | Replace flat `Emit*` walk with assembly step that reads `LoweredUopMetadata` |
| **Modified:** `Poly/Interpretation/Vm/LoweringResult.cs` | Add `SourceRanges`, pre-computed compiler metadata |
| **Modified:** `Poly/Interpretation/Vm/ProgramCompiler.cs` | Delete `ResolveProducers` |
| **Deleted:** `Poly/Interpretation/Vm/ProgramCompiler.cs` `ResolveProducers` method | No longer needed |
| **Modified:** `Poly/Interpretation/Analysis/Semantics/` extensions | Add `.UseLoweringPreparation()` to `AnalyzerBuilder` |
| **Modified:** Test files | Add `.UseLoweringPreparation()` to their `AnalyzerBuilder` chains |

---

## Implementation order

1. **Metadata types** — define `StackDepthMetadata`, label metadata records,
   `LoweredUopMetadata`. All implement `IAnalysisMetadata`.

2. **StackDepthAnalysisPass** — implement the bottom-up walk. Test against
   existing AST structures to verify depths match expectations.

3. **LabelAssignmentPass** — implement the top-down walk. Test with control-flow
   ASTs (WhileLoop, IfStatement, Conditional, nested variants).

4. **UopGenerationPass** — refactor `Emit*` methods from `Lowering.cs` into the
   pass. At this point, both the old `Lowering.Lower` and the new pass can
   coexist (output compare).

5. **Assembly in `Lowering.Lower`** — implement the AST walk, backward scan,
   label resolution, merge-point φ. Replace the body of `Lowering.Lower`.

### Pipeline gap tests

`PipelineGapTests.cs` verifies the new assembly step produces identical
results to the legacy pipeline for 15 patterns:

| Pattern | Status | Notes |
|---------|--------|-------|
| Simple arithmetic (+, -, <) | ✅ | New matches legacy |
| IfStatement (without else, with else) | ✅ | New matches legacy |
| WhileLoop counter | ✅ | New matches legacy |
| Nested WhileLoops 4x4 | ✅ | New matches legacy |
| Conditional (true, false) | ✅ | New matches legacy |
| Conditional as Add argument | ✅ | Both return same (wrong) value — pre-existing bug |
| ForLoop / DoWhileLoop | ✅ | New matches legacy |
| IfStatement inside WhileLoop | ✅ | New matches legacy |
| CLR method calls (Math.Max, Math.Abs) | ✅ | New matches legacy |
| Lambda identity | ✅ | New matches legacy |
| Collatz steps | ✅ | New matches legacy |

**Pre-existing bug confirmed:** `Conditional` used as an argument to `Add`
produces wrong result in BOTH pipelines (3 instead of 8).  The legacy
`ResolveProducers` predecessor graph does not emit φ at Conditional merge
points.  The assembly step's φ detection stamps the correct `PhiSourcePcs`/
`PhiAltPcs` on the merge consumer, but the deeper issue persists in both
paths — neither pipeline's φ resolution actually selects the correct `_v{pc}`
at runtime for expression-valued merge points.

6. **Delete `ResolveProducers`** — `ProgramCompiler.Compile` no longer calls it.
   Remove the method and the predecessor-graph code.

7. **Wire up `AnalyzerBuilder`** — register the three passes via
   `.UseLoweringPreparation()`. Update all callers.

8. **Verify** — full test suite. Zero changes expected in results except
   Mandelbrot now passes (458080).
