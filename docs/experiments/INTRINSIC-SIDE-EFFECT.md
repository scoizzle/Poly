# Experiment: Intrinsic Side-Effect Classification

**Status: FAILED** — see [Resolution](#resolution). Intrinsic side-effect fails the 100% satisfiability test; stays as an external analysis pass.

## Hypothesis

Side-effect classification is currently a centralized switch in `SideEffectAnalyzer.ClassifyIntrinsic()`:

```csharp
private static SideEffectKind ClassifyIntrinsic(Node node) => node switch {
    Assignment => SideEffectKind.Write,
    Return     => SideEffectKind.Write,
    IndexAccess => SideEffectKind.Read,
    New        => SideEffectKind.Allocate,
    Invoke     => SideEffectKind.External,
    Await      => SideEffectKind.External,
    IfStatement or SwitchStatement or ThrowStatement => SideEffectKind.External,
    WhileLoop or DoWhileLoop or ForLoop or ForEachLoop => SideEffectKind.Write,
    BreakStatement or ContinueStatement or GotoStatement or LabelDeclaration => SideEffectKind.Write,
    SuspendNode => SideEffectKind.External,
    TryCatchFinally or UsingStatement => SideEffectKind.External,
    _ => SideEffectKind.Pure
};
```

Every node's classification is known at definition time — `Assignment` is always `Write`, `Constant` is always `Pure`, `Invoke` is always `External`. The switch is a centralized lookup table for constants that are already properties of each node type.

Moving the classification into an abstract property on `Node` would eliminate the switch and let each node declare its intrinsic side-effect directly:

```csharp
public abstract record Node {
    /// <summary>Side-effect kind when considered in isolation (no children).</summary>
    internal abstract SideEffectKind IntrinsicSideEffect { get; }
}

// Concrete:
public sealed record Assignment(Variable Target, Node Value) : Statement {
    internal override SideEffectKind IntrinsicSideEffect => SideEffectKind.Write;
}

public sealed record Constant(object? Value, TypeKind Type) : Expression {
    internal override SideEffectKind IntrinsicSideEffect => SideEffectKind.Pure;
}

public sealed record Invoke(Node Target, string MethodName, IReadOnlyList<Node> Arguments) : Expression {
    internal override SideEffectKind IntrinsicSideEffect => SideEffectKind.External;
}

// The 90% case — same as the switch, but distributed
```

## Motivation

### 1. Removes a maintenance burden

Every new AST node type currently needs:
1. The record definition
2. A new case in `ClassifyIntrinsic`'s switch (easy to forget — no compiler error if you do)

With an abstract property, the compiler forces every node to declare its intrinsic side-effect. Adding a new node cannot accidentally skip classification.

### 2. Parallel pattern emerges

This is the same pattern as adding `Expand(AnalysisContext)` for lowering — each node declares its own semantics rather than being classified by an external switch. The two properties (`IntrinsicSideEffect` + `Expand`) form a lightweight contract that each node fulfills independently:

| Property | Purpose |
|---|---|
| `IntrinsicSideEffect` | What is this node *in isolation*? (for elision, DCE, optimization) |
| `Expand(AnalysisContext)` | How does this node decompose to primitives? (for execution) |

Both are intrinsic invariants of the node type, not analysis results. Both eliminate centralized switches that grow linearly with the node count.

### 3. Composable aggregation

The `SideEffectAnalyzer` would shrink from ~130 lines of switch/block-specialization/aggregation to a generic walk:

```csharp
internal sealed class SideEffectAnalyzer : INodeAnalyzer {
    public void Analyze(AnalysisContext context, Node node) {
        if (!context.TryBeginAnalyzerVisit<SideEffectAnalyzer>(node))
            return;

        // Walk children first (post-order)
        AnalyzeChildren(context, node);

        // Aggregate: worst child + intrinsic
        var kind = node.IntrinsicSideEffect;
        foreach (var child in node.Children) {
            if (child is null) continue;
            var childMeta = context.GetMetadata<SideEffectMetadata>(child);
            if (childMeta is not null && childMeta.Kind > kind)
                kind = childMeta.Kind;
        }

        context.SetMetadata(node, new SideEffectMetadata(kind));
    }
}
```

No `ClassifyIntrinsic` switch. No `ClassifyWorst` hand-rolled ordinal. No special-casing for `Block` (the indexed loop optimization is a constant-factor win that may not matter in practice).

### 4. Framework-driven elision

The `Block`-specific elision logic (marking non-final pure children as elidable) is currently embedded in `SideEffectAnalyzer.Analyze()` as a special case — 40 lines of indexed-loop logic with manual `AssignmentValueUsedMetadata`. This is the *only* pass that needs to know about "last expression in block" semantics.

With `IntrinsicSideEffect` available generically, elision marking could be a **separate generic pass** that walks blocks and applies the rule uniformly — no special-casing in the classification pass.

## Potential concerns

### 1. Property vs static data

The abstract property is per-instance, but the value is constant per-type. In theory a `static abstract` interface member would be more correct:

```csharp
public interface IIntrinsicSideEffect {
    static abstract SideEffectKind IntrinsicSideEffect { get; }
}
```

But C# static abstract members can't be called polymorphically through a base class reference (no `node.IntrinsicSideEffect` — you'd need a switch or a helper method). A virtual instance property is the pragmatic choice even though it allocates per instance in theory — in practice the JIT devirtualizes and inlines the constant.

### 2. The `Block` optimization loss

The current `SideEffectAnalyzer` has a hand-optimized path for `Block` that uses indexed loops instead of `IEnumerable<Node>.Children`. If this matters (benchmarks would tell), the optimization can stay as a fast path in the analyzer that reads `IntrinsicSideEffect` instead of calling `ClassifyIntrinsic`:

```csharp
if (node is Block block) {
    // Fast indexed loop, same structure, but reads IntrinsicSideEffect
    // instead of ClassifyIntrinsic switch
    ...
}
```

But the initial implementation should try the generic path and measure.

### 3. `Member` volatility

The current pass has special-case code for `Member` nodes with volatile access (`Mutability.VolatileAccess`) that overrides the intrinsic Pure/Read classification with `SideEffectKind.Read`. This is genuine cross-cutting logic that doesn't belong on the node itself — it's an analysis result based on resolved member metadata.

This is fine — the intrinsic property says what the node *is by default*, and the analyzer can still override based on context:

```csharp
var kind = node.IntrinsicSideEffect;
// Override: volatile member access is always a Read
if (node is Member memberAccess) {
    var resolved = context.GetResolvedMember(memberAccess);
    if (resolved?.Mutability.HasFlag(Mutability.VolatileAccess) == true)
        kind = SideEffectKind.Read;
}
// Then aggregate children
```

The intrinsic is the baseline; analysis refines it.

## Impact

### Code eliminated

| File | What | Lines |
|---|---|---|
| `SideEffectAnalysisPass.cs` | `ClassifyIntrinsic` switch + `Block`-specific elision | ~130 |
| `SideEffectAnalysisPass.cs` | `HasSideEffects` / `CanElide` extensions (moved to Node) | ~20 |

### Code added

| File | What | Lines |
|---|---|---|
| `Node.cs` | `IntrinsicSideEffect` abstract property | 1 |
| Each node file (15+) | Override returning the appropriate constant | 1 each |

### Net change

~130 lines of centralized switch → ~15 lines of distributed overrides. The analyzer shrinks by ~80%.

## Relationship to lowering expansion

This is the same architectural pattern as replacing `Emit(EmissionContext)` + `UopCompiler` + IR with `Expand(AnalysisContext)` + label fixup + ring allocation. Both are examples of **moving semantics from external switches into intrinsic node declarations**.

The two patterns together form a coherent design principle:

> **Each AST node says what it IS (IntrinsicSideEffect) and how it LOWERS (Expand). Everything else is a derived view.**

This directly parallels the AGENTS.md principle "Name things for what they ARE" — applied to behavior rather than naming.

## Resolution

### The 100% satisfiability test

An intrinsic moved to `Node` must be **definable by every concrete node type without lying or deferring to analysis metadata**. `IntrinsicSideEffect` fails this test:

| Node | Would need to say | But actually depends on |
|---|---|---|
| `New` | `Allocate` or `External` | Constructor resolution — some constructors have side effects |
| `Invoke` | `External` | Always true at definition time, but not all invocations are equal |
| `Member` | `Pure` | Volatile-qualified or property-getter members are `Read` |
| `IfStatement` | `External` | Pure if both branches are pure — child-dependent |

These nodes cannot declare an intrinsic side-effect that is both correct and complete without access to resolved metadata. Forcing an abstract property would either produce wrong classifications or require escape hatches that defeat the purpose.

### The boundary: intrinsic reduction vs derived classification

This reveals a clean architectural boundary:

**Core AST operations** (things the AST fundamentally IS or DOES) → abstract members on `Node`:
- `Children` — tree traversal
- `ToString()` — display
- `Expand(AnalysisContext)` — reduction to `PrimitiveNode` sequence

**Derived analysis results** (things computed FROM the AST) → external analysis passes:
- `IntrinsicSideEffect` / side-effect classification
- `ResolvedType` — type resolution
- `DefiniteAssignment` — data-flow analysis

The test for which side a property falls on: *Can every concrete node type implement this meaningfully based on its own structure, without lying?*

- `Expand` passes: every node has a fixed expansion shape. `Invoke` expands to `[..args, Call]` regardless of which method is being called — the `Call` primitive carries the resolved target as metadata, but the *structure* is intrinsic.
- `IntrinsicSideEffect` fails: `New` can't say whether it allocates or mutates without knowing the constructor.

### Decision

`IntrinsicSideEffect` stays as a centralized switch in `SideEffectAnalyzer`. The `Expand(AnalysisContext)` pattern on `Node` is architecturally sound because it's a **reducer** — the structured AST reduces to the primitive substrate as a fundamental system operation, not a pass-specific classification.
