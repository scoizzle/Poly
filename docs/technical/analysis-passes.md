# Analysis Passes — Technical Deep Dive

**Files:** `Poly/Interpretation/Analysis/` (11 passes, ~3,800 lines)

## Qualification Standard

Throughout this document, "dead code" means a value or method that is **computed or defined but the result is never consumed by any code path**. If the code is part of a coherent, self-contained system that produces a data model, and the unused parts are simply parts of that model that no current consumer queries, it is considered **dormant infrastructure** — not dead code — and is kept.

## Pipeline

```
TypeAndMemberResolver (300 lines) — resolves types + members in a single pass
ScopeValidator (212 lines) — scope validation + VariableAnalysisMetadata
ThisReferenceContext (88 lines) — validates `this` in static contexts
ControlFlowAnalysis (980 lines) — CFG + BasicBlocks + dead-code elision
ConstantFolding (512 lines) — constant folding + simplifications
SideEffectAnalysis (195 lines) — side-effect classification + elision
LambdaReturnTypeAnalyzer (53 lines) — lambda return type refinement
DefiniteAssignmentAnalyzer (136 lines) — definitely-assigned tracking
```

**Removed:** `StackDepthAnalyzer` (163 lines) — the VM now pre-allocates its stack from a µop-level scan in `ProgramCompiler.ComputeMaxDepth`, which is faster and more precise than an AST-level analysis pass.

## Reviewed Recommendations

### MemberResolver merge with TypeResolver — KEEP SEPARATE

**What each does:** Both walk the entire tree independently calling the same resolvers (`MethodInvocationSemanticResolver`, `ConstructorInvocationSemanticResolver`). TypeResolver discards the `ITypeMethod` after extracting the return type; MemberResolver stores it as metadata.

**Value statement:** Merging them couples type resolution (determining expression types) with member resolution (binding invocations to specific methods). These are distinct semantic concerns. The separation makes each pass independently testable and replaceable. The second walk is not a performance concern at analysis scale.

### ControlFlowAnalysisPass (980 lines) — KEEP

**What it does:** Builds a complete CFG with BasicBlocks, predecessors/successors, infinite loop detection, must-execute facts, reachability BFS, and dead-code elision.

**Consumed output:** `ElisionMetadata` on unreachable nodes — consumed by `LinqExpressionGenerator` and `CSharpGenerator` for dead code elimination. The CFG itself, `InfiniteLoopMetadata`, `MustExecuteMetadata`, and `ControlFlowGraph` objects are currently unqueried but part of the CFG data model.

**Value statement:** The CFG is the foundation for any future flow-sensitive optimization: loop invariant hoisting, common subexpression elimination, code motion, data-flow analysis. Replacing it with a simple reachability walk would save ~780 lines today but block every flow-sensitive optimization later. The CFG is dormant infrastructure within a coherent control-flow analysis system, not dead code.

### DefiniteAssignmentAnalyzer (136 lines) — KEEP

**What it does:** Tracks which variables are definitely assigned per lambda body using flow-sensitive analysis (intersection at if/else joins, reset at loops).

**Consumed by:** One line in `Lowering.cs` — skips zero-initialization of locals guaranteed assigned before first read. Saves two µops per local.

**Value statement:** The optimization is small but the cost of running the pass is small too — a single walk with cheap set operations. The pass produces `DefiniteAssignmentMetadata` as part of a coherent definite-assignment data model. If a future consumer needs this information (abstract interpretation, null-check elision), the infrastructure is already in place.

### LambdaReturnTypeAnalyzer — INLINE

**What it does:** If a lambda's resolved type is `object` (the fallback), scans the body for a more precise return type. Only registered in the test-only `UseAllAnalyzers()` pipeline.

**Value statement:** The logic is ~10 lines of refinement. The remaining 43 lines are the pass wrapper (registration, tree walk infrastructure). This can be inlined into `TypeResolver.ResolveNodeType`'s `Lambda` case with a simple 10-line addition, eliminating the standalone pass file and its registration. The pass adds nothing beyond what TypeResolver could do in its own walk — it's not a coherent data model, it's a thin refinement.

### VariableAnalysisMetadata dead fields — KEEP (dormant)

**What they are:** `BlockScopes`, `ScopeVertices`, `VariableDeclarationScope` — three fields on `VariableAnalysisMetadata` populated during the scope walk.

**Why keep:** They are part of the `VariableAnalysisMetadata` record, which is the complete data model produced by the `ScopeValidator`. The record represents the full scope tree: which variables are declared in which block, how scopes nest, and which scope each variable belongs to. Current consumers only query `VariableReferences`, `AssignmentCount`, and `EscapedVariables`. But the hierarchy data is part of the same coherent model — removing it would make the record incomplete by design, not dead code.

## Summary

| Decision | Action | Lines |
|---|---|---|
| **StackDepthAnalyzer** | Removed — µop-level scan replaces it | 163 |
| **LambdaReturnTypeAnalyzer** | Inline into TypeResolver | 43 |
| **MemberResolver merge** | Keep separate | 0 |
| **ControlFlowAnalysis replacement** | Keep — CFG is dormant infrastructure | 0 |
| **DefiniteAssignmentAnalyzer** | Keep | 0 |
| **VariableAnalysisMetadata fields** | Keep — part of coherent data model | 0 |
| **Total** | | **~206 lines** |
