using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Ontology.Contract;

using Action = Poly.DomainModeling.Ontology.Action;
using ValueType = Poly.DomainModeling.Ontology.ValueType;

namespace Poly.Tests.DomainModeling.Parsing;

/// <summary>
/// Short constructors for code-built domains, shared by the print baseline's built cases and the round-trip matrix.
/// Test-only. A property is required unless marked optional, and the parser records no constraint for that, so
/// <see cref="Prop"/> adds none: a built domain then has the shape the parser produces.
/// </summary>
internal static class ModelKit {
    internal static DomainTypeReference Ref(string name) => new(name);

    internal static Property Prop(string name, string type = "Text", params Constraint[] constraints) =>
        new(name, Ref(type), constraints);

    internal static Entity Ent(
        string name,
        IReadOnlyList<Property>? props = null,
        IReadOnlyList<Action>? actions = null,
        IReadOnlyList<Policy>? policies = null,
        IReadOnlyList<Stage>? stages = null) =>
        new(name, props ?? [], actions ?? [], policies ?? [], stages ?? []);

    internal static Action Act(string name, params Effect[] effects) =>
        new(name, InvocationResult.Void, [], effects, []);

    internal static Stage Stg(string name, IReadOnlyList<Action>? actions = null) =>
        new(name, actions ?? [], [], [], []);

    internal static Policy Pol(string name, DomainExpression expression) => new(name, expression);

    internal static Relationship Nav(string source, string name, string target, RelationshipCardinality cardinality, bool owned = false, string? via = null) =>
        new(name, Ref(source), Ref(target), cardinality, []) { SourceOwnsTarget = owned, Inverse = via };

    internal static StageTransitionEffect To(string stage) => new(new StageReference(stage));

    internal static AssignEffect Assign(string property, DomainExpression value) =>
        new(DomainExpression.Property(property), value);

    internal static DomainExpression Num(long value) => DomainExpression.Literal(value);

    internal static DomainExpression Prp(string name) => DomainExpression.Property(name);

    internal static Domain Dom(params DomainType[] types) => new("Matrix", types);

    internal static Domain DomWith(IReadOnlyList<string> extensions, params DomainType[] types) =>
        new("Matrix", types) { Extensions = extensions };

    internal static ValueType Val(string name, IReadOnlyList<Property> props, Visibility visibility = Visibility.Internal) =>
        new(name, props, []) { Visibility = visibility };

    internal static ImportedContract Contract(
        string name, ContractSourceKind kind, string source, string version, IReadOnlyList<ContractEndpoint> endpoints, IReadOnlyList<ValueType>? types = null) =>
        new(name, kind, source, version, endpoints) { Types = types ?? [] };
}
