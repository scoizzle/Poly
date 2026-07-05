# Analysis Passes (Interpretation/Analysis/)

Semantic analysis passes that consume AST nodes, attach metadata, and emit diagnostics.
Each pass implements `INodeAnalyzer` and is registered via an extension method on `AnalyzerBuilder`.

## Pass Registry

| Pass | Extension Method | Produces Metadata | Diagnostics |
|------|-----------------|-------------------|-------------|
| `TypeAndMemberResolver` | `.UseTypeAndMemberResolver()` | Resolved types + resolved members | Structural failures (missing members) |
| `ScopeValidator` | `.UseVariableScopeValidator()` | `VariableAnalysisMetadata` (block scopes, escapes) | Scoping errors |
| `SideEffectAnalyzer` | `.UseSideEffectAnalysis()` | `SideEffectMetadata`, `ElisionMetadata`, `AssignmentValueUsedMetadata` | `DEAD_CODE_ELIDABLE` |
| `ThisReferenceContextAnalyzer` | `.UseThisReferenceContext()` | `this` resolved type on `ThisReference` | `TH0001`, `TH0002` |
| `JumpTargetAnalyzer` | `.UseJumpTargetResolution()` | `ResolvedJumpTarget` (break/continue/goto targets) | `JT0001`-`JT0004` |
| `ControlFlowAnalysisPass` | `.UseControlFlowAnalysis()` | `ControlFlowMetadata`, `InfiniteLoopMetadata`, `MustExecuteMetadata` | `CF0001`-`CF0013` |
| `ValueRepresentationAnalyzer` | `.UseValueRepresentationAnalysis()` | `ValueRepresentationMetadata` (stack scalar, bool, heap ref, void, unknown) | (none) |
| `CallSiteCatalogAnalyzer` | `.UseCallSiteCatalog()` | `CallSiteCatalogMetadata`, `CallSiteIndexMetadata` | (none) |
| `ConstantFoldingPass` | `.UseConstantFolding()` | `ConstantValueMetadata`, node replacement | (none) |
| `DefiniteAssignmentAnalyzer` | `.UseDefiniteAssignmentAnalysis()` | `DefiniteAssignmentMetadata` | (none) |
| `LambdaReturnTypeAnalyzer` | `.UseLambdaReturnTypeResolution()` | Resolved Lambda types | (none) |
| `ExceptionRegionAnalyzer` | `.UseExceptionRegionAnalysis()` | `ExceptionRegionMetadata`, `InProtectedRegionMetadata` | (none) |
| `ExpansionPass` | `.UsePrimitiveExpansion()` | `PrimitiveExpansionMetadata` | (none) |

## Pass Ordering

```
 1. TypeAndMemberResolver         (types must be resolved first — everything depends on them)
 2. ScopeValidator                (variable scopes must be known before side-effect analysis)
 3. SideEffectAnalyzer            (purity/elision feeds into CFG and constant folding)
 4. ThisReferenceContext          (this-reference resolution for diagnostics)
 5. JumpTargetAnalyzer            (jump targets needed before CFG and expansion)
 6. ControlFlowAnalysisPass       (CFG depends on resolved jump targets)
 7. ValueRepresentationAnalyzer   (value kind classification — pre-CF fold)
 8. CallSiteCatalogAnalyzer       (call site indexing — depends on type resolution)
 9. ConstantFoldingPass           (post-CFG for constant-condition branch elimination)
10. DefiniteAssignmentAnalyzer    (post-CFG for merging assignment facts)
11. LambdaReturnTypeAnalyzer      (post-type-resolution for lambda return type refinement)
12. ExceptionRegionAnalyzer       (EH region table — depends on CFG + definite assignment)
13. ExpansionPass                 (final step — depends on all other metadata)
```

The standard pipeline is assembled in `Interpreter.cs`.
Changes to pass ordering there must be reflected in this table.

## Sub-directories

| Directory | Contents |
|-----------|----------|
| `Semantics/` | Type resolution, scoping, side-effect analysis, this-reference, jump targets, lambda returns, definite assignment, value representation, call site catalog, exception region analysis |
| `ControlFlow/` | CFG construction, reachability, infinite-loop detection |
| `ConstantFolding/` | Constant expression evaluation and algebraic simplification |

## Diagnostics

All passes emit diagnostics through `AnalysisContext`. See [`DIAGNOSTICS_EXAMPLE.md`](DIAGNOSTICS_EXAMPLE.md)
for usage patterns. Diagnostic codes are prefixed by pass area (CF=control flow, JT=jump target,
TH=this reference, etc.).
