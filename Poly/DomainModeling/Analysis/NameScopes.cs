using Poly.DomainModeling.Meaning;
using Poly.DomainModeling.Ontology;

using Action = Poly.DomainModeling.Ontology.Action;

namespace Poly.DomainModeling.Analysis;

/// <summary>Which list of an entity a scoped member comes from.</summary>
internal enum MemberKind {
    Property,
    Navigation,
    Action,
    Policy,
}

/// <summary>
/// One member of an entity's generated C# surface: the authored name, the identifier the
/// emitter derives for the member itself, and the identifier it derives for the member's
/// constructor parameter (null when the member has none).
/// </summary>
internal sealed record ScopeEntry(
    string Name,
    string MemberIdentity,
    string? ParameterIdentity,
    MemberKind Kind,
    DomainMember Node);

/// <summary>
/// The entity member scope table (S-01): the members that share one generated C# type, with
/// the identity the emitter gives each. Identity per kind, matching the emitter:
/// a <b>property</b> keeps its name (<c>Properties.cs:110</c>) and its constructor parameter is
/// <c>ToCamelCase</c> (<c>Properties.cs:124</c>), except an entry-assigned property, which gets
/// no parameter (<c>Properties.cs:127</c>); a <b>navigation</b>'s member is
/// <c>ToPascalCase</c> (<c>Relationships.cs:143</c>) and its parameter <c>ToCamelCase</c> of that
/// (<c>Relationships.cs:144</c>); an <b>action</b> (<c>Actions.cs:69</c>) and a <b>policy</b>
/// (<c>Invariants.cs:68-77</c>) keep their names. Only <see cref="ForEntity"/> ships; a domain
/// scope and an enum scope arrive with their first consumer (S-02/L-21).
/// </summary>
internal static class NameScopes {
    /// <summary>
    /// Every member of <paramref name="entity"/> that lands on its generated type, in report
    /// order: properties, navigations, actions (one entry per distinct name — same-name actions
    /// across stages are one method), then policy definitions.
    /// </summary>
    public static IReadOnlyList<ScopeEntry> ForEntity(Entity entity) {
        var entryAssigned = EntityStructureAnalyzer.ComputeEntryAssignedPropertyNames(entity);
        var entries = new List<ScopeEntry>();

        foreach (var prop in entity.Properties) {
            entries.Add(new ScopeEntry(
                prop.Name,
                prop.Name,
                entryAssigned.Contains(prop.Name) ? null : DomainTypeMapping.ToCamelCase(prop.Name),
                MemberKind.Property,
                prop));
        }

        foreach (var nav in entity.Navigations) {
            var pascalName = DomainTypeMapping.ToPascalCase(nav.Name);
            entries.Add(new ScopeEntry(
                nav.Name,
                pascalName,
                DomainTypeMapping.ToCamelCase(pascalName),
                MemberKind.Navigation,
                nav));
        }

        foreach (var group in AllActions(entity).GroupBy(static a => a.Name, StringComparer.Ordinal)) {
            var action = group.First();
            entries.Add(new ScopeEntry(action.Name, action.Name, null, MemberKind.Action, action));
        }

        foreach (var policy in PolicyDefinitions(entity))
            entries.Add(new ScopeEntry(policy.Name, policy.Name, null, MemberKind.Policy, policy));

        return entries;
    }

    private static IEnumerable<Action> AllActions(Entity entity) =>
        entity.Actions.Concat(entity.Stages.SelectMany(static stage => stage.Actions));

    /// <summary>
    /// Policy definitions in the order <c>StructuralDomainAnalyzer.ReportPolicyNameClashes</c>
    /// walks them: a <c>not_</c> name, and an expression instance equal to an earlier
    /// definition, are uses rather than new names. Kept in lockstep with that walk.
    /// </summary>
    private static IEnumerable<Policy> PolicyDefinitions(Entity entity) {
        var defined = new Dictionary<string, Policy>(StringComparer.Ordinal);

        bool IsUse(Policy policy) =>
            policy.Name.StartsWith("not_", StringComparison.Ordinal)
            || (defined.TryGetValue(policy.Name, out var existing)
                && ReferenceEquals(existing.Expression, policy.Expression));

        void Define(Policy policy) {
            if (!defined.ContainsKey(policy.Name))
                defined.Add(policy.Name, policy);
        }

        foreach (var policy in entity.Policies) {
            Define(policy);
            yield return policy;
        }
        foreach (var stage in entity.Stages) {
            foreach (var policy in stage.Policies) {
                if (IsUse(policy))
                    continue;
                Define(policy);
                yield return policy;
            }
        }
        foreach (var action in AllActions(entity)) {
            foreach (var policy in action.Policies) {
                if (IsUse(policy))
                    continue;
                Define(policy);
                yield return policy;
            }
        }
    }
}