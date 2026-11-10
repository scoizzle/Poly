using Poly.DomainModeling.Ontology;

using Action = Poly.DomainModeling.Ontology.Action;

namespace Poly.DomainModeling.Analysis;

/// <summary>
/// The action-parameter implicit-constraint envelope (#367 C-2): per action, per parameter, the
/// constraints the parameter must satisfy because of where it unconditionally flows. Published on the
/// <see cref="Action"/> node by <see cref="ActionParameterEnvelopePass"/>; read through
/// <see cref="ActionParameterEnvelopeExtensions.GetActionParameterEnvelope"/>.
/// <para>
/// This is the transport boundary's envelope, deliberately NOT the downstream-constraint fact of
/// <c>ConstraintPropagationAnalyzer</c> (which also follows conditionals and invoke chains for
/// diagnostics). The shapes it covers, and does not:
/// </para>
/// <list type="bullet">
///   <item>Only UNCONDITIONAL effects contribute (top level and nested sequential blocks). A flow that
///     appears only inside an <c>if</c>/<c>else</c> branch has no universally provable envelope and
///     intersecting the branches would falsely reject valid inputs, so it contributes nothing.</item>
///   <item>A flow is a bare reference to the parameter: <c>assign Prop to param</c> on the owning entity, or a
///     <c>create</c>/<c>create in</c> initializer <c>Prop: param</c> on the created entity. Any derived value
///     (<c>param + 3</c>), and <c>invoke</c> chains, contribute nothing.</item>
///   <item>Each target contributes its effective constraints — the analysis-verified range when proven, else the
///     declared range; declared length / pattern / equality; and <c>Required</c> when the property is required —
///     merged per constraint type by intersection. A type that merges to nothing (conflicting patterns, disjoint
///     ranges) is dropped. All assign flows are merged first, then all create flows, each in effect order.</item>
/// </list>
/// </summary>
public sealed record ActionParameterEnvelopeMetadata(
    IReadOnlyDictionary<string, IReadOnlyList<Constraint>> ByParameter) : IAnalysisMetadata;

public static class ActionParameterEnvelopeExtensions {
    /// <summary>
    /// The implicit constraints of <paramref name="parameterName"/> of <paramref name="action"/>, in merge
    /// order; empty when the parameter reaches no constrained target. Null when the envelope pass did not run.
    /// </summary>
    public static IReadOnlyList<Constraint>? GetActionParameterEnvelope(
        this INodeMetadataProvider analysis, Action action, string parameterName) {
        var meta = analysis.GetMetadata<ActionParameterEnvelopeMetadata>(action);
        if (meta is null) return null;
        return meta.ByParameter.TryGetValue(parameterName, out var constraints) ? constraints : [];
    }
}

internal sealed class ActionParameterEnvelopePass : INodeAnalyzer {
    public const string Id = "DomainActionParameterEnvelopePass";
    public string PassName => Id;

    public void Analyze(AnalysisContext context, Node node) {
        if (node is Domain domain) {
            AnalyzeDomain(context, domain);
            return;
        }

        this.AnalyzeChildren(context, node);
    }

    private static void AnalyzeDomain(AnalysisContext context, Domain domain) {
        var lookup = context.GetTypeLookup(domain);
        if (lookup is null) return;

        var entities = lookup.Entities.ToList();
        // First declaration of a name wins (a duplicate is a structural error reported elsewhere).
        var verified = new Dictionary<string, Dictionary<string, (ValueRange? Range, bool Verified)>>(StringComparer.Ordinal);
        foreach (var e in entities)
            if (!verified.ContainsKey(e.Name))
                verified[e.Name] = VerifiedRanges.Compute(e, a => context.GetMetadata<ActionInvariantMetadata>(a));

        foreach (var entity in entities)
            DomainAnalysis.ForEachAction(entity, action => {
                if (action.Parameters.Count == 0) return;
                var byParameter = new Dictionary<string, IReadOnlyList<Constraint>>(StringComparer.Ordinal);
                foreach (var parameter in action.Parameters)
                    byParameter[parameter.Name] = ComputeEnvelope(context, lookup, entities, verified, entity, action, parameter.Name);
                context.SetMetadata(action, new ActionParameterEnvelopeMetadata(byParameter));
            });
    }

    private static List<Constraint> ComputeEnvelope(
        AnalysisContext context,
        DomainTypeLookupMetadata lookup,
        List<Entity> entities,
        Dictionary<string, Dictionary<string, (ValueRange? Range, bool Verified)>> verified,
        Entity entity,
        Action action,
        string parameterName) {
        var merged = new List<Constraint>();
        var unconditional = FlattenUnconditionalEffects(action.Effects).ToList();

        foreach (var assign in unconditional.OfType<AssignEffect>()) {
            // In the domain model a parameter reference in an effect value is still a
            // PropertyAccess/ParameterAccess whose name equals the action parameter.
            var valueName = ValueNameOf(assign.Value);
            if (valueName is null || !string.Equals(valueName, parameterName, StringComparison.Ordinal))
                continue;
            if (assign.Target is not PropertyAccess target) continue;
            MergeTargetConstraints(context, lookup, verified, merged, entity, target.Name);
        }

        foreach (var create in unconditional.Where(e => e is CreateEntityInstance or CreateEntityInRelationshipEffect)) {
            var (targetEntityName, initializers) = create switch {
                CreateEntityInstance cei => (cei.Type.TypeName, cei.Initializers),
                CreateEntityInRelationshipEffect cer => (
                    cer.ResolvedTargetType?.TypeName ?? ResolveRelationshipTarget(entity, cer.RelationshipName),
                    cer.Initializers),
                _ => (null, null!),
            };
            if (targetEntityName is null) continue;
            var targetEntity = entities.FirstOrDefault(e =>
                string.Equals(e.Name, targetEntityName, StringComparison.Ordinal));
            if (targetEntity is null) continue;
            foreach (var binding in initializers) {
                var valueName = ValueNameOf(binding.Expression);
                if (valueName is null || !string.Equals(valueName, parameterName, StringComparison.Ordinal))
                    continue;
                MergeTargetConstraints(context, lookup, verified, merged, targetEntity, binding.PropertyName);
            }
        }

        return merged;
    }

    private static string? ValueNameOf(DomainExpression expression) => expression switch {
        PropertyAccess pa => pa.Name,
        ParameterAccess pa => pa.Name,
        _ => null,
    };

    private static void MergeTargetConstraints(
        AnalysisContext context,
        DomainTypeLookupMetadata lookup,
        Dictionary<string, Dictionary<string, (ValueRange? Range, bool Verified)>> verified,
        List<Constraint> merged,
        Entity owner,
        string propertyName) {
        foreach (var constraint in EffectivePropertyConstraints(context, lookup, verified, owner, propertyName)) {
            var existingIndex = merged.FindIndex(m => m.GetType() == constraint.GetType());
            if (existingIndex < 0) {
                merged.Add(constraint);
                continue;
            }
            var net = merged[existingIndex].Merge(constraint);
            if (net is null) merged.RemoveAt(existingIndex);
            else merged[existingIndex] = net;
        }
    }

    /// <summary>The effective constraints for a property: its analysis-verified-or-declared range, the
    /// declared length/pattern/equality constraints, and requiredness. A property is required unless it is
    /// marked <c>optional</c>.</summary>
    private static List<Constraint> EffectivePropertyConstraints(
        AnalysisContext context,
        DomainTypeLookupMetadata lookup,
        Dictionary<string, Dictionary<string, (ValueRange? Range, bool Verified)>> verified,
        Entity owner,
        string propertyName) {
        var result = new List<Constraint>();
        var prop = owner.Properties.FirstOrDefault(p =>
            string.Equals(p.Name, propertyName, StringComparison.Ordinal));
        var range = PropertyRange(lookup, verified, owner, prop, propertyName);
        if (range is not null) result.Add(range);
        if (prop is not null)
            result.AddRange(prop.Constraints.Where(c =>
                c is LengthConstraint or PatternConstraint or RequiredConstraint or EqualityConstraint));
        if (context.GetStructure(owner) is { } structure
            && structure.RequiredPropertyNames.Contains(propertyName)
            && result.All(c => c is not RequiredConstraint))
            result.Insert(0, new RequiredConstraint());
        return result;
    }

    /// <summary>The verified envelope when the invariant analysis proved no writer can leave the declared
    /// range (a stored, non-entity-typed property), else the declared range.</summary>
    private static RangeConstraint? PropertyRange(
        DomainTypeLookupMetadata lookup,
        Dictionary<string, Dictionary<string, (ValueRange? Range, bool Verified)>> verified,
        Entity owner,
        Property? prop,
        string propertyName) {
        var isStoredColumn = prop is not null
            && !(lookup.Types.TryGetValue(prop.Type.TypeName, out var type) && type is Entity);
        if (isStoredColumn
            && verified.TryGetValue(owner.Name, out var ranges)
            && ranges.TryGetValue(propertyName, out var entry)
            && entry is { Verified: true, Range: { } range })
            return new RangeConstraint(range.Min, range.Max);
        return prop?.Constraints.OfType<RangeConstraint>().FirstOrDefault();
    }

    private static string? ResolveRelationshipTarget(Entity source, string relationshipName) =>
        source.Navigations.FirstOrDefault(n => string.Equals(n.Name, relationshipName, StringComparison.Ordinal))
            ?.Target.TypeName;

    /// <summary>Flattens a composite (sequential) effect tree WITHOUT descending into conditional
    /// branches — only flows that execute on every path.</summary>
    private static IEnumerable<Effect> FlattenUnconditionalEffects(IEnumerable<Effect> effects) {
        foreach (var effect in effects) {
            yield return effect;
            if (effect is CompositeEffect composite) {
                foreach (var nested in FlattenUnconditionalEffects(composite.Effects)) yield return nested;
            }
        }
    }
}