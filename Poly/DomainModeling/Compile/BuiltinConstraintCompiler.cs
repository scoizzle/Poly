using Poly.Ast.Nodes;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Lowering;
using Poly.DomainModeling.Ontology;

namespace Poly.DomainModeling.Compile;

/// <summary>
/// Core claims required, range, length, pattern, and equality.
/// <see cref="ConstraintCheckWriter"/> owns the condition, message, presence,
/// order, and type gates. Create and assign place the one guard.
/// <see cref="ConstraintCompilation"/> does not place these statements: the
/// Create anchor inserts them at the first <c>new</c>, which cannot interleave
/// with the enum guards that stay in the body, and it cannot reach an assign block.
/// </summary>
public sealed class BuiltinConstraintCompiler : IConstraintCompiler {
    public string LibraryId => CoreLibrary.CoreId;

    public bool Applies(Constraint constraint) => constraint is
        RequiredConstraint
        or RangeConstraint
        or LengthConstraint
        or PatternConstraint
        or EqualityConstraint;

    public ConstraintContribution Compile(ConstraintCompileRequest request) {
        ArgumentNullException.ThrowIfNull(request);
        var parameter = request.ParameterOn("Create")
            ?? throw new InvalidOperationException(
                $"Constraint '{request.Constraint.GetType().Name}' on {request.Entity.Name}.{request.Property.Name} has no Create parameter to guard.");
        var includeRequired = !request.Property.Constraints.Any(c => c is DefaultValueConstraint);
        var checks = ForConstraint(
            Checks(
                request.Entity,
                request.Property,
                domain: null,
                analysis: null,
                ConstraintCheckWriter.ConstraintCheckStyle.Create,
                parameter,
                parameter.TypeReference is OptionalTypeReference,
                parameter.DefaultValue is not null,
                includeRequired),
            request.Constraint);
        if (checks.Count == 0)
            throw new InvalidOperationException(
                $"Constraint '{request.Constraint.GetType().Name}' on {request.Entity.Name}.{request.Property.Name} produced no guard.");

        return new ConstraintContribution(
            LibraryId,
            request.Entity.Name,
            new ConstraintAnchor.Create(),
            checks.Select(check => CreateGuard(request.Entity.Name, check)).ToArray(),
            [],
            []);
    }

    internal static bool Claims(ConstraintCheckWriter.Kind kind) => kind is
        ConstraintCheckWriter.Kind.Required
        or ConstraintCheckWriter.Kind.RangeMinimum
        or ConstraintCheckWriter.Kind.RangeMaximum
        or ConstraintCheckWriter.Kind.LengthMinimum
        or ConstraintCheckWriter.Kind.LengthMaximum
        or ConstraintCheckWriter.Kind.Pattern
        or ConstraintCheckWriter.Kind.Equality;

    /// <summary>
    /// The writer's checks for one property, in writer order. Enum slot and
    /// member checks are in the list so the call site can place them; this
    /// compiler does not claim those kinds.
    /// </summary>
    internal static IReadOnlyList<ConstraintCheckWriter.Check> Checks(
        Entity entity,
        Property prop,
        Domain? domain,
        INodeMetadataProvider? analysis,
        ConstraintCheckWriter.ConstraintCheckStyle style,
        Node value,
        bool nullableValue,
        bool valueHasDefault,
        bool includeRequired) =>
        ConstraintCheckWriter.ChecksFor(
            entity, prop, domain, analysis, style, value,
            nullableValue, valueHasDefault, includeRequired);

    /// <summary>
    /// Create's flat guard: <c>if (violation) return DomainResult&lt;T&gt;.Failure(...)</c>.
    /// </summary>
    internal static IfStatement CreateGuard(string entityName, ConstraintCheckWriter.Check check) {
        var resultType = new NamedTypeReference(
            "DomainResult", TypeArguments: [new NamedTypeReference(entityName)]);
        return new IfStatement(
            check.GuardedCondition,
            new Block([
                new Return(new Invoke(new Member(resultType, "Failure"), check.Message))
            ]));
    }

    private static List<ConstraintCheckWriter.Check> ForConstraint(
        IReadOnlyList<ConstraintCheckWriter.Check> checks, Constraint constraint) {
        var kinds = Kinds(constraint);
        var list = new List<ConstraintCheckWriter.Check>();
        foreach (var check in checks) {
            if (kinds.Contains(check.Kind))
                list.Add(check);
        }
        return list;
    }

    private static HashSet<ConstraintCheckWriter.Kind> Kinds(Constraint constraint) => constraint switch {
        RequiredConstraint => [ConstraintCheckWriter.Kind.Required],
        RangeConstraint => [ConstraintCheckWriter.Kind.RangeMinimum, ConstraintCheckWriter.Kind.RangeMaximum],
        LengthConstraint => [ConstraintCheckWriter.Kind.LengthMinimum, ConstraintCheckWriter.Kind.LengthMaximum],
        PatternConstraint => [ConstraintCheckWriter.Kind.Pattern],
        EqualityConstraint => [ConstraintCheckWriter.Kind.Equality],
        _ => []
    };
}