using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Ontology;
using Poly.Runtime;

using AccessModifier = Poly.Introspection.AccessModifier;
using PrimType = Poly.Introspection.PrimitiveType;
using Syntactic = Poly.Ast.Nodes;

namespace Poly.DomainModeling.Lowering;

// Invariants: policy methods, unique-property invariants, and entity-level guard methods.
public sealed partial class DomainToCSharpExporter {
    /// <summary>
    /// Assign of a unique property calls <c>this.EnsureUnique</c>. The same value
    /// already on this instance is Success. A different value fails closed: this method holds
    /// no storage port. The storage library rewrites every EnsureUnique-then-assign into
    /// <c>db.Exists</c>/<c>Release</c>/<c>Record</c> on the <c>IStoragePort</c> the action receives,
    /// so a module compiled with storage never reaches this failure.
    /// </summary>
    private static MethodDefinitionNode EnsureUniqueAssignMethod(Entity entity) {
        var propertyName = new Parameter("propertyName", new PrimitiveTypeReference(PrimType.String));
        var value = new Parameter("value", StoreJobObjectType());
        var nodes = new List<Node> {
            new IfStatement(
                new Equal(value, new Constant(null)),
                new Block([DomainResultSuccess()]))
        };
        foreach (var prop in entity.Properties) {
            if (!prop.Constraints.OfType<UniqueConstraint>().Any())
                continue;
            nodes.Add(new IfStatement(
                new Syntactic.And(
                    new Equal(propertyName, new Constant(prop.Name)),
                    new Invoke(
                        new Member(new ClrTypeReference(typeof(object)), "Equals"),
                        new Member(new ThisReference(), prop.Name),
                        value)),
                new Block([DomainResultSuccess()])));
        }
        nodes.Add(FailureLowering.Return(new NamedTypeReference("DomainResult"), new FailureLowering.Spec(
            DomainFailureKind.Constraint, new Constant("A unique assign needs the storage port."), entity.Name)));
        return new MethodDefinitionNode(
            "EnsureUnique",
            new NamedTypeReference("DomainResult"),
            Parameters: [propertyName, value],
            Body: new Block(nodes),
            AccessModifier: AccessModifier.Public);
    }

    private static Return DomainResultSuccess() =>
        new(new Invoke(new Member(new NamedTypeReference("DomainResult"), "Success")));

    /// <summary>
    /// Emits one bool method per entity, stage, and action policy. A require copy
    /// (same expression instance as the definition) and a <c>not_</c> use do not emit a
    /// second method; any other name clash throws (analysis reports it first).
    /// </summary>
    private static void AddPolicyMethods(
        Entity entity, Domain domain, INodeMetadataProvider metadata,
        List<MethodDefinitionNode> methods,
        Dictionary<(string Entity, string Policy), Node>? policyBodies) {
        // ── Policies as bool methods ──────────────────────────────
        // One unqualified method per policy name. Entity, stage, and action-local
        // definitions share that namespace with the action methods. A require copy
        // (same expression instance as the definition) and a not_ use do not emit a
        // second method, whether the require sits on a stage or an action. Any other
        // name clash throws (analysis reports it first).
        var definedPolicies = new Dictionary<string, Policy>(StringComparer.Ordinal);
        void AddPolicyMethod(Policy policy) {
            if (methods.Exists(m => string.Equals(m.Name, policy.Name, StringComparison.Ordinal)))
                throw new InvalidOperationException(
                    $"Policy '{policy.Name}' is already defined on entity '{entity.Name}'.");
            definedPolicies.Add(policy.Name, policy);
            var body = LowerExpressionToMethodBody(policy.Expression, entity, domain, analysis: metadata);
            if (body is not null)
                policyBodies?.Add((entity.Name, policy.Name), body);
            methods.Add(new MethodDefinitionNode(
                policy.Name,
                new PrimitiveTypeReference(PrimType.Boolean),
                Body: body,
                AccessModifier: AccessModifier.Public
            ));
        }

        bool IsRequireUse(Policy policy) =>
            policy.Name.StartsWith("not_", StringComparison.Ordinal)
            || (definedPolicies.TryGetValue(policy.Name, out var existing)
                && ReferenceEquals(existing.Expression, policy.Expression));

        foreach (var policy in entity.Policies)
            AddPolicyMethod(policy);
        foreach (var stage in entity.Stages)
            foreach (var policy in stage.Policies)
                if (!IsRequireUse(policy))
                    AddPolicyMethod(policy);
        foreach (var action in entity.Actions.Concat(entity.Stages.SelectMany(s => s.Actions)))
            foreach (var policy in action.Policies)
                if (!IsRequireUse(policy))
                    AddPolicyMethod(policy);
    }
}