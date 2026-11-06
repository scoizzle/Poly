using Poly.Ast.Nodes;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Lowering;
using Poly.DomainModeling.Ontology;

namespace Poly.DomainModeling.Compile;

/// <summary>
/// Core claims the constraints <see cref="ConstraintCheckWriter"/> emits a guard
/// for: required, range, length, pattern, and equality. The writer owns the
/// condition, message, presence, order, and type gates; create and assign place
/// the one guard. Claiming keeps a pack from compiling the same constraint a
/// second time. The claim set is <see cref="ConstraintCheckWriter.CoverageByConstraint"/>.
/// </summary>
public sealed class BuiltinConstraintCompiler : IConstraintCompiler {
    public string LibraryId => CoreLibrary.CoreId;

    public bool Applies(Constraint constraint) =>
        constraint is not null && ConstraintCheckWriter.KindsFor(constraint).Count > 0;

    /// <summary>
    /// Claims only. The create factory and each assign block already place the
    /// writer's guard, so <see cref="ConstraintCompilation"/> skips this compiler
    /// before calling here; a call means some path is re-placing these guards, and it fails closed.
    /// </summary>
    ConstraintContribution IConstraintCompiler.Compile(ConstraintCompileRequest request) =>
        throw new NotSupportedException(
            $"{LibraryId} places constraint guards through ConstraintCheckWriter; "
            + "BuiltinConstraintCompiler is a claim, not a second writer.");

    /// <summary>
    /// True for a check kind some claimed constraint record emits. Derived from
    /// <see cref="ConstraintCheckWriter.CoverageByConstraint"/>; enum slot and member
    /// kinds are in no row, so they stay unclaimed.
    /// </summary>
    internal static bool Claims(ConstraintCheckWriter.Kind kind) => ClaimedKinds.Contains(kind);

    private static readonly HashSet<ConstraintCheckWriter.Kind> ClaimedKinds =
        [.. ConstraintCheckWriter.CoverageByConstraint.Values.SelectMany(coverage => coverage.Kinds)];

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
}