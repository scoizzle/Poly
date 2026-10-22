# Analysis Passes (Interpretation/Analysis/)

Semantic analysis passes that consume AST nodes, attach metadata, and emit diagnostics.
Each pass implements `INodeAnalyzer` and is registered via an extension method on `AnalyzerBuilder`.

## Pass Registry

| Pass | Extension Method | Produces Metadata | Diagnostics |
|------|-----------------|-------------------|-------------|
| `TypeDefinitionNodeAnalyzer` | `.UseTypeDefinitionNodeAnalyzer()` | `TypeDefinitionMetadata`; also an `ITypeDefinitionProvider` | (none; member-type miss is fail-closed. Generic parameters and `NamedTypeReference` type arguments resolve.) |
| `ThisReferenceContextAnalyzer` | `.UseThisReferenceContext()` | `this` resolved type on `ThisReference` | `TH0001` (static body) |
| `ScopeValidator` | `.UseVariableScopeValidator()` | `VariableAnalysisMetadata` (bindings, parameter bindings, block scopes, writes, escapes, captures), `CatchBindingMetadata`, `LambdaCaptureMetadata`; resolves dotted CLR type names against the scope | Scoping errors |
| `TypeAndMemberResolver` | `.UseTypeAndMemberResolver()` | Resolved types (projected through bindings) + resolved members | Structural failures (missing members) |
| `SideEffectAnalyzer` | `.UseSideEffectAnalysis()` | `SideEffectMetadata`, `AssignmentValueUsedMetadata`; contributes the `NoObservableEffect` elision reason | `DEAD_CODE_ELIDABLE` |
| `JumpTargetAnalyzer` | `.UseJumpTargetResolution()` | `ResolvedJumpTarget` (break/continue/goto targets) | `JT0001`-`JT0004` |
| `ConstantFoldingPass` | `.UseConstantFolding()` | `ConstantValueMetadata`, node replacement | (none) |
| `ControlFlowAnalysisPass` | `.UseControlFlowAnalysis()` | `ControlFlowMetadata`; contributes the `Unreachable` elision reason | `CF0001`-`CF0013` |
| `CallSiteCatalogAnalyzer` | `.UseCallSiteCatalog()` | `CallSiteCatalogMetadata`, `CallSiteIndexMetadata` | (none) |
| `DefiniteAssignmentAnalyzer` | `.UseDefiniteAssignmentAnalysis()` | `DefiniteAssignmentMetadata` | (none) |
| `LambdaReturnTypeAnalyzer` | `.UseLambdaReturnTypeResolution()` | Invoke return types from lambda bodies (including stored closures); does not retarget the `Lambda` node itself | (none) |
| `ExceptionRegionAnalyzer` | `.UseExceptionRegionAnalysis()` | `ExceptionRegionMetadata`, `InProtectedRegionMetadata` | (none) |
| `SyntaxTypeCompatibilityAnalyzer` | `.UseSyntaxTypeCompatibility()` | (none) | `VmTypeCompatibility` |

## Pass Ordering

Built order of `Interpreter.Analyzer` (asserted by `StandardAnalyzer_PassNames_MatchInterpreterPipeline`). Direct AST lowering to the VM; no primitive expansion. `Use*` registration **is** the run order.

```
 1. TypeDefinitionNodeAnalyzer
 2. ThisReferenceContext            (root this is legal as the first argument; TH0001 in static bodies)
 3. ScopeValidator                  (bindings; every use resolves to a declaration)
 4. TypeAndMemberResolver           (types projected from bindings)
 5. LambdaReturnTypeAnalyzer        (Invoke body type; a Lambda value is its delegate type)
 6. SideEffectAnalyzer
 7. ConstantFoldingPass
 8. JumpTargetAnalyzer
 9. ControlFlowAnalysisPass
10. ExceptionRegionAnalyzer
11. SyntaxTypeCompatibilityAnalyzer
12. CallSiteCatalogAnalyzer
13. DefiniteAssignmentAnalyzer
```

Ad-hoc test pipelines may omit `TypeDefinitionNodeAnalyzer`. CLR-only trees still analyze.

**Oracle:** A shipped Syntax node's runtime meaning is proven by `Interpreter.Compile` (and execute) on that tree. CFG analysis or `BuildExpression()` / LINQ alone is not the oracle.

## Sub-directories

| Directory | Contents |
|-----------|----------|
| `Semantics/` | Type resolution, scoping, side-effect analysis, this-reference, jump targets, lambda returns, definite assignment, call site catalog, exception region analysis |
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
            builder.AddAnalyzer(new MyPass());
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
4. Constant folding before CFG; definite assignment and EH after CFG.
5. LambdaReturnType after TypeAndMember so Invoke nodes have body types. SyntaxTypeCompatibility and CallSiteCatalog after the type passes.

---

## Diagnostics

All passes emit diagnostics through `AnalysisContext`. See [`DIAGNOSTICS_EXAMPLE.md`](DIAGNOSTICS_EXAMPLE.md)
for usage patterns. Diagnostic codes are prefixed by pass area (CF=control flow, JT=jump target,
TH=this reference, etc.).
