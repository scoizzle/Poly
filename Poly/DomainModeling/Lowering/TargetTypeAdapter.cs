using Poly.Ast.Nodes;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Ontology;

namespace Poly.DomainModeling.Lowering;

/// <summary>
/// One owner for every value-to-target-type decision in lowering: enum members,
/// <c>default(...)</c> constants, Decimal literals and clock/session folds all ask
/// this type what a value must be to fit the slot it is lowered into.
///
/// <para><see cref="Adapt(Node, DomainTypeReference, LoweringContext)"/> is the #683
/// contract for an already-lowered value. It cannot recover a bare member identifier
/// (lowering has already turned that into member access on the subject), so a bare
/// identifier must be adapted *before* it is lowered: the expression-level entry is
/// <see cref="DomainExpressionLoweringPass.AdaptTo"/>, which owns that pass's
/// resolvers and its current subject.</para>
/// </summary>
internal static class TargetTypeAdapter {
    /// <summary>
    /// Adapts an already-lowered value to <paramref name="target"/>. Only the shapes
    /// that survive lowering are handled: a string constant naming an enum member
    /// becomes qualified member access, and an integral constant for a Decimal target
    /// becomes a Decimal. An unresolvable target leaves the value untouched — nothing
    /// here rescans the domain tree.
    /// </summary>
    internal static Node Adapt(Node value, DomainTypeReference target, LoweringContext ctx) {
        if (value is Constant { Value: string member } && member.Length > 0
            && DomainToCSharpExporter.TryResolveEnumType(ctx.Domain, ctx.Analysis, target.TypeName, out var enumType)
            && enumType is not null
            && enumType.MemberNames.Contains(member, StringComparer.Ordinal))
            return new Member(new NamedTypeReference(enumType.Name), member);

        if (IsDecimal(ctx.Domain, target.TypeName)) {
            switch (value) {
                case Constant { Value: long integral }:
                    return new Constant((decimal)integral);
                case Constant { Value: int narrow }:
                    return new Constant((decimal)narrow);
            }
        }

        return value;
    }

    /// <summary>The mapped CLR type name of a DSL type is <c>decimal</c>.</summary>
    internal static bool IsDecimal(Domain domain, string typeName) =>
        RuntimeAnalysisCache.ClrTypeName(domain, typeName) is "decimal";

    private static bool IsEntityType(Domain domain, string typeName) =>
        domain.Types.OfType<Entity>().Any(e =>
            string.Equals(e.Name, typeName, StringComparison.Ordinal));
}