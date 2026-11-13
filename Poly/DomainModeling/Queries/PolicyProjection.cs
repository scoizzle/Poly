using Poly.DomainModeling.Ontology;

using Action = Poly.DomainModeling.Ontology.Action;
using Entity = Poly.DomainModeling.Ontology.Entity;

namespace Poly.DomainModeling.Queries;

/// <summary>
/// One policy as a document row. <paramref name="Scope"/> is <c>entity</c>, <c>stage:&lt;S&gt;</c>,
/// <c>action:&lt;A&gt;</c> for an entity action or <c>action:&lt;S&gt;.&lt;A&gt;</c> for an action inside a stage.
/// <paramref name="ParameterText"/> stays null until parameterised policies ship (Q18).
/// </summary>
internal readonly record struct PolicyRow(string Scope, string Name, string ConditionText, string? ParameterText = null);

/// <summary>
/// The entity, stage and action policies of one entity as rows, in declaration order (one walk of
/// <see cref="EntityDeclarationOrder.Members"/>), with each condition printed by the export's printer.
/// The printer is lazy: it is resolved only when a policy is printed, so a policy-free domain never needs a session.
/// </summary>
internal static class PolicyProjection {
    public static IReadOnlyList<PolicyRow> For(Entity entity, Lazy<DomainDslPrinter> printer) {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(printer);

        var rows = new List<PolicyRow>();
        foreach (var member in EntityDeclarationOrder.Members(entity)) {
            switch (member.Node) {
                case Policy policy:
                    rows.Add(Row(entity, policy, "entity", printer));
                    break;
                case Stage stage:
                    foreach (var policy in stage.Policies)
                        rows.Add(Row(entity, policy, $"stage:{stage.Name}", printer));
                    foreach (var action in stage.Actions)
                        foreach (var policy in action.Policies)
                            rows.Add(Row(entity, policy, $"action:{stage.Name}.{action.Name}", printer));
                    break;
                case Action action:
                    foreach (var policy in action.Policies)
                        rows.Add(Row(entity, policy, $"action:{action.Name}", printer));
                    break;
            }
        }
        return rows;
    }

    private static PolicyRow Row(Entity entity, Policy policy, string scope, Lazy<DomainDslPrinter> printer) {
        try {
            return new PolicyRow(scope, policy.Name, printer.Value.PrintExpressionText(policy.Expression));
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException) {
            throw new InvalidOperationException(
                $"Cannot print the condition of policy {entity.Name}.{policy.Name} ({scope}): {ex.Message}", ex);
        }
    }
}
