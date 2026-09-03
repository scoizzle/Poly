# DomainModeling Abstraction Gaps

**Date:** 2026-07-19  
**Scope:** `Poly/DomainModeling/` — 94 `.cs` files, 17 analyzers, 56 change records, 27 expression types, 12 effect types  
**Source:** Live review of module structure, dispatch sites, and repeated patterns

Seven findings from reviewing the DomainModeling module for missing abstractions that would simplify the system. Ordered by impact.

---

## Finding 1: No visitor abstraction on Effect or DomainExpression (🔴 Structure)

**Problem:** Four independent raw-switch dispatch sites on `Effect`, three on `DomainExpression`. Adding a new expression or effect subtype requires hunting down every site by hand — new subtypes compile silently and fail at runtime with `NotSupportedException` or silent omission.

**Evidence:**

| Operation | Site | Switch Arms |
|-----------|------|-------------|
| Expression lowering | `DomainExpressionLoweringPass.LowerCore` | ~25 |
| Expression printing | `DomainDslPrinter.PrintExpression` | ~20 |
| Expression JSON parsing | `DomainExpressionJsonParser` | ~8 |
| Expression analysis | `PolicyConstraintAnalyzer` (inline) | ~6 |
| Effect lowering | `EffectLoweringPass.TryLowerVmNode` | 4 (null for rest) |
| Effect runtime | `DomainEntityInstance.ExecuteEffect` | 7 |
| Effect printing | `DomainDslPrinter.PrintEffect` | 8 |
| Effect analysis | `EffectAnalyzer` | 12+ effect types via 33 `case` matches |

**Plan (visitor structure, named by the type not the pattern):**

Use a dispatch base per hierarchy. The key rule: **methods are named by the type they handle, not by the pattern they follow.** The concern/verb lives in the class name. When you read `EffectLoweringPass.StageTransition(StageTransitionEffect)`, you know it lowers a stage transition. When you read `EffectPrinter.StageTransition(StageTransitionEffect)`, you know it prints one. No `Visit*` anywhere.

```csharp
// One abstract dispatch base per hierarchy
// Methods named by what they dispatch, not by the pattern
public abstract class EffectDispatch {
    protected abstract Node? StageTransition(StageTransitionEffect e);
    protected abstract Node? Assign(AssignEffect e);
    protected abstract Node? CreateEntity(CreateEntityInstance e);
    // ... one per Effect subtype

    // Single switch — adding a subtype causes a compile error here
    public Node? Route(Effect effect) => effect switch {
        StageTransitionEffect e => StageTransition(e),
        AssignEffect e          => Assign(e),
        CreateEntityInstance e  => CreateEntity(e),
        _ => null  // per-concern fall-through default
    };
}

// Concrete concern: class name IS the verb.
// Inside, methods are named by the subtype they handle.
public sealed class EffectLoweringPass : EffectDispatch {
    protected override Node? StageTransition(StageTransitionEffect e) => null;
    protected override Node? Assign(AssignEffect e) => LowerAssign(e);
    protected override Node? CreateEntity(CreateEntityInstance e) => null;
}

public sealed class DomainDslPrinter {
    private sealed class EffectPrinter : EffectDispatch {
        internal void Print(Effect e) => Route(e);
        protected override Node? StageTransition(StageTransitionEffect e) {
            _sb.AppendLine($"transition to {e.TargetStage.StageName}");
            return null;
        }
        protected override Node? Assign(AssignEffect e) { ... return null; }
    }
}
```

The same shape applies to `DomainExpression`:
- `DomainExpressionLoweringPass` has methods `PropertyAccess`, `OwnedAccess`, `RelationshipNavigation`, etc.
- `DomainDslPrinter` (expression section) has methods `PropertyAccess`, `OwnedAccess`, etc. — the concern "Print" comes from the containing class.
- `DomainExpressionJsonParser` has methods `PropertyAccess`, `Comparison`, etc. — the concern "Parse" comes from the containing class.

**Key rules:**
- **Name by the type, not the pattern.** Methods are `StageTransition(StageTransitionEffect)`, not `VisitStageTransition`. The verb/concern is already in the class name (`EffectLoweringPass` → "lower"), so the method names stay clean.
- **One switch per hierarchy.** The dispatch base owns the single switch. New subtypes cause a compile error in exactly that one file. Every concrete subclass must implement every abstract method (compiler-enforced across all consumers).
- **Per-concern fall-through.** Concerns that don't handle every subtype return a default (null, empty string, false). Concerns that *must* handle everything can enforce this at the base level.

**Lines affected:** ~6 files, ~150 lines of switch code → ~220 lines of dispatch base + subclasses (net +70, type-safe, no `Visit*` names).  
**Risk:** Medium — mechanical refactor, but each switch site has unique null/throw semantics to preserve.  
**Timeline:** 2-3 hours per hierarchy (Expression first, then Effect).  
**Benefit:** Adding a new subtype becomes compiler-enforced — no runtime surprises. Three consumers already exist, more coming (C# emit, MSIL). Method names say *what* (the type), class names say *what for* (the concern).

---

## Finding 2: DomainMutationContext — 9 near-identical Update* methods (🟠 Contract)

**Problem:** `DomainMutationContext` has 11 public methods all doing the same linear-scan → match → `with { }` → replace → `ModifiedNodes.Add` pattern:

| Method | List | Match |
|--------|------|-------|
| `UpdateEntity(name, fn)` | `Types` | `is Entity && Name == name` |
| `UpdateType(name, fn)` | `Types` | `Name == name` |
| `UpdateRelationship(name, fn)` | `Relationships` | `Name == name` |
| `UpdateAction(entityName, actionName, fn)` | `Types` → nested | Entity → action |
| `UpdateStage(entityName, stageName, fn)` | `Types` → nested | Entity → stage |
| `UpdateProperty(entityName, propName, fn)` | `Types` → nested | Entity → property |
| `UpdateRelationshipStage(relName, stageName, fn)` | `Relationships` → nested | Rel → stage |
| `UpdateImportedContract(name, fn)` | `ImportedContracts` | Name |
| `UpdateContractBinding(name, fn)` | `ContractBindings` | Name |
| `AddPolicyToRelationship(name, policy)` | `Relationships` | Name |
| `RemovePolicyFromRelationship(name, policyName)` | `Relationships` | Name |

Every method is 8-15 lines of linear-scan boilerplate with the same structure. Adding a new container type means writing another copy.

**Plan:**

1. **Add generic list helpers to `DomainMutationContext`:**
   ```csharp
   public bool ReplaceInList<T>(List<T> list, Func<T, bool> match, Func<T, T> transform)
       where T : Node
   ```
   ```csharp
   public bool ReplaceInEntity<T>(string entityName, 
       Func<Entity, IEnumerable<T>> getItems,
       Func<Entity, IEnumerable<T>, Entity> rebuild,
       Func<T, bool> match, Func<T, T> transform)
   ```
2. **Rebase each `Update*` method** on top of these helpers — collapse 11 methods into 3.
3. **Inline callers** in `DomainChange.cs` that previously called `context.UpdateEntity(...)` → directly call the helper.

**Lines affected:** `DomainMutationContext.cs` (~80 lines → ~30 lines), `DomainChange.cs` (callers become 2-liners).  
**Net reduction:** ~50 lines of boilerplate removed.  
**Risk:** Low — purely mechanical, test-covered by evolution suite.  
**Timeline:** 1 hour.

---

## Finding 3: 56 ApplyTo implementations — 3 variant patterns repeated (🟡 Edge case)

**Problem:** Each of the 56 `internal override void ApplyTo(DomainMutationContext context)` implementations in `DomainChange.cs` is manually written boilerplate following one of three shapes:

- **Shape A — Direct add:** `context.Types.Add(...)` / `context.ModifiedNodes.Add(...)`
- **Shape B — Remove with guard:** `RemoveAll(...)` + `RequireTarget` on zero
- **Shape C — Update with with-expression:** `RequireUpdate(context.Update*(name, e => e with { ... }), failureMsg)`

**Plan:**

1. **Introduce higher-order static helpers** (or abstract base record templates) that capture the three patterns:
   ```csharp
   // Shape A
   public static DomainChange AddChange<T>(List<T> list, Func<T> create, Action<T>? onAdded = null)
   
   // Shape B  
   public static DomainChange RemoveChange<T>(List<T> list, Func<T, bool> match, string typeLabel, string name)
   
   // Shape C — Entity child mutation
   public static DomainChange UpdateEntityChange(string entityName, Func<Entity, Entity> transform, string failMsg)
   ```
2. **Rebase ~40 of the 56 records** on top of these helpers. The remaining ~16 with non-standard logic (nested lookups, conditional chains) keep their explicit `ApplyTo`.
3. **Collapse the `UpdatePolicy` family** — `AddPolicyToEntityChange`, `RemovePolicyFromStageChange`, etc. — which follow identical patterns differing only in which container they modify.

**Net reduction:** ~30-40% of `DomainChange.cs` line count.  
**Risk:** Low — each change record is independently testable.  
**Timeline:** 2-3 hours.  
**Benefit:** Adding a new change record becomes a 3-line declaration instead of 8-12 lines of ApplyTo.

---

## Finding 4: Two lowering passes with no shared context (🟡 Edge case)

**Problem:**
- `DomainExpressionLoweringPass` holds `_parameters`, recurses via `LowerCore`, **throws** on unhandled nodes.
- `EffectLoweringPass` holds `_entity` + `Subject`, creates its own `DomainExpressionLoweringPass` internally, **returns null** for unhandled effects.
- No shared lowering context, inconsistent error strategy (throw vs null), no single abstraction for "what do we need to lower this?"

**Plan:**

1. **Introduce `LoweringContext`** record:
   ```csharp
   public sealed record LoweringContext(
       Node Subject,
       IReadOnlyDictionary<string, Node> Parameters
   );
   ```
2. **Make both passes accept `LoweringContext`** instead of owning the state independently.
3. **Unify error strategy** — introduce `LowerResult<T>` (lowered node / fall-through / error) used by both passes.
4. **Thread context through** `EffectLoweringPass` to its composed `DomainExpressionLoweringPass` so both see the same subject + parameters.

**Lines affected:** `Lowering/DomainExpressionLoweringPass.cs`, `Lowering/EffectLoweringPass.cs`.  
**Risk:** Low — both passes are test-covered (policy eval, effect lowering smoke).  
**Timeline:** 1 hour.  
**Benefit:** Consistent error handling, shared subject/params, foundation for a unified lowering pipeline.

---

## Finding 5: 25 one-liner factory methods on DomainExpression (⚪ Hygiene)

**Problem:** `DomainExpression.cs` has 25 `public static DomainExpression MethodName(...) => new ConcreteRecord(...)` — every method is an exact passthrough to the constructor with no additional logic, validation, or caching:

```csharp
public static DomainExpression Property(string name) => new PropertyAccess(name);
public static DomainExpression And(DomainExpression l, DomainExpression r) => new And(l, r);
// ... 23 more identical
```

**Plan:**

1. **Delete all 25 factory methods.**
2. **Update all callers** to use `new` constructor syntax directly.
3. Alternatively, if the factories exist for a specific API-surface reason, **generate them via source generator** (one-time cost, then zero maintenance).

**Lines affected:** ~25 lines removed from `DomainExpression.cs` + ~50 caller updates across the codebase.  
**Risk:** Low — mechanical one-to-one replacement.  
**Timeline:** 30 minutes.  
**Benefit:** Removes a maintenance surface that must stay in sync with record constructor changes. Eliminates "why doesn't `DomainExpression.Foo(...)` exist?" confusion (just use `new Foo(...)`).

---

## Finding 6: 17 analyzers with no shared iteration base (🟡 Edge case)

**Problem:** At least 10 of 17 `INodeAnalyzer` implementations follow an identical template:

```csharp
public void Analyze(AnalysisContext context, Node node) {
    if (!context.ShouldAnalyze(node)) return;
    if (node is Domain domain) { ValidateDomain(context, domain); return; }
    this.AnalyzeChildren(context, node);
}

private static void ValidateDomain(AnalysisContext context, Domain domain) {
    context.TryBeginAnalyzerVisit<XxxAnalyzer>(domain);
    foreach (var t in domain.Types) {
        if (t is Entity e) {
            foreach (var action in e.Actions) { ... }
            foreach (var stage in e.Stages) { ... }
        }
    }
}
```

The entity → action/stage iteration loop is handwritten in 10+ files.

**Plan (two options, pick one):**

**Option A — Base class:** Introduce `DomainAnalyzerBase : INodeAnalyzer` providing:
```csharp
public abstract class DomainAnalyzerBase : INodeAnalyzer {
    public abstract string PassName { get; }
    public virtual string[] Dependencies => [];
    public void Analyze(AnalysisContext context, Node node) { /* common guard + dispatch */ }
    protected abstract void AnalyzeDomain(AnalysisContext ctx, Domain domain);
    protected virtual void AnalyzeEntity(AnalysisContext ctx, Entity entity) { }
    protected virtual void AnalyzeAction(AnalysisContext ctx, Action action) { }
    protected virtual void AnalyzeStage(AnalysisContext ctx, Stage stage) { }
    // default iteration visits all entities/actions/stages
}
```

**Option B — Static helpers:** Add `static class DomainAnalysis` helpers:
```csharp
public static void ForEachEntity(Domain domain, Action<Entity> action);
public static void ForEachAction(Entity entity, Action<Action> action);
// etc.
```

Plumb them through existing analyzers.

**Lines affected:** 10+ analyzer files.  
**Risk:** Low — analyzers are independently tested.  
**Timeline:** 1-2 hours per option.  
**Benefit:** Reduces per-analyzer boilerplate by ~30%. Adding a new analyzer becomes a focused `AnalyzeDomain` method instead of a structural copy-paste.

---

## Finding 7: InstanceStore O(n) link lookups (⚪ Hygiene)

**Problem:** `DomainInstanceStore._links` is a flat `List<(string, Source, Target)>`. Both `GetRelatedInstances` and `NotifyTransition` do full linear scans. No index by relationship name.

**Plan:**

1. **Add a `Dictionary<string, List<(Source, Target)>>` index** in parallel with the flat list.
2. **Update `Link()`/`Unlink()`** to maintain both data structures.
3. **Rewrite `GetRelatedInstances`** to O(1) lookup from the dictionary.
4. **Rewrite `NotifyTransition`** to O(m) where m = relationships matching the transitioned entity.

**Lines affected:** `DomainInstanceStore.cs` (~250 lines → ~280 lines).  
**Risk:** Low — test-covered by link/subscription tests.  
**Timeline:** 1 hour.  
**Defer condition:** Only do this if link lookups show up in profiling. O(n) is fine for dogfood scale.

---

## Priority summary

| Prio | # | Finding | Sev | Effort | Risk | Why now |
|------|---|---------|-----|--------|------|---------|
| **P0** | 1 | Visitor for Effect/DomainExpression | 🔴 | Medium | Medium | Prevents bugs; 3 consumers now, more coming |
| **P1** | 2 | MutationContext generic helpers | 🟠 | Small | Low | Biggest boilerplate reduction per line changed |
| **P2** | 3 | ApplyTo pattern variants → helpers | 🟡 | Medium | Low | Reduces 56 change records by ~40% |
| **P3** | 4 | Shared lowering context | 🟡 | Small | Low | Foundation for unified pipeline |
| **P4** | 6 | Analyzer iteration base | 🟡 | Medium | Low | ~30% less boilerplate per analyzer |
| **P5** | 5 | Factory method deletion/generation | ⚪ | Trivial | Low | Maintenance surface with no value |
| **Defer** | 7 | InstanceStore index | ⚪ | Small | Low | Only if profiling shows need |

---

## Out of scope (for this plan)

- Changing the sealed-record hierarchies themselves (Effects, Expressions) — that's the domain model, not an abstraction gap
- Adding new lowering/analysis passes — that's product work
- V2 code removal — covered by `anti-pattern-005-second-system-effect.md`
