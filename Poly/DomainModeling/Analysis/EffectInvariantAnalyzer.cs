using Poly.DomainModeling.Ontology;

using Action = Poly.DomainModeling.Ontology.Action;
using Add = Poly.DomainModeling.Ontology.Add;
using And = Poly.DomainModeling.Ontology.And;
using Multiply = Poly.DomainModeling.Ontology.Multiply;
using Subtract = Poly.DomainModeling.Ontology.Subtract;

namespace Poly.DomainModeling.Analysis;

/// <summary>A numeric value range (inclusive bounds, null = unbounded).</summary>
public sealed record ValueRange(double? Min, double? Max);

/// <summary>
/// A postcondition an effect must respect: assigning to <see cref="TargetProperty"/>
/// must satisfy <see cref="Constraints"/>; <see cref="Value"/> is the abstract value
/// the effect's expression can produce in this stage context. <see cref="ValueRange"/>
/// is that value's numeric envelope, when statically known. <see cref="DeclaringAction"/>
/// is the action whose body contains the effect — it differs from the context action for
/// call-chain postconditions.
/// </summary>
public sealed record EffectPostcondition(
    Node Effect,
    string TargetProperty,
    IReadOnlyList<Constraint> Constraints,
    AbstractValue Value,
    Action DeclaringAction) {
    /// <summary>The value's numeric range, when the value carries one.</summary>
    public ValueRange? ValueRange => Value.NumericRange;
}

/// <summary>The additive invariant picture for an action in ONE stage context.</summary>
public sealed record ActionStageInvariant(
    string? StageName,
    IReadOnlyList<Policy> Preconditions,
    IReadOnlyDictionary<string, ValueRange?> NarrowedRanges,
    IReadOnlyDictionary<string, IReadOnlyList<Constraint>> MergedConstraints,
    IReadOnlyList<EffectPostcondition> Postconditions);

/// <summary>Per-action invariant metadata: one <see cref="ActionStageInvariant"/> per stage
/// the action is valid in.</summary>
public sealed record ActionInvariantMetadata(
    IReadOnlyList<ActionStageInvariant> StageContexts) : IAnalysisMetadata;

/// <summary>
/// Publishes <see cref="ActionInvariantMetadata"/> per action using the unified abstract
/// interpretation model: an <see cref="AbstractValue"/> per symbol, refined by preconditions
/// (guards, if-conditions, callers, where-filters), composed through expressions via
/// <see cref="Eval"/>, and stepped through effects via <see cref="ApplyEffects"/>.
/// </summary>
internal sealed class EffectInvariantAnalyzer : INodeAnalyzer {
    public const string Id = "DomainEffectInvariant";
    public string PassName => Id;

    public void Analyze(AnalysisContext context, Node node) {

        if (node is Domain domain) {
            foreach (var type in domain.Types) {
                if (type is Entity entity) {
                    foreach (var action in entity.Actions)
                        Publish(context, action, entity, domain);
                    foreach (var stage in entity.Stages)
                        foreach (var action in stage.Actions)
                            Publish(context, action, entity, domain);
                }
            }
            return;
        }

        this.AnalyzeChildren(context, node);
    }

    private static void Publish(AnalysisContext context, Action action, Entity entity, Domain domain) {
        var contexts = new List<ActionStageInvariant>();
        foreach (var (stageName, stage) in ValidStages(action, entity)) {
            var preconditions = CollectPreconditions(action, entity, stage);
            var conflicts = new List<RelationConflict>();
            var env = BuildInitialEnv(entity, preconditions, conflicts);
            var scalarEnv = conflicts.Count == 0 ? env : BuildInitialEnv(entity, preconditions, relations: false);
            ReportUnsatisfiablePreconditions(context, action, stageName, env, scalarEnv, conflicts);
            var narrowed = env.ToDictionary(kv => kv.Key, kv => kv.Value.NumericRange, StringComparer.Ordinal);
            var merged = env.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<Constraint>)kv.Value.Constraints, StringComparer.Ordinal);
            var postconditions = new List<EffectPostcondition>();
            // Call-stack scoped: an action is in the set only while its walk is on the stack.
            var visited = new HashSet<Action>(ReferenceEqualityComparer.Instance) { action };
            ApplyEffects(action.Effects, entity, action, env, paramEnv: null, visited, postconditions, domain, stageName);
            contexts.Add(new ActionStageInvariant(stageName, preconditions, narrowed, merged, postconditions));
        }
        context.SetMetadata(action, new ActionInvariantMetadata(contexts));
    }

    /// <summary>The stages the action is valid in: its own stage if stage-scoped, else every
    /// stage (or a single null-stage context for a no-stage entity).</summary>
    private static List<(string? StageName, Stage? Stage)> ValidStages(Action action, Entity entity) {
        foreach (var stage in entity.Stages)
            if (stage.Actions.Contains(action))
                return [(stage.Name, stage)];
        if (entity.Stages.Count == 0)
            return [(null, null)];
        return entity.Stages.Select(s => ((string?)s.Name, (Stage?)s)).ToList();
    }

    // ── Preconditions ───────────────────────────────────────────

    internal static IReadOnlyList<Policy> CollectPreconditions(Action action, Entity entity, Stage? stage) {
        return action.Policies
            .Where(p => !p.Name.StartsWith("not_", StringComparison.Ordinal))
            .Concat(stage?.Policies ?? [])
            .ToList();
    }

    // ── Abstract environment ────────────────────────────────────

    /// <summary>The initial abstract environment: each property's declared constraints as an
    /// <see cref="AbstractValue"/>, refined by every precondition's bounds.</summary>
    private static Dictionary<string, AbstractValue> BuildInitialEnv(
        Entity entity, IReadOnlyList<Policy> preconditions,
        List<RelationConflict>? conflicts = null, bool relations = true) {
        var env = new Dictionary<string, AbstractValue>(StringComparer.Ordinal);
        foreach (var prop in entity.Properties)
            env[prop.Name] = AbstractValue.From(prop.Constraints);
        foreach (var policy in preconditions)
            Refine(env, policy.Expression, conflicts, relations);
        return env;
    }

    /// <summary>A relation (<c>Start &lt; End</c>) whose refinement is what made two previously
    /// satisfiable properties unsatisfiable: one cause, reported once (#367 B-7).</summary>
    private readonly record struct RelationConflict(string Left, string Right, RelationalOrdering Orderings);

    /// <summary>Narrows the symbols an environment mentions by a boolean condition's bounds
    /// (a guard, an if-branch, or a where-filter). Single-primitive refinement.</summary>
    private static void Refine(
        Dictionary<string, AbstractValue> env, DomainExpression condition,
        List<RelationConflict>? conflicts = null, bool relations = true) {
        foreach (var conjunct in FlattenAnds(condition)) {
            if (conjunct is not Comparison cmp) continue;
            if (TryGetRelation(cmp, out var left, out var right, out var orderings)) {
                if (relations) RefineRelation(env, left, right, orderings, conflicts);
                continue;
            }
            if (!TryGetComparisonBound(cmp, out var propName, out double value, out var bound)) continue;
            if (!env.TryGetValue(propName, out var current)) continue;
            switch (bound) {
                case ComparisonKind.LessThan or ComparisonKind.LessThanOrEqual:
                    env[propName] = current.Narrow(new RangeConstraint(null, value));
                    break;
                case ComparisonKind.GreaterThan or ComparisonKind.GreaterThanOrEqual:
                    env[propName] = current.Narrow(new RangeConstraint(value, null));
                    break;
                case ComparisonKind.Equal:
                    env[propName] = current.Narrow(new RangeConstraint(value, value));
                    break;
            }
        }
    }

    /// <summary>A comparison between two properties (<c>Start &lt; End</c>) as the orderings the left
    /// property may have against the right one (#367 A-6).</summary>
    private static bool TryGetRelation(
        Comparison cmp, out string left, out string right, out RelationalOrdering orderings) {
        left = right = "";
        orderings = cmp.Kind switch {
            ComparisonKind.LessThan => RelationalOrdering.Less,
            ComparisonKind.LessThanOrEqual => RelationalOrdering.Less | RelationalOrdering.Equal,
            ComparisonKind.GreaterThan => RelationalOrdering.Greater,
            ComparisonKind.GreaterThanOrEqual => RelationalOrdering.Greater | RelationalOrdering.Equal,
            ComparisonKind.Equal => RelationalOrdering.Equal,
            ComparisonKind.NotEqual => RelationalOrdering.Less | RelationalOrdering.Greater,
            _ => RelationalOrdering.None,
        };
        if (orderings == RelationalOrdering.None
            || cmp.Left is not PropertyAccess l || cmp.Right is not PropertyAccess r
            || string.Equals(l.Name, r.Name, StringComparison.Ordinal))
            return false;
        left = l.Name;
        right = r.Name;
        return true;
    }

    /// <summary>Records the relation on both properties (each seen from its own side), then lets the two
    /// properties' ranges decide it: a relation no value pair can satisfy makes the symbols
    /// unsatisfiable. An undecidable relation is only recorded, never assumed true or false.</summary>
    private static void RefineRelation(
        Dictionary<string, AbstractValue> env, string left, string right, RelationalOrdering orderings,
        List<RelationConflict>? conflicts = null) {
        if (!env.TryGetValue(left, out var l) || !env.TryGetValue(right, out var r)) return;
        var wasBottom = l.Unsatisfiable || r.Unsatisfiable;
        var relation = new RelationalConstraint(right, orderings);
        l = l.Narrow(relation);
        r = r.Narrow(relation.MirroredFrom(left));
        if (!l.Unsatisfiable && !r.Unsatisfiable
            && ConstraintValidation.DecideRelation(relation, l.Range, r.Range) == ConstraintVerdict.Violated) {
            l = l.AsUnsatisfiable();
            r = r.AsUnsatisfiable();
        }
        if (!wasBottom && (l.Unsatisfiable || r.Unsatisfiable))
            conflicts?.Add(new RelationConflict(left, right, orderings));
        env[left] = l;
        env[right] = r;
    }

    private static void RefineNegated(Dictionary<string, AbstractValue> env, DomainExpression condition) {
        // A property-vs-property comparison negates exactly: the ordering is trichotomous
        // (Less | Equal | Greater), so the negation is the complement of the positive orderings.
        if (condition is Comparison rel
            && TryGetRelation(rel, out var left, out var right, out var orderings)) {
            RefineRelation(env, left, right, RelationalOrdering.All & ~orderings);
            return;
        }
        if (condition is Comparison cmp
            && TryGetComparisonBound(cmp, out var propName, out double value, out var bound)
            && NegateBound(bound) is { } negated
            && env.TryGetValue(propName, out var current)) {
            switch (negated) {
                case ComparisonKind.LessThan or ComparisonKind.LessThanOrEqual:
                    env[propName] = current.Narrow(new RangeConstraint(null, value));
                    break;
                case ComparisonKind.GreaterThan or ComparisonKind.GreaterThanOrEqual:
                    env[propName] = current.Narrow(new RangeConstraint(value, null));
                    break;
            }
        }
    }

    private static IEnumerable<DomainExpression> FlattenAnds(DomainExpression expr) {
        if (expr is And and) {
            foreach (var sub in FlattenAnds(and.Left)) yield return sub;
            foreach (var sub in FlattenAnds(and.Right)) yield return sub;
        }
        else {
            yield return expr;
        }
    }

    private static bool TryGetComparisonBound(
        Comparison cmp, out string propName, out double value, out ComparisonKind bound) {
        propName = "";
        value = 0;
        bound = default;
        if (cmp.Left is PropertyAccess pa && cmp.Right is Literal { Value: not null } lit) {
            propName = pa.Name;
            return NumericBound.ToDoubleOrNull(lit.Value) is double d && SetBound(cmp.Kind, d, out value, out bound);
        }
        if (cmp.Left is Literal { Value: not null } lit2 && cmp.Right is PropertyAccess pa2) {
            propName = pa2.Name;
            return NumericBound.ToDoubleOrNull(lit2.Value) is double d2 && SetBound(Flip(cmp.Kind), d2, out value, out bound);
        }
        return false;
    }

    private static ComparisonKind Flip(ComparisonKind kind) => kind switch {
        ComparisonKind.LessThan => ComparisonKind.GreaterThan,
        ComparisonKind.LessThanOrEqual => ComparisonKind.GreaterThanOrEqual,
        ComparisonKind.GreaterThan => ComparisonKind.LessThan,
        ComparisonKind.GreaterThanOrEqual => ComparisonKind.LessThanOrEqual,
        _ => kind
    };

    private static ComparisonKind? NegateBound(ComparisonKind kind) => kind switch {
        ComparisonKind.LessThan => ComparisonKind.GreaterThanOrEqual,
        ComparisonKind.LessThanOrEqual => ComparisonKind.GreaterThan,
        ComparisonKind.GreaterThan => ComparisonKind.LessThanOrEqual,
        ComparisonKind.GreaterThanOrEqual => ComparisonKind.LessThan,
        _ => null,
    };

    private static bool SetBound(ComparisonKind kind, double v, out double value, out ComparisonKind bound) {
        switch (kind) {
            case ComparisonKind.LessThan or ComparisonKind.LessThanOrEqual
                or ComparisonKind.GreaterThan or ComparisonKind.GreaterThanOrEqual
                or ComparisonKind.Equal:
                value = v;
                bound = kind;
                return true;
            default:
                value = v;
                bound = kind;
                return false;
        }
    }

    // ── Expression evaluation (abstract interpretation) ─────────

    /// <summary>Computes the abstract value of an expression under an environment.</summary>
    internal static AbstractValue Eval(
        DomainExpression expr, Entity entity, Action? action,
        IReadOnlyDictionary<string, AbstractValue> env,
        IReadOnlyDictionary<string, AbstractValue>? paramEnv = null,
        Entity? targetEntity = null,
        Domain? domain = null) {
        switch (expr) {
            case Literal { Value: not null } lit:
                return NumericBound.ToDoubleOrNull(lit.Value) is double d
                    ? AbstractValue.From([new RangeConstraint(d, d)])
                    : AbstractValue.From(lit.Value is string s ? [new EqualityConstraint(s)] : []);
            case PropertyAccess pa:
                if (paramEnv is not null && paramEnv.TryGetValue(pa.Name, out var fromParam))
                    return fromParam.WithoutRelations();
                if (env.TryGetValue(pa.Name, out var pv)) return pv.WithoutRelations();
                // Cross-entity read: the property belongs to the target entity.
                var owner = targetEntity ?? entity;
                var prop = owner.Properties.FirstOrDefault(p =>
                    string.Equals(p.Name, pa.Name, StringComparison.Ordinal));
                return prop is null ? AbstractValue.Unknown : AbstractValue.From(prop.Constraints);
            case ParameterAccess pa2:
                if (paramEnv is not null && paramEnv.TryGetValue(pa2.Name, out var bv)) return bv.WithoutRelations();
                var param = action?.Parameters.FirstOrDefault(p =>
                    string.Equals(p.Name, pa2.Name, StringComparison.Ordinal));
                return param is null ? AbstractValue.Unknown : AbstractValue.From(param.Constraints);
            case RelationshipNavigation rn:
                // The leaf belongs to the RELATED entity, never to the caller. It is evaluated against that
                // entity's DECLARED constraints (no flow state is tracked per related entity). If the
                // relationship does not resolve to exactly one single target entity the result is Unknown.
                return ResolveSingleTarget(rn.RelationshipName, targetEntity ?? entity, domain) is { } related
                    ? Eval(rn.TargetProperty, related, null, EmptyEnv, null, null, domain)
                    : AbstractValue.Unknown;
            case Add add:
                return ComposeArithmetic(Eval(add.Left, entity, action, env, paramEnv, targetEntity, domain),
                    Eval(add.Right, entity, action, env, paramEnv, targetEntity, domain), ArithOp.Add);
            case Subtract sub:
                return ComposeArithmetic(Eval(sub.Left, entity, action, env, paramEnv, targetEntity, domain),
                    Eval(sub.Right, entity, action, env, paramEnv, targetEntity, domain), ArithOp.Sub);
            case Multiply mul:
                return ComposeArithmetic(Eval(mul.Left, entity, action, env, paramEnv, targetEntity, domain),
                    Eval(mul.Right, entity, action, env, paramEnv, targetEntity, domain), ArithOp.Mul);
            default:
                return AbstractValue.Unknown;
        }
    }

    private static readonly IReadOnlyDictionary<string, AbstractValue> EmptyEnv =
        new Dictionary<string, AbstractValue>(StringComparer.Ordinal);

    /// <summary>The single entity a to-one relationship of <paramref name="source"/> points at, or null when
    /// there is no domain, the relationship is unknown or declared more than once, it is to-many, or the target
    /// type is not exactly one entity. Null means the caller must answer Unknown.</summary>
    private static Entity? ResolveSingleTarget(string relationshipName, Entity source, Domain? domain) {
        if (domain is null) return null;
        var matches = source.Navigations
            .Where(r => string.Equals(r.Name, relationshipName, StringComparison.Ordinal)).Take(2).ToList();
        if (matches.Count != 1) return null;
        if (matches[0].Cardinality is not (RelationshipCardinality.OneToOne or RelationshipCardinality.ManyToOne))
            return null;
        var targets = domain.Types.OfType<Entity>()
            .Where(e => string.Equals(e.Name, matches[0].Target.TypeName, StringComparison.Ordinal)).Take(2).ToList();
        return targets.Count == 1 ? targets[0] : null;
    }

    /// <summary>The arithmetic an abstract value composes over. <c>*</c> carries no open endpoint yet:
    /// 0 · ±∞ needs V-05's exact number (OQ-6), so it stays fail-open (Unknown) rather than guess.</summary>
    private enum ArithOp { Add, Sub, Mul }

    /// <summary>
    /// Composes two abstract values, one rule per result endpoint, each depending only on its own two
    /// operand endpoints (#475): add takes like sides, subtract flips, and an absent operand endpoint
    /// makes that result endpoint absent. An absent side is unbounded, never 0. Both result endpoints
    /// absent means no numeric range at all (Unknown), never a <c>range(−∞, +∞)</c> constraint.
    /// </summary>
    private static AbstractValue ComposeArithmetic(AbstractValue left, AbstractValue right, ArithOp op) {
        if (left.NumericRange is not { } lr || right.NumericRange is not { } rr) return AbstractValue.Unknown;
        double? min, max;
        switch (op) {
            case ArithOp.Add:
                min = lr.Min + rr.Min;
                max = lr.Max + rr.Max;
                break;
            case ArithOp.Sub:
                min = lr.Min - rr.Max;
                max = lr.Max - rr.Min;
                break;
            default: {
                    if (lr.Min is not { } lMin || lr.Max is not { } lMax
                        || rr.Min is not { } rMin || rr.Max is not { } rMax)
                        return AbstractValue.Unknown;
                    double[] combos = [lMin * rMin, lMin * rMax, lMax * rMin, lMax * rMax];
                    min = combos.Min();
                    max = combos.Max();
                    break;
                }
        }
        if (min is null && max is null) return AbstractValue.Unknown;
        return AbstractValue.From([new RangeConstraint(min, max)]);
    }

    // ── Effect stepping ─────────────────────────────────────────

    private static void ApplyEffects(
        IReadOnlyList<Effect> effects, Entity entity, Action action,
        Dictionary<string, AbstractValue> env,
        IReadOnlyDictionary<string, AbstractValue>? paramEnv,
        HashSet<Action> visited, List<EffectPostcondition> result, Domain domain,
        string? currentStage) {
        foreach (var effect in effects) {
            switch (effect) {
                case AssignEffect { Target: PropertyAccess target } assign: {
                        var prop = entity.Properties.FirstOrDefault(p =>
                            string.Equals(p.Name, target.Name, StringComparison.Ordinal));
                        if (prop is null) break;
                        var value = Eval(assign.Value, entity, action, env, paramEnv, domain: domain);
                        // Strong update: the assigned value replaces the old one, so it is intersected with the
                        // property's DECLARED constraints, not the (possibly branch-refined) incoming value.
                        env[target.Name] = AbstractValue.From(prop.Constraints).Merge(value);
                        // Relations to the reassigned property are stale on every other symbol.
                        foreach (var key in env.Keys.ToList())
                            if (!string.Equals(key, target.Name, StringComparison.Ordinal))
                                env[key] = env[key].WithoutRelationsTo(target.Name);
                        result.Add(new EffectPostcondition(
                            assign, prop.Name,
                            BuildPostconditionConstraints(prop, assign.Value, action, paramEnv),
                            value,
                            action));
                        break;
                    }
                case ConditionalEffect cond:
                    var thenEnv = CloneEnv(env);
                    Refine(thenEnv, cond.Condition);
                    var thenDead = ArmIsDead(env, thenEnv);
                    ApplyEffects(cond.ThenEffects, entity, action, thenEnv, paramEnv, visited, result, domain, currentStage);
                    Dictionary<string, AbstractValue>? elseEnv = null;
                    var elseDead = false;
                    if (cond.ElseEffects is not null) {
                        elseEnv = CloneEnv(env);
                        RefineNegated(elseEnv, cond.Condition);
                        elseDead = ArmIsDead(env, elseEnv);
                        ApplyEffects(cond.ElseEffects, entity, action, elseEnv, paramEnv, visited, result, domain, currentStage);
                    }
                    JoinBranches(env, thenEnv, thenDead, elseEnv, elseDead, entity);
                    break;
                case CompositeEffect composite:
                    ApplyEffects(composite.Effects, entity, action, env, paramEnv, visited, result, domain, currentStage);
                    break;
                case InvokeActionEffect invoke:
                    ApplyInvoke(invoke, entity, action, env, paramEnv, visited, result, domain, currentStage);
                    DropAllRelations(env);
                    break;
                case ForEachEffect fe:
                    // Zero or more iterations: assigns join into the env (Merge), so the
                    // pre-loop value still holds for zero iterations.
                    ApplyEffects(fe.Body, entity, action, env, paramEnv, visited, result, domain, currentStage);
                    break;
                case ForEachInvokeEffect efe:
                    ApplyForEachInvoke(efe, entity, action, env, paramEnv, visited, result, domain);
                    DropAllRelations(env);
                    break;
            }
        }
    }

    /// <summary>A callee runs on a copy of the environment and may reassign any property, so after
    /// an invoke no relation between symbols is known (conservative widening).</summary>
    private static void DropAllRelations(Dictionary<string, AbstractValue> env) {
        foreach (var key in env.Keys.ToList())
            env[key] = env[key].WithoutRelations();
    }

    /// <summary>Call-chain propagation. Self-invoke threads the current environment into the
    /// callee (refined by its own preconditions) with argument bindings mapped to its params.
    /// Cross-entity invoke (Rel.Action) builds the related entity's environment and steps
    /// the callee on it.</summary>
    private static void ApplyInvoke(
        InvokeActionEffect invoke, Entity entity, Action action,
        Dictionary<string, AbstractValue> env,
        IReadOnlyDictionary<string, AbstractValue>? paramEnv,
        HashSet<Action> visited, List<EffectPostcondition> result, Domain domain,
        string? currentStage) {
        if (invoke.TargetRelationship is not null) {
            ApplyCrossEntityInvoke(invoke, entity, action, env, paramEnv, visited, result, domain);
            return;
        }

        var target = DomainAnalysis.FindAction(entity, invoke.ActionName, currentStage);
        if (target is null || !visited.Add(target)) return;

        var targetEnv = CloneEnv(env);
        foreach (var policy in CollectPreconditions(target, entity, null))
            Refine(targetEnv, policy.Expression);

        Dictionary<string, AbstractValue>? targetParams = null;
        if (invoke.ParameterBindings.Count > 0) {
            targetParams = new(StringComparer.Ordinal);
            foreach (var binding in invoke.ParameterBindings)
                targetParams[binding.PropertyName] = Eval(binding.Expression, entity, action, env, paramEnv, domain: domain);
        }

        // The callee's own guards refine its entry state; only what its EFFECTS change afterwards is
        // carried back to the caller (the guard facts stay the callee's).
        var atEntry = CloneEnv(targetEnv);

        try {
            ApplyEffects(target.Effects, entity, target, targetEnv, targetParams, visited, result, domain, currentStage);
        }
        finally {
            visited.Remove(target);
        }

        MergeCalleePostState(env, targetEnv, atEntry, entity);
    }

    /// <summary>#367 B-3: a self-invoke runs on the SAME entity (the callee env is keyed by this
    /// entity's property names), so the properties its effects rewrote now hold the callee's final
    /// values in the caller. A strong update is sound because a failed invoke returns the caller's
    /// failure (fail-fast, <c>EffectLoweringPass.WrapInvokeResult</c>) and the action's transaction
    /// restores the instance, so effects after the invoke only run on the callee's success path. The
    /// callee's conditionals are already joined (B-1) inside its env. A value the callee assigned
    /// outside its declared range looks empty here (assign narrows by intersection), which is not
    /// proof the path is unreachable, so it falls back to the declared constraints.</summary>
    private static void MergeCalleePostState(
        Dictionary<string, AbstractValue> env, Dictionary<string, AbstractValue> post,
        Dictionary<string, AbstractValue> atEntry, Entity entity) {
        foreach (var (key, value) in post) {
            if (atEntry.TryGetValue(key, out var before) && ReferenceEquals(before, value)) continue;
            if (!env.ContainsKey(key)) continue;
            env[key] = SoundArm(value, key, entity);
        }
    }

    /// <summary>Cross-entity invoke: build the related entity's environment (declared
    /// constraints refined by its own preconditions) and step the target action on it.</summary>
    private static void ApplyCrossEntityInvoke(
        InvokeActionEffect invoke, Entity entity, Action action,
        Dictionary<string, AbstractValue> env,
        IReadOnlyDictionary<string, AbstractValue>? paramEnv,
        HashSet<Action> visited, List<EffectPostcondition> result, Domain domain) {
        var relationship = entity.Navigations.FirstOrDefault(r =>
            string.Equals(r.Name, invoke.TargetRelationship, StringComparison.Ordinal));
        if (relationship is null) return;
        var targetEntity = domain.Types.OfType<Entity>().FirstOrDefault(e =>
            string.Equals(e.Name, relationship.Target.TypeName, StringComparison.Ordinal));
        if (targetEntity is null) return;

        var targetAction = DomainAnalysis.FindAction(targetEntity, invoke.ActionName);
        if (targetAction is null || !visited.Add(targetAction)) return;

        // The target entity's environment: declared constraints, refined by its own
        // preconditions.
        var targetEnv = new Dictionary<string, AbstractValue>(StringComparer.Ordinal);
        foreach (var prop in targetEntity.Properties)
            targetEnv[prop.Name] = AbstractValue.From(prop.Constraints);
        foreach (var policy in CollectPreconditions(targetAction, targetEntity, null))
            Refine(targetEnv, policy.Expression);

        Dictionary<string, AbstractValue>? targetParams = null;
        if (invoke.ParameterBindings.Count > 0) {
            targetParams = new(StringComparer.Ordinal);
            foreach (var binding in invoke.ParameterBindings)
                targetParams[binding.PropertyName] = Eval(binding.Expression, entity, action, env, paramEnv, domain: domain);
        }

        try {
            ApplyEffects(targetAction.Effects, targetEntity, targetAction, targetEnv, targetParams, visited, result, domain, currentStage: null);
        }
        finally {
            visited.Remove(targetAction);
        }
    }

    /// <summary>Fan-out invoke (<c>for Rel as x [where x.Policy] invoke x.Action</c>): build
    /// the target entity's environment refined by the predicate's NAMED policy (the same
    /// reasoning as a require gate) and step the callee on it.</summary>
    private static void ApplyForEachInvoke(
        ForEachInvokeEffect efe, Entity entity, Action action,
        Dictionary<string, AbstractValue> env,
        IReadOnlyDictionary<string, AbstractValue>? paramEnv,
        HashSet<Action> visited, List<EffectPostcondition> result, Domain domain) {
        var relationship = entity.Navigations.FirstOrDefault(r =>
            string.Equals(r.Name, efe.RelationshipName, StringComparison.Ordinal));
        if (relationship is null) return;
        var targetEntity = domain.Types.OfType<Entity>().FirstOrDefault(e =>
            string.Equals(e.Name, relationship.Target.TypeName, StringComparison.Ordinal));
        if (targetEntity is null) return;

        var targetAction = DomainAnalysis.FindAction(targetEntity, efe.ActionName);
        if (targetAction is null || !visited.Add(targetAction)) return;

        // Target environment: declared constraints refined by its own preconditions and by
        // the predicate's named policy (a require-gate-style refinement).
        var targetEnv = new Dictionary<string, AbstractValue>(StringComparer.Ordinal);
        foreach (var prop in targetEntity.Properties)
            targetEnv[prop.Name] = AbstractValue.From(prop.Constraints);
        foreach (var policy in CollectPreconditions(targetAction, targetEntity, null))
            Refine(targetEnv, policy.Expression);
        if (efe.Predicate is ForEachNamedPolicy { PolicyName: var policyName }) {
            var predicatePolicy = targetEntity.Policies.FirstOrDefault(p =>
                string.Equals(p.Name, policyName, StringComparison.Ordinal));
            if (predicatePolicy is not null)
                Refine(targetEnv, predicatePolicy.Expression);
        }

        Dictionary<string, AbstractValue>? targetParams = null;
        if (efe.ParameterBindings.Count > 0) {
            targetParams = new(StringComparer.Ordinal);
            foreach (var binding in efe.ParameterBindings)
                targetParams[binding.PropertyName] = EvalForEachBinding(
                    binding.Expression, efe.BinderName, targetEntity, entity, action, env, paramEnv, domain);
        }

        try {
            ApplyEffects(targetAction.Effects, targetEntity, targetAction, targetEnv, targetParams, visited, result, domain, currentStage: null);
        }
        finally {
            visited.Remove(targetAction);
        }
    }

    /// <summary>A for-each binding argument's only legal navigation root is the BINDER (<c>line Qty</c>), which
    /// names the ITERATED entity, not a caller relationship that happens to share the binder's name. The leaf
    /// is evaluated against the iterated entity's declared constraints. A non-binder root is rejected by the
    /// validator (DMEFF007); an API-built one keeps its caller-relative meaning.</summary>
    private static AbstractValue EvalForEachBinding(
        DomainExpression expr, string binder, Entity iterated, Entity caller, Action action,
        Dictionary<string, AbstractValue> env, IReadOnlyDictionary<string, AbstractValue>? paramEnv, Domain domain) {
        switch (expr) {
            case RelationshipNavigation rn:
                return string.Equals(rn.RelationshipName, binder, StringComparison.Ordinal)
                    ? Eval(rn.TargetProperty, iterated, null, EmptyEnv, null, null, domain)
                    : Eval(rn, caller, action, env, paramEnv, domain: domain);
            case Add add:
                return ComposeArithmetic(
                    EvalForEachBinding(add.Left, binder, iterated, caller, action, env, paramEnv, domain),
                    EvalForEachBinding(add.Right, binder, iterated, caller, action, env, paramEnv, domain), ArithOp.Add);
            case Subtract sub:
                return ComposeArithmetic(
                    EvalForEachBinding(sub.Left, binder, iterated, caller, action, env, paramEnv, domain),
                    EvalForEachBinding(sub.Right, binder, iterated, caller, action, env, paramEnv, domain), ArithOp.Sub);
            case Multiply mul:
                return ComposeArithmetic(
                    EvalForEachBinding(mul.Left, binder, iterated, caller, action, env, paramEnv, domain),
                    EvalForEachBinding(mul.Right, binder, iterated, caller, action, env, paramEnv, domain), ArithOp.Mul);
        }
        return Eval(expr, caller, action, env, paramEnv, domain: domain);
    }

    /// <summary>#367 B-1: after an <c>if</c>, each symbol either arm touched becomes the join
    /// (lub) of the two arms; with no <c>else</c> the unchanged incoming environment is the
    /// implicit else. A symbol both arms left as the incoming instance is not rewritten.
    /// An arm value that is <see cref="AbstractValue.Unsatisfiable"/> is not treated as an
    /// unreachable arm: the assign step narrows by intersection, so an arm that assigns
    /// outside its refinement looks empty while the value really exists. That arm contributes
    /// the property's declared constraints instead, which keeps the join sound.</summary>
    private static void JoinBranches(
        Dictionary<string, AbstractValue> env,
        Dictionary<string, AbstractValue> thenEnv, bool thenDead,
        Dictionary<string, AbstractValue>? elseEnv, bool elseDead,
        Entity entity) {
        // A dead arm (its condition contradicts the incoming facts) is bottom: it is skipped.
        // Both dead: the incoming environment is already the best statement.
        if (thenDead && elseDead) return;
        if (thenDead) { if (elseEnv is not null) AdoptArm(env, elseEnv); return; }
        if (elseDead) { AdoptArm(env, thenEnv); return; }
        var keys = new HashSet<string>(env.Keys, StringComparer.Ordinal);
        keys.UnionWith(thenEnv.Keys);
        if (elseEnv is not null) keys.UnionWith(elseEnv.Keys);

        foreach (var key in keys) {
            env.TryGetValue(key, out var incoming);
            if (!thenEnv.TryGetValue(key, out var fromThen)) continue;
            AbstractValue? fromElse;
            if (elseEnv is null) fromElse = incoming;
            else if (!elseEnv.TryGetValue(key, out fromElse)) continue;
            if (fromElse is null) continue;
            if (ReferenceEquals(fromThen, incoming) && ReferenceEquals(fromElse, incoming)) continue;

            env[key] = SoundArm(fromThen, key, entity).Join(SoundArm(fromElse, key, entity));
        }
    }

    /// <summary>With the then arm dead and an else present, the else arm is the only path.</summary>
    private static void AdoptArm(Dictionary<string, AbstractValue> env, Dictionary<string, AbstractValue> arm) {
        foreach (var (key, value) in arm)
            env[key] = value;
    }

    /// <summary>True when refining by the branch condition turned a satisfiable incoming
    /// symbol into an unsatisfiable one: the arm is unreachable.</summary>
    private static bool ArmIsDead(
        Dictionary<string, AbstractValue> incoming, Dictionary<string, AbstractValue> refined) {
        foreach (var (key, value) in refined)
            if (value.Unsatisfiable && incoming.TryGetValue(key, out var before) && !before.Unsatisfiable)
                return true;
        return false;
    }

    private static AbstractValue SoundArm(AbstractValue arm, string key, Entity entity) {
        if (!arm.Unsatisfiable) return arm;
        var prop = entity.Properties.FirstOrDefault(p => string.Equals(p.Name, key, StringComparison.Ordinal));
        return prop is null ? AbstractValue.Unknown : AbstractValue.From(prop.Constraints);
    }

    private static Dictionary<string, AbstractValue> CloneEnv(Dictionary<string, AbstractValue> env) =>
        new(env, StringComparer.Ordinal);

    /// <summary>The net postcondition constraint for an assign: the target's constraints
    /// merged with a parameter's constraints when the RHS is a parameter.</summary>
    private static IReadOnlyList<Constraint> BuildPostconditionConstraints(
        Property targetProp, DomainExpression value, Action action,
        IReadOnlyDictionary<string, AbstractValue>? paramEnv) {
        var merged = new List<Constraint>(targetProp.Constraints);
        if (value is ParameterAccess pa) {
            var param = action.Parameters.FirstOrDefault(p =>
                string.Equals(p.Name, pa.Name, StringComparison.Ordinal));
            if (param is not null) {
                foreach (var pc in param.Constraints) {
                    var idx = merged.FindIndex(c => c.GetType() == pc.GetType());
                    if (idx >= 0) {
                        if (merged[idx].Merge(pc) is { } net) merged[idx] = net;
                    }
                    else {
                        merged.Add(pc);
                    }
                }
            }
        }
        return merged;
    }

    // ── Unsatisfiable precondition detection ────────────────────

    private static void ReportUnsatisfiablePreconditions(
        AnalysisContext context, Action action, string? stageName,
        IReadOnlyDictionary<string, AbstractValue> env,
        IReadOnlyDictionary<string, AbstractValue> scalarEnv,
        IReadOnlyList<RelationConflict> conflicts) {
        var prefix = $"Action '{action.Name}'{(stageName is null ? "" : $" in stage '{stageName}'")} has unsatisfiable preconditions: ";
        // A property is reported by name only when its own (non-relational) constraints are empty;
        // a property that only the relation emptied is covered by the relation's single report.
        foreach (var (prop, value) in env) {
            if (!value.Unsatisfiable) continue;
            if (!(scalarEnv.TryGetValue(prop, out var scalar) && scalar.Unsatisfiable)
                && conflicts.Any(c => c.Left == prop || c.Right == prop))
                continue;
            context.ReportError(
                action,
                prefix + $"its guard policies narrow property '{prop}' to an empty admissible set " +
                "given the property's declared constraint.",
                DomainModelDiagnosticCodes.ConstraintSatisfiability);
        }
        var seen = new HashSet<(string, string)>();
        foreach (var c in conflicts) {
            var pair = string.CompareOrdinal(c.Left, c.Right) <= 0 ? (c.Left, c.Right) : (c.Right, c.Left);
            if (!seen.Add(pair)) continue;
            context.ReportError(
                action,
                prefix + $"the relation '{c.Left} {RelationalConstraint.Symbol(c.Orderings)} {c.Right}' between " +
                $"properties '{c.Left}' and '{c.Right}' contradicts the other constraints on them.",
                DomainModelDiagnosticCodes.ConstraintSatisfiability);
        }
    }

    // ── Numeric fallback for diagnostics (no-metadata contexts) ──

    /// <summary>
    /// The abstract value of <paramref name="expr"/> evaluated in <paramref name="entity"/>'s scope.
    /// Create initializers call this with the CREATOR entity, not the created one: an initializer's
    /// expression is written in the creating action's scope, exactly as an assign's RHS is.
    /// </summary>
    internal static AbstractValue InferValue(
        DomainExpression expr, Entity entity, Action? action,
        IReadOnlyDictionary<string, ValueRange?>? narrowed = null) {
        var env = new Dictionary<string, AbstractValue>(StringComparer.Ordinal);
        if (narrowed is not null) {
            foreach (var (k, v) in narrowed)
                env[k] = v is { } r ? AbstractValue.From([new RangeConstraint(r.Min, r.Max)]) : AbstractValue.Unknown;
        }
        return Eval(expr, entity, action, env);
    }

    internal static (double? Min, double? Max) InferNumericRange(
        DomainExpression expr, Entity entity, Action? action,
        IReadOnlyDictionary<string, ValueRange?>? narrowed = null) =>
        InferValue(expr, entity, action, narrowed).NumericRange is { } nr ? (nr.Min, nr.Max) : (null, null);
}

/// <summary>Queryable surface for the additive invariant model.</summary>
public static class EffectInvariantQueries {
    public static ActionInvariantMetadata? GetActionInvariants(this INodeMetadataProvider provider, Action action) =>
        provider.GetMetadata<ActionInvariantMetadata>(action);

    public static IReadOnlyList<ActionStageInvariant> StageContexts(this ActionInvariantMetadata metadata) =>
        metadata.StageContexts;

    public static ActionStageInvariant? ContextForStage(this ActionInvariantMetadata metadata, string? stageName) =>
        metadata.StageContexts.FirstOrDefault(c =>
            string.Equals(c.StageName, stageName, StringComparison.Ordinal));

    /// <summary>Net constraints for a property in a context (declared + policy-narrowed, per type).</summary>
    public static IReadOnlyList<Constraint> ImplicitConstraints(
        this ActionStageInvariant context, Entity entity, string propertyName) {
        if (context.MergedConstraints.TryGetValue(propertyName, out var merged))
            return merged;
        return entity.Properties.FirstOrDefault(p =>
            string.Equals(p.Name, propertyName, StringComparison.Ordinal))?.Constraints ?? [];
    }

    /// <summary>Strictest per-property numeric narrowing across every stage context.</summary>
    public static IReadOnlyDictionary<string, ValueRange> CombinedRanges(this ActionInvariantMetadata metadata) {
        var result = new Dictionary<string, ValueRange>(StringComparer.Ordinal);
        foreach (var context in metadata.StageContexts) {
            foreach (var (prop, range) in context.NarrowedRanges) {
                if (range is null) continue;
                if (!result.TryGetValue(prop, out var current)) { result[prop] = range; continue; }
                result[prop] = new ValueRange(
                    current.Min is null || (range.Min is not null && range.Min > current.Min) ? range.Min : current.Min,
                    current.Max is null || (range.Max is not null && range.Max < current.Max) ? range.Max : current.Max);
            }
        }
        return result;
    }

    /// <summary>Net constraints for a property across every stage context (per-type intersection).</summary>
    public static IReadOnlyList<Constraint> CombinedConstraints(
        this ActionInvariantMetadata metadata, Entity entity, string propertyName) {
        var merged = new List<Constraint>();
        foreach (var context in metadata.StageContexts) {
            if (!context.MergedConstraints.TryGetValue(propertyName, out var perContext)) continue;
            foreach (var constraint in perContext) {
                var idx = merged.FindIndex(c => c.GetType() == constraint.GetType());
                if (idx < 0) { merged.Add(constraint); continue; }
                if (merged[idx].Merge(constraint) is { } net) merged[idx] = net;
            }
        }
        return merged;
    }

    public static (IReadOnlyList<Policy> Preconditions, IReadOnlyList<EffectPostcondition> Postconditions)
        PreAndPostConditions(this ActionStageInvariant context) =>
        (context.Preconditions, context.Postconditions);
}