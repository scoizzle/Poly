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
| How to execute store-aware quantifiers | **Lowering** to Notify-shaped Store reads (`ExistsRelated` / `AnyRelated` / `GetRelatedOne`); the VM invokes those methods |
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

When a domain concept cannot be expressed in the current Syntax AST (e.g. a collaborator the dictionary `This` cannot Member-read), bind a Notify-shaped instance method and lower to `Invoke(Member(This, job), …)` — same as `EnsureUnique` / `Create` / `ExistsRelated`. Unique assign and create / create-in bind Store that way. Do not emit `Comment` as shipped meaning (§2d).

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
│  │              Syntax AST (Poly.Ast.Nodes)                 │    │
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
| `EffectLoweringPass` | `Effect` tree | `Syntax.Node` (never `null` for a shipped effect) | `EffectDispatch<Node?>` |

Both consume a `LoweringContext` that carries the current-instance `Subject` and optional parameter map.

---

## 2. One tree: session.Lower → Interpreter

All shipped effects **lower to Syntax AST on both runtime and emit**. `ExecuteStructured` / `EffectExecutor` / `LowerStageTransitions` / `PreprocessRuntimeKeyword` are gone.

`session.Lower` / `RuntimeAnalysisCache.GetOrLower` builds the module. Named invoke runs the entity method `Body` (same node `session.Emit` prints), rebound for dictionary `This`. Subscriptions and transition batches still call `LowerActionBody` at execute time. `EvaluatePolicy` still lowers the guard expression per call.

Create / create-in / unique on the **operation tree** are `this.Create` / `CreateIn` / `ProbeCreate` / `EnsureUnique` (flattened name/value pairs). C# `Stay.Create` / `CreateNav` are the **host bind** of those jobs inside generated factories — not a second action body.

### 2a. VM-Compiled Path (Lowering → Compile → Execute)

Every action effect list maps to Syntax AST, compiles via `Interpreter.CompileChecked`, and executes against the instance bag with Store bound.

| Effect | Lowered to | Notes |
|--------|-----------|-------|
| `AssignEffect` | `Assignment(target, value)` | Unique properties wrap `EnsureUnique` then assign |
| `CompositeEffect` | `Block(nodes)` | Sub-effects lowered recursively. A sub-effect that cannot lower throws (no silent drop) |
| `ConditionalEffect` | `IfStatement(cond, thenBlock, elseBlock?)` | Mixed if+create is one tree |
| `StageTransitionEffect` | Assignment of `CurrentStage` + `Invoke(Member(This, "Notify"), stageName)` | Same tree on runtime and emit |
| `InvokeActionEffect` | Self: `Invoke(Member(This, action), args)`. Cross-entity: `this.Rel.Action(args)` with a linked-target `DomainResult.Failure` guard | Same tree on runtime and emit |
| `ForEachInvokeEffect` | Fail-fast `ForEachLoop` over a **OneToMany** collection nav | Zero-match `DomainResult.Failure` |
| `CreateEntityInstance` | `this.Create(typeName, prop, value, …)` | Notify-shaped Store job; C# factory may call `Stay.Create` as host bind |
| `CreateEntityInRelationship` | `this.CreateIn(relName, prop, value, …)` | Store.CreateIn registers and links; C# factory may call `CreateNav` as host bind |

```csharp
RuntimeAnalysisCache.GetOrLower(domain, session, analysis);
var tree = RuntimeAnalysisCache.TryGetOperation(domain, key, out var cached)
    ? cached
    : effectPass.LowerActionBody(effects); // subscriptions / transition batches
var compiled = Interpreter.CompileChecked(tree, typeProvider);
using var exec = Interpreter.Execute(compiled, s => s.SetArgs(new object?[] { this }));
if (exec.Result.Value is DomainResult { IsSuccess: false } failed)
    return failed;
```

### 2b. Direct-Execution Path (deleted)

No Effect-IR walk at simulate. `CreateChildInstance` exists only as the body of `Store.Create` / instance `Create` when no Store is bound.

> **Removed 2026-08-10:** `DeleteEntityInstance`, `LinkRelationshipEffect`, `UnlinkRelationshipEffect`,
> and `TransitionRelationshipEffect`. Linking existing instances is `DomainInstanceStore.Link` /
> `Unlink` (MCP `link_instances` / `unlink_instances`).
>
> **Removed 2026-09-03:** `ExecuteStructured`, `RequiresDirectExecution`, `HasEffectDependentConditionalCreate`,
> `CreateByType` / `CreateInNav` / `ProbeCreateByType` as shipped factories.

### 2c. Dispatch Decision Tree

```
ExecuteEffectList(effects)
  │
  ├─ named action / OnEntry: GetOrLower → TryGetOperation (runtime-shaped tree)
  ├─ subscription / transition batch: LowerActionBody
  │
  └─ Interpreter.CompileChecked → Execute
      ├─ AssignEffect  → Assignment (+ EnsureUnique)
      ├─ CompositeEffect → Block
      ├─ ConditionalEffect → IfStatement
      ├─ StageTransitionEffect → CurrentStage Assignment + Invoke Notify
      ├─ InvokeActionEffect → Invoke(Member(...))
      ├─ ForEachInvokeEffect → ForEachLoop
      ├─ CreateEntityInstance → this.Create(name, prop, value, …)
      └─ CreateEntityInRelationship → this.CreateIn(rel, prop, value, …)
```

### 2d. Comment is not shipped meaning

`EffectLoweringPass` does **not** emit `Comment` nodes. A composite/conditional sub-effect that cannot lower throws `InvalidOperationException`. All shipped effects lower on both runtime and emit paths. The `Comment` AST node is not product meaning and must not be used as a lowering-gap marker. Sequential transitions update `SourceStageName` after each transition so exit effects use the correct source stage. Clocks (`now`/`today`/`guid`) lower to static BCL members the VM executes — they are not rewritten to host literals.

---

## 3. Policy Evaluation

Policies are boolean guard expressions attached to entities, stages, or actions. Evaluation is **lower → compile → execute**. Store-aware reads (`Rel exists`, quantifiers, path-prefix) stay in the tree as Notify-shaped Store jobs.

### 3a. Full Path

```
Policy.Expression (DomainExpression)
  │
  ├─ DomainExpressionLoweringPass.Lower(expr, entityParam)
  │   Rel exists → ExistsRelated; any/all/none/count → AnyRelated/…;
  │   to-one path-prefix → GetRelatedOne + TypeCast to the target entity.
  │   Action-parameter roots stay bag Member reads.
  │
  ├─ Interpreter.CompileChecked(lowered, typeDefAnalyzer)
  │
  ├─ Interpreter.Execute(compiled, args: this)
  │
  └─ coerce bool / long / null
```

```csharp
public bool EvaluatePolicy(Policy policy) {
    var pass = new DomainExpressionLoweringPass(new LoweringContext(
        entityParam, Analysis: analysis, Domain: Domain,
        IsRelationshipNavigation: …, SourceEntityName: Entity.Name));
    var lowered = pass.Lower(policy.Expression, entityParam);
    var compiled = Interpreter.CompileChecked(lowered, _typeDefAnalyzer);
    using var exec = Interpreter.Execute(compiled, s => s.SetArgs(new object?[] { this }));
    …
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

## 4. Store reads in the tree

Q3′ quantifiers (`any`, `all`, `none`, `count`), `Rel exists`, and to-one path-prefix lower to Notify-shaped methods on `This`. Dictionary `This` cannot Member-read `Store`. The VM invokes those methods; the implementation walks outbound links.

Execute-time `PreprocessQuantifiers` → literals is gone. A rewrite to literals is wrong when the same action creates then queries.

### 4a. Lowered jobs

| Domain | Runtime tree | Notes |
|--------|--------------|-------|
| `Rel exists` | `ExistsRelated(relName)` | False when no links; unknown rel throws |
| `any` / `all` / `none` | `AnyRelated` / `AllRelated` / `NoneRelated` | Predicate is a `Constant` of the domain expression; `all` on empty is false |
| `count` | `CountRelated` | Bare count or filtered |
| to-one path-prefix | `GetRelatedOne(relName)` then TypeCast to the target entity | Zero or many links fail closed |
| action-parameter root (`sku ListPrice`) | `Member(param, leaf)` | Not a store hop |

C# export may still throw Q3 except bare `count Rel`, and may keep coalesce-throw for path-prefix — persistence print until an EF Store exists.

### 4b. Job bodies

`AnyRelated` / `AllRelated` / `CountRelated` still iterate `GetOutboundRelatedInstances` and evaluate the predicate on each target through Interpreter. That is the **implementation of the Store job**, not a preprocess that replaces the tree with a literal.

## 5. Cross-Entity and For-Invoke Flow

`InvokeActionEffect` and `ForEachInvokeEffect` lower to Syntax AST on both runtime and emit. There is no `ExecuteInvokeEffect`. Runtime `This` has no CLR method for domain actions, so `InvokeNamed` runs the action (Notify still hits the real CLR method first).

| Case | Lowered to | Behavior |
|------|------------|----------|
| Self-invoke | `Invoke(Member(This, action), args)` | Analysis sees the action on the type def; C# prints `this.Checkout()`. Nested Failure is discarded like C# `this.Foo();` |
| Singular cross-entity | `this.Rel.Action(args)` with a linked-target `DomainResult.Failure` guard before deref | Same tree on runtime and emit. Does not wrap `IsSuccess` |
| For-invoke | Fail-fast `ForEachLoop` over a **OneToMany** collection nav | Analysis rejects ManyToMany / OneToOne. VM walks `IList` (fail-loud non-IList). Per-item `if (!result.IsSuccess) return result`. Zero-match `DomainResult.Failure`; `ExecuteEffect` throws on a failed program result |

`GetOutboundRelatedInstances` is the body of those Store-read jobs — not invoke dispatch, and not an execute-time rewrite of the action/policy tree.

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
| Effect → Syntax AST | `EffectLoweringPass` | `Node?` | Inherits `EffectDispatch<Node?>`; shipped effects lower to a real node |
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
| **Quantifier lowering** | Runtime: Store jobs in the tree. C# export still limited (Q3 except bare `count Rel`) | EF Store so export can print the same jobs |
| **Relationship navigation** | Runtime: `GetRelatedOne` + TypeCast. C#: coalesce-throw | Same Store job on both paths |
| **Effect lowering** | One operation tree (`this.Create` / `CreateIn` / `EnsureUnique`). Named invoke looks up `session.Lower` cache. Sequential transitions update `SourceStageName`. | Bind an EF Store so C# factories do not wrap `Stay.Create` / `CreateNav` |
| **VM quantifier eval** | Per-target re-lowering + compile inside the Store job | Cached lowering or batch evaluation |
| **ParameterAccess in DSL** | Product spelling is a **bare identifier** (`PropertyAccess`) | L3 — no separate parameter authoring syntax |
| **Clock keywords** | Clocks lower to BCL members (`DateTime.UtcNow`, `DateOnly.FromDateTime`, `Guid.NewGuid`); VM executes them | Injectable `TimeProvider` (not a product seam) |

## 10. Related Documents

| Document | Purpose |
|----------|---------|
| [`Poly/Interpretation/README.md`](../../Poly/Interpretation/README.md) | Interpretation system overview (below this layer) |
| [`vm-abi-reference.md`](vm-abi-reference.md) | VM ABI, frame layout, register model (below this layer) |
| [`docs/CORE.md`](../CORE.md) | Module boundaries and pipeline ownership |
| `Poly/DomainModeling/Runtime/DomainEntityInstance.cs` | Runtime dispatch; Store jobs; named invoke looks up `session.Lower` |
| `Poly/DomainModeling/Lowering/DomainExpressionLoweringPass.cs` | Expression → Syntax AST lowering |
| `Poly/DomainModeling/Lowering/EffectLoweringPass.cs` | Effect → Syntax AST lowering |
| `Poly/DomainModeling/EffectDispatch.cs` | Effect dispatch base class |
| `Poly/DomainModeling/DomainExpressionDispatch.cs` | Expression dispatch base class |
| `Poly/DomainModeling/Lowering/LoweringContext.cs` | Shared lowering context |