using Poly.DomainModeling.Meaning;
using Poly.DomainModeling.Ontology;

using Action = Poly.DomainModeling.Ontology.Action;
using Add = Poly.DomainModeling.Ontology.Add;
using And = Poly.DomainModeling.Ontology.And;
using Multiply = Poly.DomainModeling.Ontology.Multiply;
using Not = Poly.DomainModeling.Ontology.Not;
using Or = Poly.DomainModeling.Ontology.Or;
using Subtract = Poly.DomainModeling.Ontology.Subtract;

namespace Poly.DomainModeling.Analysis;

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

/// <summary>
/// A create or create-in initializer's abstract value in one stage context: <see cref="Initializer"/>'s expression
/// evaluated in the creating action's environment at the create (guards, branch conditions and earlier assigns
/// applied), in the creator's scope. <see cref="DeclaringAction"/> is the action whose body holds the create.
/// </summary>
public sealed record InitializerValue(PropertyBinding Initializer, AbstractValue Value, Action DeclaringAction) {
    /// <summary>The value's numeric range, when the value carries one.</summary>
    public ValueRange? ValueRange => Value.NumericRange;
}

/// <summary>The additive invariant picture for an action in ONE stage context.</summary>
public sealed record ActionStageInvariant(
    string? StageName,
    IReadOnlyList<Policy> Preconditions,
    IReadOnlyDictionary<string, ValueRange?> NarrowedRanges,
    IReadOnlyDictionary<string, IReadOnlyList<Constraint>> MergedConstraints,
    IReadOnlyList<EffectPostcondition> Postconditions) {
    /// <summary>The values of the action's own create and create-in initializers in this context. An initializer
    /// with no entry here (a create in a loop body, or after an effect the flow does not model) is judged against
    /// declared ranges.</summary>
    public IReadOnlyList<InitializerValue> InitializerValues { get; init; } = [];
}

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
            var axes = new DomainGuardAxes(TypeModelOf(context, domain));
            foreach (var type in domain.Types) {
                if (type is Entity entity) {
                    foreach (var action in entity.Actions)
                        Publish(context, action, entity, domain, axes);
                    foreach (var stage in entity.Stages)
                        foreach (var action in stage.Actions)
                            Publish(context, action, entity, domain, axes);
                }
            }
            return;
        }

        this.AnalyzeChildren(context, node);
    }

    /// <summary>
    /// The domain type model this pass reads integrality from: <see cref="DomainTypeModelMetadata"/>, stamped on the
    /// <see cref="Domain"/> node by the <c>DomainExpressionType</c> pass, which runs before this one (<c>CorePasses</c>
    /// order). An absent stamp is a wrong pass order: this fails closed rather than treat every axis as non-integral.
    /// </summary>
    private static DomainTypeModel TypeModelOf(AnalysisContext context, Domain domain) =>
        context.GetMetadata<DomainTypeModelMetadata>(domain)?.Model
        ?? throw new InvalidOperationException(
            $"The {Id} pass reads {nameof(DomainTypeModelMetadata)} on domain '{domain.Name}', which the " +
            $"{ExpressionTypeAnalyzer.Id} pass stamps; it is absent, so that pass did not run before this one.");

    /// <summary>What a guard needs to know about one entity's properties: which hold integers (a strict bound on one
    /// is exact) and which are optional (a guard that also holds for a null value cannot empty one).</summary>
    internal sealed record GuardAxes(IReadOnlySet<string> Integral, IReadOnlySet<string> Optional);

    /// <summary>The <see cref="GuardAxes"/> of each entity, computed once per entity from the domain type model:
    /// integral is <see cref="NumericKind.Integer64"/>. A guard narrows an environment with the axes of the entity
    /// that environment belongs to, so a callee's guard uses the callee entity's.</summary>
    private sealed class DomainGuardAxes(DomainTypeModel model) {
        private readonly Dictionary<Entity, GuardAxes> _byEntity = new(ReferenceEqualityComparer.Instance);

        public GuardAxes For(Entity entity) {
            if (_byEntity.TryGetValue(entity, out var axes)) return axes;
            axes = new GuardAxes(
                entity.Properties.Where(p => model.KindOf(model.OfProperty(p)) == NumericKind.Integer64)
                    .Select(p => p.Name).ToHashSet(StringComparer.Ordinal),
                entity.Properties.Where(p => !EntityStructureAnalyzer.IsRequired(p))
                    .Select(p => p.Name).ToHashSet(StringComparer.Ordinal));
            _byEntity[entity] = axes;
            return axes;
        }
    }

    private static void Publish(AnalysisContext context, Action action, Entity entity, Domain domain, DomainGuardAxes axes) {
        var contexts = new List<ActionStageInvariant>();
        foreach (var (stageName, stage) in ValidStages(action, entity)) {
            var preconditions = CollectPreconditions(action, entity, stage);
            var guards = PolicyGuardFacts.Guards(action, stage);
            var conflicts = new List<RelationConflict>();
            var env = BuildInitialEnv(entity, guards, axes.For(entity), conflicts);
            var scalarEnv = conflicts.Count == 0 ? env : BuildInitialEnv(entity, guards, axes.For(entity), relations: false);
            ReportUnsatisfiablePreconditions(context, action, stageName, env, scalarEnv, conflicts);
            var narrowed = env.ToDictionary(kv => kv.Key, kv => kv.Value.NumericRange, StringComparer.Ordinal);
            var merged = env.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<Constraint>)kv.Value.Constraints, StringComparer.Ordinal);
            var postconditions = new List<EffectPostcondition>();
            // Call-stack scoped: an action is in the set only while its walk is on the stack.
            var visited = new HashSet<Action>(ReferenceEqualityComparer.Instance) { action };
            ApplyEffects(action.Effects, entity, action, env, paramEnv: null, visited, postconditions, domain, stageName, axes);
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
    /// <see cref="AbstractValue"/>, refined by every guard (<see cref="PolicyGuardFacts"/>).</summary>
    private static Dictionary<string, AbstractValue> BuildInitialEnv(
        Entity entity, IReadOnlyList<GuardedPolicy> guards, GuardAxes axes,
        List<RelationConflict>? conflicts = null, bool relations = true) {
        var env = new Dictionary<string, AbstractValue>(StringComparer.Ordinal);
        foreach (var prop in entity.Properties)
            env[prop.Name] = AbstractValue.From(prop.Constraints);
        foreach (var guard in guards)
            PolicyGuardFacts.Refine(env, guard, entity, args: null, instantiate: null, axes, conflicts, relations);
        return env;
    }

    /// <summary>A relation (<c>Start &lt; End</c>) whose refinement is what made two previously
    /// satisfiable properties unsatisfiable: one cause, reported once (#367 B-7).</summary>
    internal readonly record struct RelationConflict(string Left, string Right, RelationalOrdering Orderings);

    /// <summary>
    /// Narrows the symbols an environment mentions by a boolean condition (a guard, an if-branch, or a where-filter).
    /// With <see cref="RefineNegated"/> this is the one condition refiner: a comparison against a literal or between
    /// two properties, <c>and</c>, <c>or</c> (the hull of the arms) and <c>not</c> (the complement). Soundness
    /// contract: the refined environment never excludes a value the condition admits, so a leaf the flow does not
    /// model narrows nothing, positively or negated. A strict bound is exact on an integral axis
    /// (<see cref="GuardAxes.Integral"/>) and inclusive on any other (OQ-5). Ranges describe present values: a guard
    /// that is also true for a null optional property still narrows its present values (F4, as the else-branch does),
    /// but never empties it.
    /// </summary>
    internal static void Refine(
        Dictionary<string, AbstractValue> env, DomainExpression condition, GuardAxes axes,
        List<RelationConflict>? conflicts = null, bool relations = true) {
        switch (condition) {
            case And and:
                Refine(env, and.Left, axes, conflicts, relations);
                Refine(env, and.Right, axes, conflicts, relations);
                return;
            case Or or:
                RefineDisjunction(env,
                    arm => Refine(arm, or.Left, axes, conflicts: null, relations: false),
                    arm => Refine(arm, or.Right, axes, conflicts: null, relations: false));
                return;
            case Not not:
                RefineNegated(env, not.Operand, axes, conflicts, relations);
                return;
            case Comparison cmp:
                RefineComparison(env, cmp, negated: false, axes, conflicts, relations);
                return;
        }
    }

    /// <summary>Narrows an environment by the negation of <paramref name="condition"/> (an else-branch, or the operand
    /// of <c>not</c>): <c>not not x</c> is <c>x</c>, <c>not (a and b)</c> is <c>not a or not b</c>, <c>not (a or b)</c>
    /// is <c>not a and not b</c>, and a comparison takes its complement kind. Same contract as <see cref="Refine"/>.</summary>
    internal static void RefineNegated(
        Dictionary<string, AbstractValue> env, DomainExpression condition, GuardAxes axes,
        List<RelationConflict>? conflicts = null, bool relations = true) {
        switch (condition) {
            case Not not:
                Refine(env, not.Operand, axes, conflicts, relations);
                return;
            case And and:
                RefineDisjunction(env,
                    arm => RefineNegated(arm, and.Left, axes, conflicts: null, relations: false),
                    arm => RefineNegated(arm, and.Right, axes, conflicts: null, relations: false));
                return;
            case Or or:
                RefineNegated(env, or.Left, axes, conflicts, relations);
                RefineNegated(env, or.Right, axes, conflicts, relations);
                return;
            case Comparison cmp:
                RefineComparison(env, cmp, negated: true, axes, conflicts, relations);
                return;
        }
    }

    /// <summary>
    /// One comparison leaf, or its negation. A comparison is false for a null operand except <c>!=</c>, which is
    /// true, so the leaf holds for a null value exactly when it is a positive <c>!=</c> or a negated other kind. Such
    /// a leaf never empties an optional property, and a relation it records is dropped when it would contradict one:
    /// the value is then null, which a range does not describe, so neither is a contradiction.
    /// </summary>
    private static void RefineComparison(
        Dictionary<string, AbstractValue> env, Comparison cmp, bool negated, GuardAxes axes,
        List<RelationConflict>? conflicts, bool relations) {
        var holdsForNull = negated != (cmp.Kind == ComparisonKind.NotEqual);
        if (TryGetRelation(cmp, out var left, out var right, out var orderings)) {
            // A property-vs-property comparison negates exactly: the ordering is trichotomous
            // (Less | Equal | Greater), so the negation is the complement of the positive orderings.
            if (!relations) return;
            var relation = negated ? RelationalOrdering.All & ~orderings : orderings;
            if (holdsForNull && (axes.Optional.Contains(left) || axes.Optional.Contains(right))) {
                var trial = CloneEnv(env);
                RefineRelation(trial, left, right, relation);
                if (!ArmIsDead(env, trial)) AdoptArm(env, trial);
                return;
            }
            RefineRelation(env, left, right, relation, conflicts);
            return;
        }
        if (!TryGetComparisonBound(cmp, out var propName, out decimal value, out var bound)) return;
        if (!env.TryGetValue(propName, out var current)) return;
        if (negated) bound = Complement(bound);
        var integral = axes.Integral.Contains(propName);
        var narrowed = bound == ComparisonKind.NotEqual
            ? NarrowNotEqual(current, value, integral)
            : BoundConstraint(bound, value, integral) is { } narrowing ? current.Narrow(narrowing) : current;
        if (narrowed.Unsatisfiable && !current.Unsatisfiable && holdsForNull && axes.Optional.Contains(propName))
            return;
        env[propName] = narrowed;
    }

    /// <summary>
    /// <c>a or b</c>: each arm refines its own copy and the environment becomes their hull (lub), never one arm alone.
    /// The dead-arm and branch-join discipline of <see cref="JoinBranches"/>: an arm whose refinement turned a
    /// satisfiable symbol unsatisfiable cannot hold and is skipped; both dead leaves the incoming environment; a
    /// symbol an arm left as it came in admits every incoming value, so only symbols both arms changed are rewritten.
    /// The arms run with relations off (their callers pass <c>relations: false</c>), so no relation recorded inside an
    /// arm is kept or reported.
    /// </summary>
    private static void RefineDisjunction(
        Dictionary<string, AbstractValue> env,
        System.Action<Dictionary<string, AbstractValue>> refineLeft,
        System.Action<Dictionary<string, AbstractValue>> refineRight) {
        var left = CloneEnv(env);
        refineLeft(left);
        var right = CloneEnv(env);
        refineRight(right);
        var leftDead = ArmIsDead(env, left);
        var rightDead = ArmIsDead(env, right);
        if (leftDead && rightDead) return;
        if (leftDead) { AdoptArm(env, right); return; }
        if (rightDead) { AdoptArm(env, left); return; }
        foreach (var key in env.Keys.ToList()) {
            var incoming = env[key];
            if (!left.TryGetValue(key, out var fromLeft) || !right.TryGetValue(key, out var fromRight)) continue;
            if (ReferenceEquals(fromLeft, incoming) || ReferenceEquals(fromRight, incoming)) continue;
            env[key] = fromLeft.Join(fromRight);
        }
    }

    /// <summary>
    /// <c>x != v</c> excludes one value, which an interval can drop only at an endpoint: a point range at
    /// <paramref name="value"/> becomes empty on any axis; on an integral axis an endpoint equal to an integral
    /// <paramref name="value"/> moves inward by one. Anything else (an interior value, a decimal axis, a range
    /// open on that side) is not narrowed. <see cref="ValueRange.Above"/>/<see cref="ValueRange.Below"/> run only on
    /// the bounded receiver, where null means empty.
    /// </summary>
    private static AbstractValue NarrowNotEqual(AbstractValue current, decimal value, bool integral) {
        if (current.NumericRange is not { } range) return current;
        if (range.Min == value && range.Max == value) return current.AsUnsatisfiable();
        if (!integral || value != decimal.Truncate(value)) return current;
        ValueRange? tightened;
        if (range.Min == value) tightened = range.Above(value, integral: true);
        else if (range.Max == value) tightened = range.Below(value, integral: true);
        else return current;
        return tightened?.ToRangeConstraint() is { } narrowing ? current.Narrow(narrowing) : current.AsUnsatisfiable();
    }

    /// <summary>The comparison that holds exactly when <paramref name="kind"/> does not.</summary>
    private static ComparisonKind Complement(ComparisonKind kind) => kind switch {
        ComparisonKind.LessThan => ComparisonKind.GreaterThanOrEqual,
        ComparisonKind.LessThanOrEqual => ComparisonKind.GreaterThan,
        ComparisonKind.GreaterThan => ComparisonKind.LessThanOrEqual,
        ComparisonKind.GreaterThanOrEqual => ComparisonKind.LessThan,
        ComparisonKind.Equal => ComparisonKind.NotEqual,
        ComparisonKind.NotEqual => ComparisonKind.Equal,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "no complement for this comparison kind"),
    };

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

    /// <summary>
    /// The one-sided (or point) constraint a comparison against an exact bound implies. A strict bound goes through
    /// <see cref="ValueRange.Below"/>/<see cref="ValueRange.Above"/>: exact on an integral axis (<c>ceil(v) − 1</c>,
    /// <c>floor(v) + 1</c>), inclusive on any other, where the endpoint is the bound itself (F5, OQ-5: soundly
    /// over-approximate). The constraint carries CLR decimals (A1).
    /// </summary>
    private static RangeConstraint? BoundConstraint(ComparisonKind kind, decimal value, bool integral) => kind switch {
        ComparisonKind.LessThan => new ValueRange(null, null).Below(value, integral)?.ToRangeConstraint(),
        ComparisonKind.LessThanOrEqual => new RangeConstraint(null, value),
        ComparisonKind.GreaterThan => new ValueRange(null, null).Above(value, integral)?.ToRangeConstraint(),
        ComparisonKind.GreaterThanOrEqual => new RangeConstraint(value, null),
        ComparisonKind.Equal => new RangeConstraint(value, value),
        _ => null,
    };

    private static bool TryGetComparisonBound(
        Comparison cmp, out string propName, out decimal value, out ComparisonKind bound) {
        propName = "";
        value = 0;
        bound = default;
        if (cmp.Left is PropertyAccess pa && cmp.Right is Literal { Value: not null } lit) {
            propName = pa.Name;
            return DeclaredBound(lit.Value) is { } d && SetBound(cmp.Kind, d, out value, out bound);
        }
        if (cmp.Left is Literal { Value: not null } lit2 && cmp.Right is PropertyAccess pa2) {
            propName = pa2.Name;
            return DeclaredBound(lit2.Value) is { } d2 && SetBound(Flip(cmp.Kind), d2, out value, out bound);
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

    private static bool SetBound(ComparisonKind kind, decimal v, out decimal value, out ComparisonKind bound) {
        switch (kind) {
            case ComparisonKind.LessThan or ComparisonKind.LessThanOrEqual
                or ComparisonKind.GreaterThan or ComparisonKind.GreaterThanOrEqual
                or ComparisonKind.Equal or ComparisonKind.NotEqual:
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
                // The literal keeps its declared bound: a long stays long, a decimal stays decimal (A1).
                return DeclaredClrBound(lit.Value) is { } declared
                    ? AbstractValue.From([new RangeConstraint(declared, declared)])
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
                return EvalArithmetic(add, add.Left, add.Right,
                    operand => Eval(operand, entity, action, env, paramEnv, targetEntity, domain));
            case Subtract sub:
                return EvalArithmetic(sub, sub.Left, sub.Right,
                    operand => Eval(operand, entity, action, env, paramEnv, targetEntity, domain));
            case Multiply mul:
                return EvalArithmetic(mul, mul.Left, mul.Right,
                    operand => Eval(operand, entity, action, env, paramEnv, targetEntity, domain));
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

    /// <summary>
    /// The one arithmetic evaluator (A4, A10): both <see cref="Eval"/> and the for-each binding walk
    /// reach it. <c>X − X</c> is 0 when both operands are structurally equal and pure — checked before
    /// any range is looked up, so an operand with no range still cancels. Otherwise each operand's
    /// interval goes through <see cref="IntervalTransfers"/>, the only arithmetic path: an operand with
    /// no range is a null interval, an unregistered node a null result, and an interval that is open on
    /// both sides carries no information (Unknown), never a <c>range(−∞, +∞)</c>.
    /// </summary>
    private static AbstractValue EvalArithmetic(
        DomainExpression node, DomainExpression left, DomainExpression right,
        Func<DomainExpression, AbstractValue> evalOperand) {
        if (node is Subtract
            && !EffectAnalyzer.HasUnmodelledOperator(left) && !EffectAnalyzer.HasUnmodelledOperator(right)
            && DomainExpressionStructuralComparer.Equal(left, right))
            return AbstractValue.From([new RangeConstraint(0m, 0m)]);
        var operands = new[] { evalOperand(left).NumericRange, evalOperand(right).NumericRange };
        return IntervalTransfers.Transfer(node, operands)?.ToRangeConstraint() is { } range
            ? AbstractValue.From([range])
            : AbstractValue.Unknown;
    }

    /// <summary>A literal's numeric value exactly, or null when it is not a number or has no exact form.</summary>
    private static decimal? DeclaredBound(object? value) => ExactNumber.TryFrom(value)?.ToDecimal();

    /// <summary>A literal's declared bound as the CLR value a <see cref="RangeConstraint"/> keeps: integral
    /// numbers are <see cref="long"/>, everything else exact is <see cref="decimal"/>; never a
    /// <see cref="double"/> and never an <see cref="ExactNumber"/> (A1).</summary>
    private static object? DeclaredClrBound(object? value) => value switch {
        long or decimal => value,
        _ when DeclaredBound(value) is { } exact => exact == decimal.Truncate(exact) && exact is >= long.MinValue and <= long.MaxValue
            ? (long)exact
            : exact,
        _ => null,
    };

    // ── Effect stepping ─────────────────────────────────────────

    private static void ApplyEffects(
        IReadOnlyList<Effect> effects, Entity entity, Action action,
        Dictionary<string, AbstractValue> env,
        IReadOnlyDictionary<string, AbstractValue>? paramEnv,
        HashSet<Action> visited, List<EffectPostcondition> result, Domain domain,
        string? currentStage, DomainGuardAxes axes) {
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
                    Refine(thenEnv, cond.Condition, axes.For(entity));
                    var thenDead = ArmIsDead(env, thenEnv);
                    ApplyEffects(cond.ThenEffects, entity, action, thenEnv, paramEnv, visited, result, domain, currentStage, axes);
                    Dictionary<string, AbstractValue>? elseEnv = null;
                    var elseDead = false;
                    if (cond.ElseEffects is not null) {
                        elseEnv = CloneEnv(env);
                        RefineNegated(elseEnv, cond.Condition, axes.For(entity));
                        elseDead = ArmIsDead(env, elseEnv);
                        ApplyEffects(cond.ElseEffects, entity, action, elseEnv, paramEnv, visited, result, domain, currentStage, axes);
                    }
                    JoinBranches(env, thenEnv, thenDead, elseEnv, elseDead, entity);
                    break;
                case CompositeEffect composite:
                    ApplyEffects(composite.Effects, entity, action, env, paramEnv, visited, result, domain, currentStage, axes);
                    break;
                case InvokeActionEffect invoke:
                    ApplyInvoke(invoke, entity, action, env, paramEnv, visited, result, domain, currentStage, axes);
                    DropAllRelations(env);
                    break;
                case ForEachEffect fe:
                    // Zero or more iterations: assigns join into the env (Merge), so the
                    // pre-loop value still holds for zero iterations.
                    ApplyEffects(fe.Body, entity, action, env, paramEnv, visited, result, domain, currentStage, axes);
                    break;
                case ForEachInvokeEffect efe:
                    ApplyForEachInvoke(efe, entity, action, env, paramEnv, visited, result, domain, axes);
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
        string? currentStage, DomainGuardAxes axes) {
        if (invoke.TargetRelationship is not null) {
            ApplyCrossEntityInvoke(invoke, entity, action, env, paramEnv, visited, result, domain, axes);
            return;
        }

        var target = DomainAnalysis.FindAction(entity, invoke.ActionName, currentStage);
        if (target is null || !visited.Add(target)) return;

        var targetEnv = CloneEnv(env);
        foreach (var guard in PolicyGuardFacts.Guards(target, stage: null))
            PolicyGuardFacts.Refine(targetEnv, guard, entity, args: null, instantiate: null, axes.For(entity));

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
            ApplyEffects(target.Effects, entity, target, targetEnv, targetParams, visited, result, domain, currentStage, axes);
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
        HashSet<Action> visited, List<EffectPostcondition> result, Domain domain, DomainGuardAxes axes) {
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
        foreach (var guard in PolicyGuardFacts.Guards(targetAction, stage: null))
            PolicyGuardFacts.Refine(targetEnv, guard, targetEntity, args: null, instantiate: null, axes.For(targetEntity));

        Dictionary<string, AbstractValue>? targetParams = null;
        if (invoke.ParameterBindings.Count > 0) {
            targetParams = new(StringComparer.Ordinal);
            foreach (var binding in invoke.ParameterBindings)
                targetParams[binding.PropertyName] = Eval(binding.Expression, entity, action, env, paramEnv, domain: domain);
        }

        try {
            ApplyEffects(targetAction.Effects, targetEntity, targetAction, targetEnv, targetParams, visited, result, domain, currentStage: null, axes);
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
        HashSet<Action> visited, List<EffectPostcondition> result, Domain domain, DomainGuardAxes axes) {
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
        foreach (var guard in PolicyGuardFacts.Guards(targetAction, stage: null))
            PolicyGuardFacts.Refine(targetEnv, guard, targetEntity, args: null, instantiate: null, axes.For(targetEntity));
        if (efe.Predicate is ForEachNamedPolicy { PolicyName: var policyName }) {
            var predicatePolicy = targetEntity.Policies.FirstOrDefault(p =>
                string.Equals(p.Name, policyName, StringComparison.Ordinal));
            if (predicatePolicy is not null)
                PolicyGuardFacts.Refine(targetEnv, new GuardedPolicy(predicatePolicy, Negated: false), targetEntity,
                    args: null, instantiate: null, axes.For(targetEntity));
        }

        Dictionary<string, AbstractValue>? targetParams = null;
        if (efe.ParameterBindings.Count > 0) {
            targetParams = new(StringComparer.Ordinal);
            foreach (var binding in efe.ParameterBindings)
                targetParams[binding.PropertyName] = EvalForEachBinding(
                    binding.Expression, efe.BinderName, targetEntity, entity, action, env, paramEnv, domain);
        }

        try {
            ApplyEffects(targetAction.Effects, targetEntity, targetAction, targetEnv, targetParams, visited, result, domain, currentStage: null, axes);
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
                return EvalArithmetic(add, add.Left, add.Right,
                    operand => EvalForEachBinding(operand, binder, iterated, caller, action, env, paramEnv, domain));
            case Subtract sub:
                return EvalArithmetic(sub, sub.Left, sub.Right,
                    operand => EvalForEachBinding(operand, binder, iterated, caller, action, env, paramEnv, domain));
            case Multiply mul:
                return EvalArithmetic(mul, mul.Left, mul.Right,
                    operand => EvalForEachBinding(operand, binder, iterated, caller, action, env, paramEnv, domain));
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

    internal static (decimal? Min, decimal? Max) InferNumericRange(
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