# Analysis Passes (Interpretation/Analysis/)

Semantic analysis passes that consume AST nodes, attach metadata, and emit diagnostics.
Each pass implements `INodeAnalyzer` and is registered via an extension method on `AnalyzerBuilder`.

## Pass Registry

| Pass | Extension Method | Produces Metadata | Diagnostics |
|------|-----------------|-------------------|-------------|
| `TypeDefinitionNodeAnalyzer` | `.UseTypeDefinitionNodeAnalyzer()` | `TypeDefinitionMetadata`; also an `ITypeDefinitionProvider` | (none; member-type miss is fail-closed. Generic parameters and `NamedTypeReference` type arguments resolve.) |
| `ThisReferenceContextAnalyzer` | `.UseThisReferenceContext()` | `this` resolved type on `ThisReference` | `TH0001` (static body) |
| `TypeAndMemberResolver` | `.UseTypeAndMemberResolver()` | Resolved types + resolved members | Structural failures (missing members) |
| `ScopeValidator` | `.UseVariableScopeValidator()` | `VariableAnalysisMetadata` (block scopes, escapes, captured bindings), `LambdaCaptureMetadata` per `Lambda` | Scoping errors |
| `SideEffectAnalyzer` | `.UseSideEffectAnalysis()` | `SideEffectMetadata`, `ElisionMetadata`, `AssignmentValueUsedMetadata` | `DEAD_CODE_ELIDABLE` |
| `JumpTargetAnalyzer` | `.UseJumpTargetResolution()` | `ResolvedJumpTarget` (break/continue/goto targets) | `JT0001`-`JT0004` |
| `ConstantFoldingPass` | `.UseConstantFolding()` | `ConstantValueMetadata`, node replacement | (none) |
| `ControlFlowAnalysisPass` | `.UseControlFlowAnalysis()` | `ControlFlowMetadata`, `InfiniteLoopMetadata`, `MustExecuteMetadata` | `CF0001`-`CF0013` |
| `ValueRepresentationAnalyzer` | `.UseValueRepresentationAnalysis()` | `ValueRepresentationMetadata` (stack scalar, bool, heap ref, void, unknown) | (none) |
| `CallSiteCatalogAnalyzer` | `.UseCallSiteCatalog()` | `CallSiteCatalogMetadata`, `CallSiteIndexMetadata` | (none) |
| `DefiniteAssignmentAnalyzer` | `.UseDefiniteAssignmentAnalysis()` | `DefiniteAssignmentMetadata` | (none) |
| `LambdaReturnTypeAnalyzer` | `.UseLambdaReturnTypeResolution()` | Invoke return types from lambda bodies (including stored closures); does not retarget the `Lambda` node itself | (none) |
| `ExceptionRegionAnalyzer` | `.UseExceptionRegionAnalysis()` | `ExceptionRegionMetadata`, `InProtectedRegionMetadata` | (none) |
| `SyntaxTypeCompatibilityAnalyzer` | `.UseSyntaxTypeCompatibility()` | (none) | `VmTypeCompatibility` |

## Pass Ordering

Built order of `Interpreter.Analyzer` (asserted by `StandardAnalyzer_PassNames_MatchInterpreterPipeline`). Direct AST-to-VM-ABI lowering; no primitive expansion. `Use*` registration **is** the run order.

```
 1. TypeDefinitionNodeAnalyzer
 2. ThisReferenceContext            (root this is legal SetArgs slot 0; TH0001 in static bodies)
 3. TypeAndMemberResolver
 4. LambdaReturnTypeAnalyzer        (Invoke body type; Lambda value stays heap/object)
 5. ScopeValidator
 6. SideEffectAnalyzer
 7. ConstantFoldingPass
 8. JumpTargetAnalyzer
 9. ControlFlowAnalysisPass
10. ExceptionRegionAnalyzer
11. ValueRepresentationAnalyzer
12. SyntaxTypeCompatibilityAnalyzer
13. CallSiteCatalogAnalyzer
14. DefiniteAssignmentAnalyzer
```

Ad-hoc test pipelines may omit `TypeDefinitionNodeAnalyzer`. CLR-only trees still analyze.

**Oracle:** A shipped Syntax node's runtime meaning is proven by `Interpreter.Compile` (and execute) on that tree. CFG analysis or `BuildExpression()` / LINQ alone is not the oracle.

## Sub-directories

| Directory | Contents |
|-----------|----------|
| `Semantics/` | Type resolution, scoping, side-effect analysis, this-reference, jump targets, lambda returns, definite assignment, value representation, call site catalog, exception region analysis |
| `ControlFlow/` | CFG construction, reachability, infinite-loop detection |
| `ConstantFolding/` | Constant expression evaluation and algebraic simplification |

## Writing a New Analysis Pass

Every pass must implement `INodeAnalyzer`:

```csharp
internal sealed class MyPass : INodeAnalyzer {
    public const string Id = "MyPass";
    public string PassName => Id;

    public void Analyze(AnalysisContext context, Node node) {
        if (!context.TryBeginAnalyzerVisit<MyPass>(node))
            return;

        // Post-order: analyze children first, then the parent
        this.AnalyzeChildren(context, node);

        // Attach metadata
        context.SetMetadata(node, new MyMetadata(someValue));

        // Report diagnostics
        if (someError)
            context.ReportDiagnostic(node, DiagnosticSeverity.Error,
                "Description of the problem", "MY0001");
    }
}
```

### Registration

Add an extension method on `AnalyzerBuilder`:

```csharp
public static class MyPassExtensions {
    extension(AnalyzerBuilder builder) {
        public AnalyzerBuilder UseMyPass() {
            builder.AddPass(state => new MyPass());
            return builder;
        }
    }
}
```

Then register in your `AnalyzerBuilder` chain:

```csharp
var analyzer = new AnalyzerBuilder()
    .UseTypeAndMemberResolver()
    // ... other passes ...
    .UseMyPass()
    .Build();
```

### Metadata Types

Metadata records implement `IAnalysisMetadata` and are stored per-node via
`context.SetMetadata(node, metadata)`. Retrieve with `context.GetMetadata<T>(node)`.

Metadata on the root node (null key) is accessible module-wide.
Per-node metadata is scoped to that specific AST node.

### Pass ordering

`Build` is registration order. Put a pass after the passes whose bags it reads.

1. TypeDefinitionNode (when present) before This/TypeAndMember so AST types exist.
2. Variable scoping before side-effect analysis.
3. Jump targets before CFG construction.
4. Constant folding before CFG; ValueRepresentation, definite assignment, and EH after CFG.
5. LambdaReturnType after TypeAndMember so Invoke nodes have body types before value representation. SyntaxTypeCompatibility and CallSiteCatalog after ValueRepresentation.

---

## Diagnostics

All passes emit diagnostics through `AnalysisContext`. See [`DIAGNOSTICS_EXAMPLE.md`](DIAGNOSTICS_EXAMPLE.md)
for usage patterns. Diagnostic codes are prefixed by pass area (CF=control flow, JT=jump target,
TH=this reference, etc.).
