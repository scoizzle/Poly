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
///
/// <para><see cref="TypeDefault"/> is the static default of a required slot.</para>
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
        // `null` reaches lowering only for an optional slot (analysis refuses it into a required one, #606). It is that
        // slot's own absent value, `(T?)null`: a bare null constant is the word 0 (a present zero in a value-type slot)
        // and has no type (a conditional over it is typed Object, which has no Length or IsMatch).
        if (value is Constant { Value: null } && OptionalSlot(target, ctx) is { } slot)
            return new TypeCast(value, slot);

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

    /// <summary>
    /// The static default of a required slot whose type the caller cannot name: an
    /// enum's first member, a Decimal zero, a scalar's CLR zero, a mapped default
    /// member, or a typed null for an entity reference. The adapter invents no value
    /// for a slot without <c>default(...)</c> — only this supplies one.
    ///
    /// <para>Transitional: L-08 owns the final required/NotCreatable rule for an
    /// omitted slot; this never decides requiredness.</para>
    /// </summary>
    internal static Node TypeDefault(DomainTypeReference target, LoweringContext ctx) {
        if (DomainToCSharpExporter.TryResolveEnumType(ctx.Domain, ctx.Analysis, target.TypeName, out var enumType)
            && enumType is { MemberNames.Count: > 0 })
            return new Member(new NamedTypeReference(enumType.Name), enumType.MemberNames[0]);

        var clr = RuntimeAnalysisCache.ClrTypeName(ctx.Domain, target.TypeName);
        if (clr is "string") return new Constant("");
        if (clr is "long") return new Constant(0L);
        if (clr is "int") return new Constant(0);
        if (clr is "decimal") return new Constant(0m);
        if (clr is "bool") return new Constant(false);
        if (DomainTypeMapping.TryDefaultMember(clr, out var defaultType, out var member))
            return new Member(new NamedTypeReference(defaultType), member);
        if (IsEntityType(ctx.Domain, target.TypeName))
            return DomainToCSharpExporter.TypedNull(target.TypeName);
        return new Constant(null);
    }

    /// <summary>
    /// The optional type of an enum or primitive slot (<c>long?</c>, <c>bool?</c>, <c>string?</c>, an enum's <c>T?</c>);
    /// null for any other slot.
    /// </summary>
    internal static OptionalTypeReference? OptionalSlot(DomainTypeReference target, LoweringContext ctx) {
        var typed = DomainToCSharpExporter.TryResolveEnumType(ctx.Domain, ctx.Analysis, target.TypeName, out _)
            || DomainTypeMapping.TryPrimitiveType(RuntimeAnalysisCache.ClrTypeName(ctx.Domain, target.TypeName), out _);
        return typed
            ? new OptionalTypeReference(DomainToCSharpExporter.MapDomainTypeRef(target, ctx.Domain, ctx.Analysis))
            : null;
    }

    /// <summary>The mapped CLR type name of a DSL type is <c>decimal</c>.</summary>
    internal static bool IsDecimal(Domain domain, string typeName) =>
        RuntimeAnalysisCache.ClrTypeName(domain, typeName) is "decimal";

    private static bool IsEntityType(Domain domain, string typeName) =>
        domain.Types.OfType<Entity>().Any(e =>
            string.Equals(e.Name, typeName, StringComparison.Ordinal));
}