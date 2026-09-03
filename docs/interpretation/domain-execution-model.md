# Domain Execution Model — Developer's Guide

**Audience:** Developers extending the DomainModeling module or adding new effect/expression types.  
**Prerequisites:** [`Poly/Interpretation/README.md`](../../Poly/Interpretation/README.md), [`docs/CORE.md`](../CORE.md).  
**Corpus:** Lowering passes live under `Poly/DomainModeling/Lowering/`. Runtime dispatch lives in `Poly/DomainModeling/Runtime/DomainEntityInstance.cs`. Policy evaluation runs through `DomainEntityInstance.EvaluatePolicy` → `DomainExpressionLoweringPass` → `Interpreter`; the CLR-subject wrapper `PolicyEvaluator` is test-only (`Poly.Tests/TestHelpers/`).

This document describes the execution model that bridges **domain concepts** (effects, policies, expressions) to **executable results** through the Syntax AST and VM pipeline. It is the missing layer between `Poly.DomainModeling` and the Interpretation system documented in this directory.

---

### Design Principle: Lowering is VM-ABI-Unaware

The lowering passes (`DomainExpressionLoweringPass`, `EffectLoweringPass`) produce Syntax AST that is **semantically faithful to the domain model** — not optimized or shaped for the VM's ABI. The Syntax AST is a general-purpose imperative IR (`Member`, `Constant`, `Assignment`, `Block`, `IfStatement`, ...). If the VM (or any consumer) cannot handle a form that the lowering pass emits, the fix goes in the consumer — not in the lowering pass.

**What this means in practice:**

| Concern | Belongs in |
|---------|-----------|
| What Syntax AST node represents an `AssignEffect` | **Lowering pass** (the domain model's semantics) |
| How to compile a `Member` node | **`DirectVmAbiEmitter`** (or any other consumer) |
| Whether a `Block` with zero expressions is valid | **Syntax AST** type system (all consumers must agree) |
| How to execute store-aware quantifiers | **`DomainEntityInstance`** (preprocessing, see §4) or a future Syntax AST extension |
| Register allocation, frame layout, calling convention | **VM** — not the lowering pass |

The lowering pass should never produce `Constant(0L)` as a NOP placeholder because the VM needs a value to consume. If the Syntax AST's type system disallows certain shapes (e.g., empty `Block`), the fix is in the Syntax AST itself — not a workaround in lowering. If the VM's ABI requires a value where the domain has none, the VM handles that — not the lowering pass.

### Design Principle: Lower Like You'd Write C#

The lowering pass should emit Syntax AST that mirrors how a C# developer would express the same domain concept. The Syntax AST is already a C#-ish IR (`Member` for `.`, `Invoke` for `()`, `Assignment` for `=`, `IfStatement` for `if`, `Block` for `{}`). If the domain model has an effect that would be a method call in C#, lower it as `Member` + `Invoke`. If it would be a `foreach` loop, lower it as `ForEachLoop`.

| Domain concept | C# equivalent | Lowered to |
|----------------|---------------|------------|
| `PropertyAccess("Name")` | `entity.Name` | `Member(entity, "Name")` |
| `RelationshipNavigation("customer", PropertyAccess("Tier"))` | `entity.customer.Tier` | `Member(Member(entity, "customer"), "Tier")` |
| `AssignEffect(target, value)` | `target = value` | `Assignment(target, value)`; unique properties wrap `EnsureUnique` then assign |
| `ConditionalEffect(cond, then, else)` | `if (cond) { ... } else { ... }` | `IfStatement(cond, thenBlock, elseBlock?)` |
| `CompositeEffect(stmts)` | `{ stmt1; stmt2; }` | `Block(nodes)` |
| Self-invoke `InvokeActionEffect("Activate")` | `this.Activate()` | `Invoke(Member(This, "Activate"), args)` |
| Cross-entity invoke | `this.customer.Activate()` | `this.Rel.Action(args)` with a `DomainResult.Failure` linked-target guard |
| For-invoke | `foreach (var x in Rel) { x.Action(...); }` | Fail-fast `ForEachLoop` over a **OneToMany** collection nav |

When a domain concept cannot be expressed in the current Syntax AST (e.g., store-aware collection operations for quantifiers), the fix goes into the Syntax AST or VM — not into lowering workarounds. Do not emit `Comment` as shipped meaning (§2d). Unique assign binds Store via `EnsureUnique` (Notify-shaped). Remaining store effects (create / create-in) still return `null` on the runtime path for relationship-coupled create.

---

## 1. Pipeline Overview

```
┌──────────────────────────────────────────────────────────────────┐
│                      Domain Model Layer                          │
│                                                                  │
│  Policy   Effect   DomainExpression   DomainEntityInstance        │
│     │        │           │                    │                  │
│     ▼        ▼           ▼                    ▼                  │
│  ┌──────────────────────────────────────────────────────────┐    │
│  │                 Lowering Layer                            │    │
│  │  DomainExpressionLoweringPass   EffectLoweringPass       │    │
│  │  (DomainExpression → Syntax.Node)  (Effect → Syntax.Node)│    │
│  └─────────────────────┬────────────────────────────────────┘    │
│                        │                                         │
│                        ▼                                         │
│  ┌──────────────────────────────────────────────────────────┐    │
│  │              Syntax AST (Poly.Syntax.Nodes)              │    │
│  │  65+ node types — Member, Constant, Assignment, Block,  │    │
│  │  IfStatement, Equal, Add, Invoke, Parameter, etc.       │    │
│  └──────────────────────────┬───────────────────────────────┘    │
│                             │                                    │
│                             ▼                                    │
│  ┌──────────────────────────────────────────────────────────┐    │
│  │        Interpretation System (Poly.Interpretation)       │    │
│  │  Interpreter.Compile → DirectVmAbiEmitter → VmProgram   │    │
│  │  Interpreter.Execute → VmState → ExecutionResult        │    │
│  └──────────────────────────────────────────────────────────┘    │
└──────────────────────────────────────────────────────────────────┘
```

Two distinct lowering passes produce Syntax AST nodes:

| Pass | Input | Output | Inherits |
|------|-------|--------|----------|
| `DomainExpressionLoweringPass` | `DomainExpression` tree | `Syntax.Node` | `DomainExpressionDispatch<Node>` |
| `EffectLoweringPass` | `Effect` tree | `Syntax.Node?` (`null` only for remaining create / create-in on the runtime path) | `EffectDispatch<Node?>` |

Both consume a `LoweringContext` that carries the current-instance `Subject` and optional parameter map.

---

## 2. Dual-Path Effect Execution

All effects **lower to Syntax AST on both runtime and emit**. Create / create-in use different factory shapes: runtime lowers to `InvokeNamed("CreateByType"/"CreateInNav")` factories; C# emit uses `Stay.Create`/`this.CreateNav`. `EffectExecutor` is deleted. `ExecuteStructured` remains for unique-assign if/else and store-coupled creates (`CreateEntityInstance` with `RelationshipName`).

### 2a. VM-Compiled Path (Lowering → Compile → Execute)

For effects that map directly to Syntax AST nodes. The effect is lowered, compiled via `Interpreter.Compile`, and executed against the instance's property bag.

| Effect | Lowered to | Notes |
|--------|-----------|-------|
| `AssignEffect` | `Assignment(target, value)` | Target and value lowered via `DomainExpressionLoweringPass` |
| `CompositeEffect` | `Block(nodes)` | Sub-effects lowered recursively. A sub-effect that cannot lower throws (no silent drop). Mixed create children use `ExecuteStructured` at runtime |
| `ConditionalEffect` | `IfStatement(cond, thenBlock, elseBlock?)` | Then/else effects lowered recursively; same fail-closed rule |
| `StageTransitionEffect` | Assignment of `CurrentStage` + `Invoke(Member(This, "Notify"), stageName)` (plus inlined exit/entry when they lower) | Same tree on runtime and emit. `Notify` is an instance method on This, resolved via the type def. Not a host-ABI node. Not an EffectExecutor arm |
| `InvokeActionEffect` | Self: `Invoke(Member(This, action), args)`. Cross-entity: `this.Rel.Action(args)` with a linked-target `DomainResult.Failure` guard | Same tree on runtime and emit. Not gated on `LowerStageTransitions`. Self / singular cross-entity do not wrap `IsSuccess` |
| `ForEachInvokeEffect` | Fail-fast `ForEachLoop` over a **OneToMany** collection nav | Zero-match `DomainResult.Failure`; per-item `if (!result.IsSuccess) return result`. VM walks `IList` (fail-loud non-IList). Analysis rejects ManyToMany / OneToOne |

```csharp
var lowered = effectPass.TryLowerVmNode(effect);
if (lowered is not null) {
    var compiled = Interpreter.Compile(lowered, typeProvider);
    using var exec = Interpreter.Execute(compiled, s => s.SetArgs(new object?[] { this }));
    return;
}
```

### 2b. Direct-Execution Path (Residual)

`CreateEntityInstance` with `RelationshipName` stays on the direct path for store-coupled auto-linking validation (source/target checks). All other effects lower through VM.

> **Removed 2026-08-10:** `DeleteEntityInstance`, `LinkRelationshipEffect`, `UnlinkRelationshipEffect`,
> and `TransitionRelationshipEffect` were deleted. Linking existing instances is `DomainInstanceStore.Link`
> / `DomainInstanceStore.Unlink` (MCP `link_instances` / `unlink_instances`) — a store operation, not an
> Effect IR.

```csharp
// EffectExecutor deleted — all effects lower to Syntax AST.
// Leaf create / create-in: _lowerRuntimeCreate defaults to true in runtime mode.
// CreateEntityInstance with RelationshipName stays on CreateChildInstance (store auto-linking).
```

### 2c. Dispatch Decision Tree

```
ExecuteEffect(effect)
  │
  ├─ unique-assign if/else or store-coupled creates
  │   → ExecuteStructured (each sub-effect through ExecuteEffect)
  │
  ├─ CreateEntityInstance with RelationshipName
  │   → CreateChildInstance (direct: store auto-linking validation)
  │
  ├─ EffectLoweringPass.TryLowerVmNode(effect)
  │   │
  │   ├─ AssignEffect  → Assignment
  │   ├─ CompositeEffect → Block (cannot-lower sub-effect throws)
  │   ├─ ConditionalEffect → IfStatement
  │   ├─ StageTransitionEffect → CurrentStage Assignment + Invoke Notify on This
  │   ├─ InvokeActionEffect → Invoke(Member(...)) (self or Rel with Failure guard)
  │   ├─ ForEachInvokeEffect → ForEachLoop (IList walk; fail-loud non-IList)
  │   ├─ CreateEntityInstance → InvokeNamed("CreateByType") (runtime)
  │   └─ CreateEntityInRelationship → InvokeNamed("CreateInNav") (runtime)
  │
  └─ returned Node ≠ null
      → Interpreter.Compile(lowered)
      → Interpreter.Execute(compiled, args: this)
      → failed DomainResult returned
```

### 2d. Comment is not shipped meaning

`EffectLoweringPass` does **not** emit `Comment` nodes. A composite/conditional sub-effect that cannot lower throws `InvalidOperationException`. All effects lower on both runtime and emit paths (different factory shapes for create). `_lowerRuntimeCreate` defaults to true in runtime mode. The `Comment` AST node is not product meaning and must not be used as a lowering-gap marker. Sequential transitions update `SourceStageName` after each transition so exit effects use the correct source stage.

---

## 3. Policy Evaluation

Policies are boolean guard expressions attached to entities, stages, or actions. Evaluation follows a **preprocess → lower → compile → execute** pipeline.

### 3a. Full Path

```
Policy.Expression (DomainExpression)
  │
  ├─ PreprocessQuantifiers(expr)
  │   Walks the expression tree, evaluates AnyExpr/AllExpr/NoneExpr/CountExpr
  │   against the instance store's linked targets. Replaces quantifier nodes
  │   with Literal(true/false/count). Non-quantifier composites recurse.
  │   Returns a quantifier-free DomainExpression.
  │
  ├─ DomainExpressionLoweringPass.Lower(expr, entityParam)
  │   Lowers the preprocessed expression to Syntax AST.
  │   Produces a tree of Member, Constant, Equal, And, etc.
  │
  ├─ Interpreter.Compile(lowered, typeDefAnalyzer)
  │   Analyzes + emits VM program targeting the instance's type definition.
  │   Member nodes resolve through ITypeDefinitionProvider → dictionary indexer.
  │
  ├─ Interpreter.Execute(compiled, args: this)
  │   Runs the compiled program with the instance as args (This).
  │
  └─ exec.Result.GetValue<bool>()
      Returns the boolean result.
```

```csharp
// DomainEntityInstance.EvaluatePolicy
public bool EvaluatePolicy(Policy policy) {
    var expr = PreprocessQuantifiers(policy.Expression);

    var entityParam = new Parameter("entity", new TypeReference(Entity.Name));
    var pass = new DomainExpressionLoweringPass();
    var lowered = pass.Lower(expr, entityParam);

    var compiled = Interpreter.Compile(lowered, _typeDefAnalyzer);
    using var exec = Interpreter.Execute(compiled,
        s => s.SetArgs(new object?[] { this }));
    return exec.Result.GetValue<bool>();
}
```

### 3b. Action Guard Pipeline

When `InvokeAction` is called, policies are evaluated in order:

```
InvokeAction(actionName, args)
  │
  ├─ Inject args into _values (action parameter bag)
  ├─ Evaluate action-level policies    → if any fail → Blocked
  ├─ Evaluate current-stage policies   → if any fail → Blocked
  ├─ Evaluate entity-level policies    → if any fail → Blocked
  ├─ Execute effects (see §2)
  └─ Clean up args from _values
```

---

## 4. Quantifier Preprocessing

Q3′ quantifiers (`any`, `all`, `none`, `count`) cannot be lowered to Syntax AST + VM because they require **store-aware evaluation** — looking up linked instances, evaluating per-target, and aggregating. The solution is a preprocessing step before lowering.

### 4a. Walk and Replace

`PreprocessQuantifiers` recursively walks the expression tree:

```csharp
private DomainExpression PreprocessQuantifiers(DomainExpression expr) {
    return expr switch {
        AnyExpr a  => DomainExpression.Literal(EvaluateAnyExpr(a)),
        AllExpr a  => DomainExpression.Literal(EvaluateAllExpr(a)),
        NoneExpr n => DomainExpression.Literal(EvaluateNoneExpr(n)),
        CountExpr c => DomainExpression.Literal(EvaluateCountExpr(c)),

        // Composite expressions — recurse and rebuild via `with`
        And a       => a with { Left = PreprocessQuantifiers(...), Right = PreprocessQuantifiers(...) },
        Comparison c => c with { Left = ..., Right = ... },
        // ... 9 more composite types

        // Leaf nodes — no quantifiers possible inside
        PropertyAccess | ParameterAccess | Literal => expr,
    };
}
```

### 4b. Quantifier Evaluation

| Quantifier | Implementation |
|-----------|---------------|
| `Any` | Iterate linked targets via `GetOutboundRelatedInstances`, evaluate body on each, return true on first match. False if none match. |
| `All` | Iterate linked targets. If any fails the body, return false. Empty set → false (no vacuous success). |
| `None` | `!AnyExpr(rel, body)` — delegates to Any. |
| `Count(body?)` | Without body: return `targets.Count`. With body: iterate, count matches. |

```csharp
private bool EvaluateAnyExpr(AnyExpr a) {
    var targets = GetOutboundRelatedInstances(a.RelationshipName);
    foreach (var t in targets)
        if (EvaluateBodyOnTarget(a.Body, t))
            return true;
    return false;
}

private static bool EvaluateBodyOnTarget(DomainExpression body, DomainEntityInstance target) {
    var pass = new DomainExpressionLoweringPass();
    var lowered = pass.Lower(body, new Parameter("entity", new TypeReference(target.Entity.Name)));
    var compiled = Interpreter.Compile(lowered, target._typeDefAnalyzer);
    using var exec = Interpreter.Execute(compiled, s => s.SetArgs(new object?[] { target }));
    return exec.Result.GetValue<bool>();
}
```

### 4c. Design Rationale

- **Preprocessing keeps the VM path quantifier-free.** DomainExpressionLoweringPass throws `NotSupportedException` for quantifier nodes — they must never reach it.
- **Per-target evaluation reuses the same lowering path** as policy evaluation, but compiled against each target's own type definition.
- **The dual path is temporary.** A future store-aware VM node or dedicated lowering path could replace preprocessing with inline execution.

---

## 5. Cross-Entity and For-Invoke Flow

`InvokeActionEffect` and `ForEachInvokeEffect` lower to Syntax AST on both runtime and emit. There is no `ExecuteInvokeEffect`. Runtime `This` has no CLR method for domain actions, so `InvokeNamed` runs the action (Notify still hits the real CLR method first).

| Case | Lowered to | Behavior |
|------|------------|----------|
| Self-invoke | `Invoke(Member(This, action), args)` | Analysis sees the action on the type def; C# prints `this.Checkout()`. Nested Failure is discarded like C# `this.Foo();` |
| Singular cross-entity | `this.Rel.Action(args)` with a linked-target `DomainResult.Failure` guard before deref | Same tree on runtime and emit. Not gated on `LowerStageTransitions`. Does not wrap `IsSuccess` |
| For-invoke | Fail-fast `ForEachLoop` over a **OneToMany** collection nav | Analysis rejects ManyToMany / OneToOne. VM walks `IList` (fail-loud non-IList). Per-item `if (!result.IsSuccess) return result`. Zero-match `DomainResult.Failure`; `ExecuteEffect` throws on a failed program result |

`GetOutboundRelatedInstances` remains the store path for Q3′ quantifiers, path-prefix, and `Rel exists` preprocessing — not for invoke dispatch.

---

## 6. Lowering Pass Architecture

### 6a. Dispatch Bases

Two abstract dispatch bases own **one switch statement each** for their respective type hierarchy.

| Base Class | Hierarchy | Default behavior |
|-----------|-----------|------------------|
| `EffectDispatch<TResult>` | 11 Effect subtypes | `Default()` returns per-concern fallback |
| `DomainExpressionDispatch<TResult>` | 20 expression subtypes | `Default()` returns per-concern fallback |

Each base has:
- A `Route(Effect/DomainExpression)` method with an exhaustive switch — **adding a subtype causes a compile error here**
- Virtual methods named by the subtype (`StageTransition`, `PropertyAccess`, etc.) — no `Visit*` pattern names
- The default catch-all throws `NotSupportedException` for unknown types

```csharp
public abstract class EffectDispatch<TResult> {
    protected abstract TResult Default();

    protected virtual TResult StageTransition(StageTransitionEffect e) => Default();
    protected virtual TResult Assign(AssignEffect e) => Default();
    // ... one per subtype

    public TResult Route(Effect effect) => effect switch {
        StageTransitionEffect e => StageTransition(e),
        AssignEffect e          => Assign(e),
        // ... every arm explicit
        _ => throw new NotSupportedException($"...")
    };
}
```

### 6b. Concrete Subclasses

| Concern | Subclass | Result Type | Pattern |
|---------|----------|-------------|---------|
| Expression → Syntax AST | `DomainExpressionLoweringPass` | `Node` | Inherits `DomainExpressionDispatch<Node>` |
| Effect → Syntax AST | `EffectLoweringPass` | `Node?` | Inherits `EffectDispatch<Node?>`; null = remaining create / create-in on the runtime path |
| Effect → runtime mutation | `EffectExecutor` (nested in `DomainEntityInstance`) | `object?` | Inherits `EffectDispatch<object?>` |
| Effect → DSL text | `EffectPrinter` (nested in `DomainDslPrinter`) | `object?` | Inherits `EffectDispatch<object?>` |
| Expression → DSL text | `ExpressionPrinter` (nested in `DomainDslPrinter`) | `string` | Inherits `DomainExpressionDispatch<string>` |

### 6c. LoweringContext

Both lowering passes accept `LoweringContext`, a bundle carrying the current-instance `Subject` and optional `Parameters`:

```csharp
public sealed record LoweringContext(
    Node Subject,
    IReadOnlyDictionary<string, Node>? Parameters = null
);
```

This ensures both passes see the same context and eliminates the mismatch where `EffectLoweringPass` previously created its own `DomainExpressionLoweringPass` without sharing parameters.

---

## 7. Relationship Navigation Lowering

`RelationshipNavigation("customer", PropertyAccess("Tier"))` lowers to a Member chain:

```csharp
// DomainModeling:
RelationshipNavigation("customer", PropertyAccess("Tier"))

// Lowered Syntax AST:
Member(Member(entityParam, "customer"), "Tier")
```

The outer `Member(entityParam, "customer")` resolves the relationship name. At the Syntax AST level, this is an unresolved Member — it has no analysis metadata because "customer" is a relationship name, not a CLR property. The VM's `EmitResolvedMember` fallback for unresolved members returns the entire instance as a passthrough (the instance is the `IDictionary`). The inner `Member("Tier")` then resolves via the standard `ITypeDefinitionProvider` path (dictionary indexer on the target entity's type definition).

**This works but is semantically misleading.** The outer Member should conceptually read "navigate the relationship," not "access a property." Future work may introduce a dedicated navigation IR node that makes this distinction explicit.

---

## 8. InstanceStore Subscription Fan-Out

`DomainInstanceStore.NotifyTransition` fires stage-subscription effects when an instance transitions to a new stage. This is **not** an effect execution path — it's a store-level mechanism.

```
Instance transitions to stage
  → Store.NotifyTransition(instance, targetStage, depth)
    → Find relationships where the instance is the Target
    → For each subscriber instance
      → Find current-stage subscriptions matching the relationship + stage
      → For each quantifier:
        - Each: fire per matching instance
        - Any: fire once if at least one matching
        - All: fire once if all match
      → Execute subscription effects (recursive InvokeAction, depth-limited to 10)
```

This is the runtime counterpart of `StageSubscription` declarations in the DSL.

---

## 9. Known Limitations & Future Work

| Area | Current | Desired |
|------|---------|---------|
| **Quantifier lowering** | Preprocessed out before lowering (dual path) — the Syntax AST and VM have no store-aware nodes | Store-aware lowering node or dedicated Syntax AST extension; fix goes in Syntax/VM, not lowering |
| **Relationship navigation** | Lowered as `Member(Param, relName)` — semantically works via VM's unresolved-Member fallback | Dedicated navigation Syntax node with explicit semantics; fix goes in Syntax AST |
| **Effect lowering** | All effects lower to Syntax AST on both paths. Create uses runtime factories (`CreateByType`/`CreateInNav`) vs C# `Stay.Create`. `EffectExecutor` deleted. Sequential transitions update `SourceStageName`. Emit path runs `Interpreter.Analyze` on the full projected unit (generic type-parameter / closed-generic / short-name resolve in Interpretation). | `CreateEntityInstance` with `RelationshipName` stays direct for store auto-linking. `ExecuteStructured` remains for unique-assign if/else. |
| **VM quantifier eval** | Per-target re-lowering + compile is expensive for large collections | Cached lowering or batch evaluation in VM |
| **ParameterAccess in DSL** | Product spelling is a **bare identifier** (`PropertyAccess`) — analysis/lowering/bindings/runtime treat an in-scope action-parameter name as a parameter (`paramEnv`); there is no distinct `param` keyword or `@param` form | L3 — no separate parameter authoring syntax |

---

## 10. Related Documents

| Document | Purpose |
|----------|---------|
| [`Poly/Interpretation/README.md`](../../Poly/Interpretation/README.md) | Interpretation system overview (below this layer) |
| [`vm-abi-reference.md`](vm-abi-reference.md) | VM ABI, frame layout, register model (below this layer) |
| [`docs/CORE.md`](../CORE.md) | Module boundaries and pipeline ownership |
| `Poly/DomainModeling/Runtime/DomainEntityInstance.cs` | Runtime dispatch, effect execution, quantifier preprocessing |
| `Poly/DomainModeling/Lowering/DomainExpressionLoweringPass.cs` | Expression → Syntax AST lowering |
| `Poly/DomainModeling/Lowering/EffectLoweringPass.cs` | Effect → Syntax AST lowering |
| `Poly/DomainModeling/EffectDispatch.cs` | Effect dispatch base class |
| `Poly/DomainModeling/DomainExpressionDispatch.cs` | Expression dispatch base class |
| `Poly/DomainModeling/Lowering/LoweringContext.cs` | Shared lowering context |