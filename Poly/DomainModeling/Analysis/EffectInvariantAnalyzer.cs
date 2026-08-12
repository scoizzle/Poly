using Poly.Analysis;
using Poly.DomainModeling.Constraints;
using Poly.DomainModeling.Effects;

namespace Poly.DomainModeling.Analysis;

/// <summary>A numeric value range (inclusive bounds, null = unbounded).</summary>
public sealed record ValueRange(double? Min, double? Max);

/// <summary>
/// A postcondition an effect must respect: assigning to <see cref="TargetProperty"/>
/// must satisfy <see cref="Constraints"/>; <see cref="ValueRange"/> is the value range
/// the effect's expression can produce in this stage context (when statically known).
/// <see cref="DeclaringAction"/> is the action whose body contains the effect — it differs
/// from the context action for call-chain postconditions.
/// </summary>
public sealed record EffectPostcondition(
    Node Effect,
    string TargetProperty,
    IReadOnlyList<Constraint> Constraints,
    ValueRange? ValueRange,
    Action DeclaringAction);

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
    public string[] Dependencies => [EffectFactsPass.Id];

    public void Analyze(AnalysisContext context, Node node) {
        if (!context.ShouldAnalyze(node))
            return;

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
            var env = BuildInitialEnv(entity, preconditions);
            ReportUnsatisfiablePreconditions(context, action, stageName, env);
            var narrowed = env.ToDictionary(kv => kv.Key, kv => kv.Value.NumericRange, StringComparer.Ordinal);
            var merged = env.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<Constraint>)kv.Value.Constraints, StringComparer.Ordinal);
            var postconditions = new List<EffectPostcondition>();
            var visited = new HashSet<Action>(ReferenceEqualityComparer.Instance) { action };
            ApplyEffects(action.Effects, entity, action, env, paramEnv: null, visited, postconditions, domain);
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
        var inverted = action.Policies
            .Where(p => p.Name.StartsWith("not_", StringComparison.Ordinal))
            .Select(p => p.Name.Substring(4))
            .ToHashSet(StringComparer.Ordinal);
        return action.Policies
            .Where(p => !p.Name.StartsWith("not_", StringComparison.Ordinal))
            .Concat(stage?.Policies ?? [])
            .Concat(entity.Policies.Where(p => !inverted.Contains(p.Name)))
            .ToList();
    }

    // ── Abstract environment ────────────────────────────────────

    /// <summary>The initial abstract environment: each property's declared constraints as an
    /// <see cref="AbstractValue"/>, refined by every precondition's bounds.</summary>
    private static Dictionary<string, AbstractValue> BuildInitialEnv(
        Entity entity, IReadOnlyList<Policy> preconditions) {
        var env = new Dictionary<string, AbstractValue>(StringComparer.Ordinal);
        foreach (var prop in entity.Properties)
            env[prop.Name] = AbstractValue.From(prop.Constraints);
        foreach (var policy in preconditions)
            Refine(env, policy.Expression);
        return env;
    }

    /// <summary>Narrows the symbols an environment mentions by a boolean condition's bounds
    /// (a guard, an if-branch, or a where-filter). Single-primitive refinement.</summary>
    private static void Refine(Dictionary<string, AbstractValue> env, DomainExpression condition) {
        foreach (var conjunct in FlattenAnds(condition)) {
            if (conjunct is not Comparison cmp) continue;
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

    private static void RefineNegated(Dictionary<string, AbstractValue> env, DomainExpression condition) {
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
            return ToDouble(lit.Value) is double d && SetBound(cmp.Kind, d, out value, out bound);
        }
        if (cmp.Left is Literal { Value: not null } lit2 && cmp.Right is PropertyAccess pa2) {
            propName = pa2.Name;
            return ToDouble(lit2.Value) is double d2 && SetBound(Flip(cmp.Kind), d2, out value, out bound);
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
        Entity? targetEntity = null) {
        switch (expr) {
            case Literal { Value: not null } lit:
                return ToDouble(lit.Value) is double d
                    ? AbstractValue.From([new RangeConstraint(d, d)])
                    : AbstractValue.From(lit.Value is string s ? [new EqualityConstraint(s)] : []);
            case PropertyAccess pa:
                if (env.TryGetValue(pa.Name, out var pv)) return pv;
                // Cross-entity read: the property belongs to the target entity.
                var owner = targetEntity ?? entity;
                var prop = owner.Properties.FirstOrDefault(p =>
                    string.Equals(p.Name, pa.Name, StringComparison.Ordinal));
                return prop is null ? AbstractValue.Unknown : AbstractValue.From(prop.Constraints);
            case ParameterAccess pa2:
                if (paramEnv is not null && paramEnv.TryGetValue(pa2.Name, out var bv)) return bv;
                var param = action?.Parameters.FirstOrDefault(p =>
                    string.Equals(p.Name, pa2.Name, StringComparison.Ordinal));
                return param is null ? AbstractValue.Unknown : AbstractValue.From(param.Constraints);
            case RelationshipNavigation rn:
                // Path-prefix leaf on a related entity — best-effort: read the leaf property
                // on the target entity's environment (caller supplies it via targetEntity).
                return Eval(rn.TargetProperty, entity, action, env, paramEnv, targetEntity);
            case Add add:
                return ComposeArithmetic(Eval(add.Left, entity, action, env, paramEnv, targetEntity),
                    Eval(add.Right, entity, action, env, paramEnv, targetEntity), compose: (l, r) => l + r);
            case Subtract sub:
                return ComposeArithmetic(Eval(sub.Left, entity, action, env, paramEnv, targetEntity),
                    Eval(sub.Right, entity, action, env, paramEnv, targetEntity), compose: (l, r) => l - r);
            case Multiply mul:
                return ComposeArithmetic(Eval(mul.Left, entity, action, env, paramEnv, targetEntity),
                    Eval(mul.Right, entity, action, env, paramEnv, targetEntity), compose: (l, r) => l * r);
            default:
                return AbstractValue.Unknown;
        }
    }

    private static AbstractValue ComposeArithmetic(
        AbstractValue left, AbstractValue right, Func<double, double, double> compose) {
        if (left.NumericRange is not { } lr || right.NumericRange is not { } rr) return AbstractValue.Unknown;
        if (lr.Min is null || lr.Max is null || rr.Min is null || rr.Max is null) return AbstractValue.Unknown;
        // For + and -, the general min/max of the endpoint combos (works for * too with care).
        double[] combos = [compose(lr.Min.Value, rr.Min.Value), compose(lr.Min.Value, rr.Max.Value),
                           compose(lr.Max.Value, rr.Min.Value), compose(lr.Max.Value, rr.Max.Value)];
        return AbstractValue.From([new RangeConstraint(combos.Min(), combos.Max())]);
    }

    // ── Effect stepping ─────────────────────────────────────────

    private static void ApplyEffects(
        IReadOnlyList<Effect> effects, Entity entity, Action action,
        Dictionary<string, AbstractValue> env,
        IReadOnlyDictionary<string, AbstractValue>? paramEnv,
        HashSet<Action> visited, List<EffectPostcondition> result, Domain domain) {
        foreach (var effect in effects) {
            switch (effect) {
                case AssignEffect { Target: PropertyAccess target } assign: {
                        var prop = entity.Properties.FirstOrDefault(p =>
                            string.Equals(p.Name, target.Name, StringComparison.Ordinal));
                        if (prop is null) break;
                        var value = Eval(assign.Value, entity, action, env, paramEnv);
                        env[target.Name] = env.TryGetValue(target.Name, out var cur) ? cur.Merge(value) : value;
                        result.Add(new EffectPostcondition(
                            assign, prop.Name,
                            BuildPostconditionConstraints(prop, assign.Value, action, paramEnv),
                            value.NumericRange,
                            action));
                        break;
                    }
                case ConditionalEffect cond:
                    var thenEnv = CloneEnv(env);
                    Refine(thenEnv, cond.Condition);
                    ApplyEffects(cond.ThenEffects, entity, action, thenEnv, paramEnv, visited, result, domain);
                    if (cond.ElseEffects is not null) {
                        var elseEnv = CloneEnv(env);
                        RefineNegated(elseEnv, cond.Condition);
                        ApplyEffects(cond.ElseEffects, entity, action, elseEnv, paramEnv, visited, result, domain);
                    }
                    break;
                case CompositeEffect composite:
                    ApplyEffects(composite.Effects, entity, action, env, paramEnv, visited, result, domain);
                    break;
                case InvokeActionEffect invoke:
                    ApplyInvoke(invoke, entity, action, env, paramEnv, visited, result, domain);
                    break;
            }
        }
    }

    /// <summary>Call-chain propagation. Self-invoke threads the current environment into the
    /// callee (refined by its own preconditions) with argument bindings mapped to its params.
    /// Cross-entity invoke (Rel.Action) builds the related entity's environment and applies the
    /// where-filter as a refinement on it.</summary>
    private static void ApplyInvoke(
        InvokeActionEffect invoke, Entity entity, Action action,
        Dictionary<string, AbstractValue> env,
        IReadOnlyDictionary<string, AbstractValue>? paramEnv,
        HashSet<Action> visited, List<EffectPostcondition> result, Domain domain) {
        if (invoke.TargetRelationship is not null) {
            ApplyCrossEntityInvoke(invoke, entity, action, env, paramEnv, visited, result, domain);
            return;
        }

        var target = entity.Actions.FirstOrDefault(a =>
            string.Equals(a.Name, invoke.ActionName, StringComparison.Ordinal));
        if (target is null || !visited.Add(target)) return;

        var targetEnv = CloneEnv(env);
        foreach (var policy in CollectPreconditions(target, entity, null))
            Refine(targetEnv, policy.Expression);

        Dictionary<string, AbstractValue>? targetParams = null;
        if (invoke.ParameterBindings.Count > 0) {
            targetParams = new(StringComparer.Ordinal);
            foreach (var binding in invoke.ParameterBindings)
                targetParams[binding.PropertyName] = Eval(binding.Expression, entity, action, env, paramEnv);
        }

        ApplyEffects(target.Effects, entity, target, targetEnv, targetParams, visited, result, domain);
    }

    /// <summary>Cross-entity invoke: build the related entity's environment (declared
    /// constraints), refine it by the where-filter, and step the target action on it.</summary>
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

        var targetAction = targetEntity.Actions.FirstOrDefault(a =>
            string.Equals(a.Name, invoke.ActionName, StringComparison.Ordinal));
        if (targetAction is null || !visited.Add(targetAction)) return;

        // The target entity's environment: declared constraints, refined by its own
        // preconditions and by the invoke's where-filter (target-local props).
        var targetEnv = new Dictionary<string, AbstractValue>(StringComparer.Ordinal);
        foreach (var prop in targetEntity.Properties)
            targetEnv[prop.Name] = AbstractValue.From(prop.Constraints);
        foreach (var policy in CollectPreconditions(targetAction, targetEntity, null))
            Refine(targetEnv, policy.Expression);
        if (invoke.Filter is not null)
            Refine(targetEnv, invoke.Filter);

        Dictionary<string, AbstractValue>? targetParams = null;
        if (invoke.ParameterBindings.Count > 0) {
            targetParams = new(StringComparer.Ordinal);
            foreach (var binding in invoke.ParameterBindings)
                targetParams[binding.PropertyName] = Eval(binding.Expression, entity, action, env, paramEnv);
        }

        ApplyEffects(targetAction.Effects, targetEntity, targetAction, targetEnv, targetParams, visited, result, domain);
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
        IReadOnlyDictionary<string, AbstractValue> env) {
        foreach (var (prop, value) in env) {
            if (!value.Unsatisfiable) continue;
            context.ReportError(
                action,
                $"Action '{action.Name}'{(stageName is null ? "" : $" in stage '{stageName}'")} has unsatisfiable " +
                $"preconditions: its guard policies narrow property '{prop}' to an empty admissible set " +
                "given the property's declared constraint.",
                DomainModelDiagnosticCodes.ConstraintSatisfiability);
        }
    }

    // ── Numeric fallback for diagnostics (no-metadata contexts) ──

    internal static (double? Min, double? Max) InferNumericRange(
        DomainExpression expr, Entity entity, Action? action,
        IReadOnlyDictionary<string, ValueRange?>? narrowed = null) {
        var env = new Dictionary<string, AbstractValue>(StringComparer.Ordinal);
        if (narrowed is not null) {
            foreach (var (k, v) in narrowed)
                env[k] = v is { } r ? AbstractValue.From([new RangeConstraint(r.Min, r.Max)]) : AbstractValue.Unknown;
        }
        var value = Eval(expr, entity, action, env);
        return value.NumericRange is { } nr ? (nr.Min, nr.Max) : (null, null);
    }

    private static double? ToDouble(object? value) {
        try { return Convert.ToDouble(value); }
        catch { return null; }
    }
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