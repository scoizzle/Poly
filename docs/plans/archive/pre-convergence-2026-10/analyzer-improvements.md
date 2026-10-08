# Plan: Analyzer Improvements (Shift Work Up the Chain)

**Date:** 2026-06-08  
**Goal:** Move correctness logic from VM hacks into the analysis passes, so the lowering and VM receive accurate information without guessing.

---

## 1. Lambda Return Type Resolution

**Problem:** `Invoke(Lambda([], body), [])` resolves to `typeof(object)` or `typeof(void)`. The lowering then normalizes `typeof(object)` to null, and `ExtractResult` has to guess whether the result is a heap handle or raw int.

**Fix:** The `TypeResolver` pass should resolve a lambda's return type by examining the body's last expression type (for `Block` → last node's type, for other nodes → the node's resolved type).

**Implementation sketch:**

```csharp
// In TypeResolver, Analyze method:
case Lambda lambda:
    var returnType = ResolveNodeType(context, lambda.Body);
    // ... a Block's type resolves to its last node's type
    context.SetResolvedType(lambda, returnType);
    break;

// Then in Invoke:
case Invoke invoke when invoke.Delegate is Lambda lambda:
    var resolvedType = context.GetResolvedType(lambda);
    context.SetResolvedType(invoke, resolvedType);
    break;
```

**Downstream effects:**
- `Invoke(Lambda([], Constant(42)), [])` → `resultType = typeof(int)` → ExtractResult uses the typed int path (no guessing)
- `Invoke(Lambda([], Variable("x")), [])` → resolves to `x`'s type (e.g., `typeof(int)` or `typeof(string)`)
- The `typeof(object) → null` hack in `Lower()` can be removed
- The `val > 0` guard in `ExtractResult` can be tightened or removed

**Risk:** Recursive or mutually-recursive lambdas may have unresolvable types (cycles). Fall back to `typeof(object)` for those.

---

## 2. Variable Scope Metdata Exfiltration

**Problem:** The lowering re-discovers locals and captures by walking the AST in `DiscoverLocals` and `DiscoverCapturesWalk`. This duplicates work the `VariableScopeValidator` already did.

**Fix:** The `VariableScopeValidator` pass or a new lightweight pass stores variable scope information in analysis metadata. The lowering queries this instead of re-walking.

**Implementation sketch:**

```csharp
// New metadata type:
public sealed record VariableScopeMetadata(
    IReadOnlySet<string> Locals,     // variable names defined in this scope
    IReadOnlySet<string> Captures    // variable names captured from enclosing scopes
);

// Analysis pass populates this for each function/lambda scope:
context.SetMetadata(lambda.Body, new VariableScopeMetadata(locals, captures));

// Lowering queries:
var scope = analysis.GetMetadata<VariableScopeMetadata>(lambda.Body);
if (scope is not null) {
    // Use scope.Locals for localIndexMap
    // Use scope.Captures for capture list
}
```

**Downstream effects:**
- Eliminates `DiscoverLocals` and `DiscoverCapturesWalk` from `Lowering.cs`
- Single source of truth for variable scoping
- The `VariableScopeValidator` already catches scope errors; metadata reuse ensures lowering agrees with validation

---

## 3. `EmitsValue` from Analysis

**Problem:** `EmitsValue` is a hardcoded switch of Node types that must be manually kept in sync with the type system. New node types need two updates (the switch + the node).

**Fix:** `EmitsValue` uses `analysis.GetResolvedType(node)` when available, falling back to the switch for nodes without analysis context.

```csharp
private static bool EmitsValue(Node node, AnalysisResult? analysis = null) {
    if (analysis is not null) {
        var type = analysis.GetResolvedType(node);
        if (type is not null)
            return type.GetRuntimeType() != typeof(void);
    }
    // fallback switch for non-analyzed contexts
    return node switch { ... };
}
```

**Downstream effects:**
- New expression nodes automatically get correct `EmitsValue` behavior when their type is resolved
- The manual switch becomes a fallback, reducing maintenance burden
- `UsingStatement` and `ForEachLoop` would have been automatically correct without the manual fix
