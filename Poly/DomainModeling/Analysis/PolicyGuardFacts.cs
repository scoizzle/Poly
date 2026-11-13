using Poly.DomainModeling.Ontology;

using Action = Poly.DomainModeling.Ontology.Action;

namespace Poly.DomainModeling.Analysis;

/// <summary>One guard an action runs under: a <c>require</c>'d policy, <see cref="Negated"/> for <c>require not</c>
/// (the resolver's <c>not_P</c> marker, which the runtime reads by name).</summary>
internal readonly record struct GuardedPolicy(Policy Policy, bool Negated);

/// <summary>Substitutes a parameterised policy's arguments into its expression (F-09 supplies it).</summary>
internal delegate DomainExpression PolicyInstantiator(Policy policy, IReadOnlyList<DomainExpression> args);

/// <summary>
/// The one seam through which a <c>require</c>'d policy narrows the abstract environment (V-18): the guards an
/// action runs under in one stage context (<see cref="Guards"/>), and the refinement each one implies
/// (<see cref="Refine"/>). <see cref="EffectInvariantAnalyzer"/> calls it at action entry, on a self-invoke and on a
/// cross-entity or fan-out invoke; F-09 adds only the <see cref="PolicyInstantiator"/> and the argument list.
/// </summary>
internal static class PolicyGuardFacts {
    /// <summary>The name prefix of the resolver's <c>require not P</c> marker policy (<c>not_P</c>).</summary>
    internal const string NegatedPrefix = "not_";

    /// <summary>
    /// The guards <paramref name="action"/> runs under in <paramref name="stage"/>'s context, as the lowered body
    /// checks them: the action's own policies (<c>require not P</c> included), then the stage's, except a stage
    /// policy <c>P</c> the action negates (<c>require not P</c>), whose gate the body suppresses
    /// (<c>DomainToCSharpExporter.EmitStagePolicyGuards</c>; <c>ProvableGuards.StagePresence</c> mirrors it).
    /// </summary>
    public static IReadOnlyList<GuardedPolicy> Guards(Action action, Stage? stage) =>
        action.Policies
            .Concat((stage?.Policies ?? []).Where(p => !SuppressesStageGate(action, p)))
            .Select(p => new GuardedPolicy(p, p.Name.StartsWith(NegatedPrefix, StringComparison.Ordinal)))
            .ToList();

    /// <summary>
    /// True when <paramref name="action"/>'s <c>require not P</c> lifts its stage's gate <paramref name="stagePolicy"/>
    /// (<c>P</c>): the one rule the lowered body (<c>DomainToCSharpExporter.EmitStagePolicyGuards</c>), the
    /// stage-presence facts (<c>ProvableGuards.StagePresence</c>) and <see cref="Guards"/> all read.
    /// </summary>
    public static bool SuppressesStageGate(Action action, Policy stagePolicy) =>
        action.Policies.Any(p => string.Equals(p.Name, NegatedPrefix + stagePolicy.Name, StringComparison.Ordinal));

    /// <summary>The guards analysis narrowed by before V-18, the public precondition set
    /// (<c>EffectInvariantAnalyzer.CollectPreconditions</c>): the action's policies except its <c>require not</c>
    /// markers, then every stage policy. Read only where the walk no longer models the instance.</summary>
    public static IReadOnlyList<GuardedPolicy> PreconditionGuards(Action action, Stage? stage) =>
        action.Policies
            .Where(p => !p.Name.StartsWith(NegatedPrefix, StringComparison.Ordinal))
            .Concat(stage?.Policies ?? [])
            .Select(p => new GuardedPolicy(p, p.Name.StartsWith(NegatedPrefix, StringComparison.Ordinal)))
            .ToList();

    /// <summary>
    /// Narrows <paramref name="env"/> by one guard, under <see cref="EffectInvariantAnalyzer.Refine"/>'s soundness
    /// contract. A negated guard narrows by the negation of <paramref name="owner"/>'s policy of that name, as the
    /// runtime evaluates it; the marker's own expression is never read, so <c>Not</c> wraps the policy's expression
    /// exactly once. An owner with no such policy narrows nothing. <paramref name="args"/> (F-09's
    /// <c>require P(args)</c>) are substituted by <paramref name="instantiate"/>; arguments with no instantiator throw.
    /// </summary>
    public static void Refine(
        Dictionary<string, AbstractValue> env, GuardedPolicy guard, Entity owner,
        IReadOnlyList<DomainExpression>? args, PolicyInstantiator? instantiate, EffectInvariantAnalyzer.GuardAxes axes,
        List<EffectInvariantAnalyzer.RelationConflict>? conflicts = null, bool relations = true) {
        var policy = guard.Negated ? NegatedTarget(guard.Policy, owner) : guard.Policy;
        if (policy is null) return;
        var condition = args is null ? policy.Expression : Instantiate(policy, owner, args, instantiate);
        if (guard.Negated)
            EffectInvariantAnalyzer.RefineNegated(env, condition, axes, conflicts, relations);
        else
            EffectInvariantAnalyzer.Refine(env, condition, axes, conflicts, relations);
    }

    /// <summary>The entity policy a <c>not_P</c> marker negates, found by name as the runtime finds it; null when the
    /// owner declares none (an API-built marker), which narrows nothing.</summary>
    private static Policy? NegatedTarget(Policy marker, Entity owner) {
        if (!marker.Name.StartsWith(NegatedPrefix, StringComparison.Ordinal)) return null;
        var name = marker.Name[NegatedPrefix.Length..];
        return owner.Policies.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.Ordinal));
    }

    private static DomainExpression Instantiate(
        Policy policy, Entity owner, IReadOnlyList<DomainExpression> args, PolicyInstantiator? instantiate) =>
        instantiate is null
            ? throw new InvalidOperationException(
                $"Policy '{policy.Name}' on entity '{owner.Name}' is required with {args.Count} argument(s), but no " +
                "policy instantiator was supplied to substitute them, so analysis cannot narrow by it.")
            : instantiate(policy, args);
}