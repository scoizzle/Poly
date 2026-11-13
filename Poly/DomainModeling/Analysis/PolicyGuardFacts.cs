using Poly.DomainModeling.Ontology;

using Action = Poly.DomainModeling.Ontology.Action;

namespace Poly.DomainModeling.Analysis;

/// <summary>One guard an action runs under: a <c>require</c>'d policy, <see cref="Negated"/> for <c>require not</c>.</summary>
internal readonly record struct GuardedPolicy(Policy Policy, bool Negated);

/// <summary>Substitutes a parameterised policy's arguments into its expression (F-09 supplies it).</summary>
internal delegate DomainExpression PolicyInstantiator(Policy policy, IReadOnlyList<DomainExpression> args);

/// <summary>The one seam through which a <c>require</c>'d policy narrows the abstract environment (V-18).</summary>
internal static class PolicyGuardFacts {
    public static IReadOnlyList<GuardedPolicy> Guards(Action action, Entity owner, Stage? stage) => [];

    public static void Refine(
        Dictionary<string, AbstractValue> env, GuardedPolicy guard, Entity owner,
        IReadOnlyList<DomainExpression>? args, PolicyInstantiator? instantiate, EffectInvariantAnalyzer.GuardAxes axes,
        List<EffectInvariantAnalyzer.RelationConflict>? conflicts = null, bool relations = true) { }
}