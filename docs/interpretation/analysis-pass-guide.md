# Analysis Pass Guide

This document provides a step-by-step guide to creating, registering, and testing
semantic analysis passes for the Poly Interpretation system.

## Overview

Analysis passes implement `INodeAnalyzer` and are composed into an `Analyzer` via
`AnalyzerBuilder`. Each pass walks the AST in post-order, attaches metadata to nodes,
and reports diagnostics. Passes declare dependencies to ensure correct execution order.

## Pass Lifecycle

1. **Registration**: Added to `AnalyzerBuilder` via an extension method
2. **Analysis**: `Analyzer.Analyze(rootNode)` runs all passes in dependency order
3. **Metadata**: Passes attach `IAnalysisMetadata` records to nodes via `AnalysisContext`
4. **Retrieval**: Downstream passes and the emitter read metadata via `context.GetMetadata<T>(node)`

## Step 1: Define Metadata Types

Create a record implementing `IAnalysisMetadata`:

```csharp
/// <summary>Metadata describing the widget count for a node.</summary>
/// <param name="Count">The number of widgets this node references.</param>
public sealed record WidgetCountMetadata(int Count) : IAnalysisMetadata;
```

Metadata records should be:
- **Immutable** (use `record` or `sealed record`)
- **Serialization-friendly** (prefer primitive types in constructor)
- **Self-documenting** (include XML doc on the record and each parameter)

## Step 2: Implement the Pass

```csharp
using Poly.Syntax.Analysis;
using Poly.Syntax.Nodes;

namespace Poly.Interpretation.Analysis.Semantics;

internal sealed class WidgetAnalyzer : INodeAnalyzer {
    public const string Id = "Widget";
    public string PassName => Id;
    public string[] Dependencies => [TypeAndMemberResolver.Id];

    public void Analyze(AnalysisContext context, Node node) {
        // Guard: skip nodes already visited by this pass
        if (!context.TryBeginAnalyzerVisit<WidgetAnalyzer>(node))
            return;

        // Post-order: visit children first
        this.AnalyzeChildren(context, node);

        // Analyze this node
        if (node is SomeExpression expr) {
            int count = CountWidgets(expr);
            context.SetMetadata(node, new WidgetCountMetadata(count));
        }

        // Report diagnostics
        if (node is BadPattern bad && SomeCondition(bad)) {
            context.ReportDiagnostic(bad, DiagnosticSeverity.Warning,
                "Description of the potential issue", "WD0001");
        }
    }

    private static int CountWidgets(SomeExpression expr) {
        // Implementation
        return 0;
    }
}
```

### Key Methods on `AnalysisContext`

| Method | Purpose |
|--------|---------|
| `TryBeginAnalyzerVisit<T>(node)` | Guard against re-visiting nodes. Returns false if already visited by this pass type. |
| `SetMetadata(node, metadata)` | Attach metadata to a node |
| `GetMetadata<T>(node)` | Retrieve metadata of type `T` from a node |
| `GetResolvedType(node)` | Get the resolved `ITypeDefinition` for a node |
| `SetResolvedType(node, type)` | Set the resolved type for a node |
| `ReportDiagnostic(node, severity, message, code)` | Emit a diagnostic |
| `ReportInformation(node, message, code)` | Emit an informational diagnostic |
| `ShouldAnalyze(node)` | Check if a node should be analyzed (non-null, not replaced) |
| `SetNodeReplacement(node, replacement)` | Replace a node with another (used by constant folding) |

## Step 3: Register the Pass

Add an extension method on `AnalyzerBuilder`:

```csharp
public static class WidgetPassExtensions {
    extension(AnalyzerBuilder builder) {
        public AnalyzerBuilder UseWidgetAnalysis() {
            builder.AddPass(state => new WidgetAnalyzer());
            return builder;
        }
    }
}
```

The extension method pattern (`extension(AnalyzerBuilder builder)`) uses C# 
extension types for discoverable fluent syntax.

## Step 4: Add to the Pipeline

In your application code:

```csharp
var analyzer = new AnalyzerBuilder()
    .UseThisReferenceContext()
    .UseTypeAndMemberResolver()
    .UseVariableScopeValidator()
    .UseSideEffectAnalysis()
    .UseWidgetAnalysis()  // <-- new pass
    .UseJumpTargetResolution()
    .UseControlFlowAnalysis()
    // ... remaining passes ...
    .Build();
```

If adding to the standard pipeline in `Interpreter.cs`, update the pass table
in both `Interpretation/README.md` and `Analysis/README.md`.

## Step 5: Write Tests

Tests use TUnit and should verify:
1. Metadata is correctly attached to the expected nodes
2. Diagnostics are emitted for error conditions
3. No diagnostics are emitted for valid code
4. The pass handles edge cases (empty blocks, nested constructs)

```csharp
[Test]
public async Task WidgetCount_SimpleExpression_ReturnsCorrectCount() {
    var node = new SomeExpression(/* ... */);
    var analysis = Interpreter.Analyze(node);
    var metadata = analysis.GetMetadata<WidgetCountMetadata>(node);
    await Assert.That(metadata?.Count).IsEqualTo(42);
}
```

## Pass Dependencies

| Dependency | Meaning |
|------------|---------|
| `[]` | No dependencies — pass can run first |
| `[TypeAndMemberResolver.Id]` | Needs resolved types and members |
| `[SideEffectAnalyzer.Id]` | Needs side-effect classification |
| `[ControlFlowAnalysisPass.Id]` | Needs CFG and reachability information |

The `AnalyzerBuilder` topological sorts passes. Circular dependencies cause
a build-time exception.

## Metadata Scoping

| Scope | Key | How |
|-------|-----|-----|
| Per-node | Specific AST node | `context.SetMetadata(node, meta)` |
| Module-wide | `null` key | `context.SetMetadata(null, meta)` on the root node |

Passes should prefer per-node metadata for node-specific information and
module-wide (null-key) metadata for aggregate data like global tables.

## Best Practices

1. **Guard with `TryBeginAnalyzerVisit`** — prevents double-processing when
   the same node is reachable through multiple parents
2. **Post-order traversal** — analyze children before the parent; use
   `this.AnalyzeChildren(context, node)` for recursion
3. **Immutable metadata** — records are strongly preferred; avoid mutable
   metadata objects that could be shared across threads
4. **Declare all dependencies** — be honest about what your pass needs;
   missing dependencies cause subtle ordering bugs
5. **Test diagnostics** — verify both presence and absence of diagnostics
6. **Document diagnostic codes** — add codes to the pass table in
   `Analysis/README.md` with a brief description

## Common Patterns

### Collecting Aggregate Information
Use module-wide (null-key) metadata for tables:

```csharp
public void Analyze(AnalysisContext context, Node node) {
    if (!context.TryBeginAnalyzerVisit<MyPass>(node)) return;
    this.AnalyzeChildren(context, node);

    if (node is InterestingThing thing) {
        var table = context.GetMetadata<MyTable>(null)
            ?? new MyTable(new List<Thing>());
        table.Items.Add(thing);
        context.SetMetadata(null, table);
    }
}
```

### Aborting Analysis on First Error
Some passes (like type resolution) should stop processing a subtree on error:

```csharp
if (/* error condition */) {
    context.ReportDiagnostic(node, DiagnosticSeverity.Error, "...", "ERR001");
    return; // Don't analyze children
}
```

### Providing Node Replacements
Constant folding demonstrates replacing nodes:

```csharp
var simplified = TrySimplify(context, node);
if (simplified != null) {
    context.SetNodeReplacement(node, simplified);
}
```

Downstream consumers check `context.GetNodeReplacement(node)` to use the
replacement instead of the original node.
