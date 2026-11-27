using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Ontology;
using Poly.Runtime;

using Action = Poly.DomainModeling.Ontology.Action;
using ValueType = Poly.DomainModeling.Ontology.ValueType;

namespace Poly.DomainModeling.Lowering;

/// <summary>
/// The action-boundary guards for a value-type payload (#612, #571). A value type is a contract
/// payload or action parameter with no factory of its own, so its member constraints are checked
/// where the parameter enters the action. The checks come from <see cref="ConstraintCheckWriter"/>,
/// the same writer the create factory and the assign composer use, so both hosts refuse the same
/// payload with the same Range/Length/Pattern failure. A null payload is a Required failure, never
/// an NRE; a nested value-type member is checked recursively.
/// </summary>
internal static class ValuePayloadGuardWriter {
    internal static IReadOnlyList<Node> Guards(
        Entity entity, Action action, Domain domain, INodeMetadataProvider analysis, Node actionResultType) {
        var guards = new List<Node>();
        foreach (var parameter in action.Parameters) {
            if (ValueTypeOf(domain, parameter.Type.TypeName) is not { } valueType)
                continue;
            var required = !parameter.Constraints.OfType<OptionalConstraint>().Any();
            var value = (Node)new Parameter(parameter.Name);
            var path = $"{entity.Name}.{action.Name}";
            if (required)
                guards.Add(RequiredGuard(value, parameter.Name, path, actionResultType));
            var memberGuards = MemberGuards(valueType, value, domain, analysis, actionResultType,
                new HashSet<string>(StringComparer.Ordinal) { valueType.Name });
            if (required) {
                guards.AddRange(memberGuards);
                continue;
            }
            // An optional payload is checked only when it is present; an absent one is fine.
            if (memberGuards.Count > 0)
                guards.Add(new IfStatement(
                    new NotEqual(value, new Constant(null)),
                    new Block(memberGuards)));
        }
        return guards;
    }

    private static List<Node> MemberGuards(
        ValueType valueType, Node value, Domain domain, INodeMetadataProvider analysis, Node actionResultType,
        HashSet<string> nesting) {
        var guards = new List<Node>();
        foreach (var prop in valueType.Properties) {
            var required = !prop.Constraints.OfType<OptionalConstraint>().Any();
            var access = (Node)new Member(value, prop.Name);
            var path = $"{valueType.Name}.{prop.Name}";
            if (ValueTypeOf(domain, prop.Type.TypeName) is { } nested) {
                if (!nesting.Add(nested.Name))
                    throw new NotSupportedException(
                        $"Value type '{nested.Name}' contains itself, so its payload constraints cannot be lowered soundly.");
                if (required)
                    guards.Add(RequiredGuard(access, prop.Name, path, actionResultType));
                var nestedGuards = MemberGuards(nested, access, domain, analysis, actionResultType, nesting);
                nesting.Remove(nested.Name);
                if (required) {
                    guards.AddRange(nestedGuards);
                    continue;
                }
                if (nestedGuards.Count > 0)
                    guards.Add(new IfStatement(
                        new NotEqual(access, new Constant(null)),
                        new Block(nestedGuards)));
                continue;
            }

            foreach (var check in ConstraintCheckWriter.ChecksForValueMember(
                         prop, required, domain, analysis,
                         ConstraintCheckWriter.ConstraintCheckStyle.Create,
                         access, nullableValue: !required, includeRequired: true)) {
                guards.Add(new IfStatement(
                    check.GuardedCondition,
                    new Block([FailureLowering.Return(actionResultType, new FailureLowering.Spec(
                        ConstraintCheckWriter.FailureKindOf(check.Kind), check.Message, path))])));
            }
        }
        return guards;
    }

    private static Node RequiredGuard(Node value, string name, string path, Node actionResultType) =>
        new IfStatement(
            new Equal(value, new Constant(null)),
            new Block([FailureLowering.Return(actionResultType, new FailureLowering.Spec(
                DomainFailureKind.Required, new Constant($"'{name}' is required."), path))]));

    /// <summary>The value type of a name, a contract payload included. The same lookup order the module projection uses.</summary>
    private static ValueType? ValueTypeOf(Domain domain, string typeName) =>
        domain.Types.OfType<ValueType>().FirstOrDefault(t =>
            string.Equals(t.Name, typeName, StringComparison.Ordinal))
        ?? domain.ImportedContracts
            .SelectMany(contract => contract.Types)
            .OfType<ValueType>()
            .FirstOrDefault(t => string.Equals(t.Name, typeName, StringComparison.Ordinal));
}