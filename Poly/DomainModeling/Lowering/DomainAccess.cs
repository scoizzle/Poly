using Poly.Ast;
using Poly.Ast.Nodes;
using Poly.DomainModeling.Ontology;
using Poly.Introspection;

using Action = Poly.DomainModeling.Ontology.Action;
using ValueType = Poly.DomainModeling.Ontology.ValueType;

namespace Poly.DomainModeling.Lowering;

/// <summary>
/// The one rule that maps DSL <see cref="Visibility"/> to C# access (Q13, #659): an entity, value type or
/// action is <c>public</c> iff it is marked <c>public</c>, otherwise <c>internal</c>. Enums carry no mark and
/// stay public; a stage enum follows its entity. A member of a public type whose signature names an internal
/// domain type is demoted to <c>internal</c> (<see cref="Demote"/>). <see cref="ForInfrastructure"/> is the
/// host clamp: L-22's minimal seam, which P-17 replaces with the final host shape.
/// </summary>
internal static class DomainAccess {
    public static AccessModifier ForEntity(Entity entity) => Of(entity.Visibility);

    public static AccessModifier ForValue(ValueType valueType) => Of(valueType.Visibility);

    public static AccessModifier ForAction(Action action) => Of(action.Visibility);

    /// <summary>A type the host generates (DbContext, DTOs): internal iff any named domain type is internal.</summary>
    public static AccessModifier ForInfrastructure(Domain domain, IEnumerable<string> domainTypeNames) {
        var names = domainTypeNames.ToHashSet(StringComparer.Ordinal);
        return domain.Types.Any(t => names.Contains(t.Name) && IsInternal(t))
            ? AccessModifier.Internal
            : AccessModifier.Public;
    }

    /// <summary>Internal when any of the types is (or wraps) an internal domain entity or value type.</summary>
    public static AccessModifier Demote(AccessModifier declared, Domain domain, params Node?[] types) =>
        declared == AccessModifier.Public && types.Any(t => t is not null && ExposesInternal(t, domain))
            ? AccessModifier.Internal
            : declared;

    public static bool ExposesInternal(Node type, Domain domain) => type switch {
        NamedTypeReference named =>
            domain.Types.Any(t => t.Name == named.TypeName && IsInternal(t))
            || (named.TypeArguments?.Any(a => ExposesInternal(a, domain)) ?? false),
        OptionalTypeReference optional => ExposesInternal(optional.InnerType, domain),
        _ => false,
    };

    private static bool IsInternal(DomainType type) => type switch {
        Entity entity => entity.Visibility != Visibility.Public,
        ValueType valueType => valueType.Visibility != Visibility.Public,
        _ => false,
    };

    private static AccessModifier Of(Visibility visibility) =>
        visibility == Visibility.Public ? AccessModifier.Public : AccessModifier.Internal;
}