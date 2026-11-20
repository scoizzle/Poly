using Poly.DomainModeling.Ontology;

namespace Poly.DomainModeling.Meaning;

/// <summary>
/// Which property types a constraint applies to: a question the constraint's owner declares and registers with the
/// domain's type system (#998 T7, decision §3.2), never a category a type states about itself.
/// </summary>
internal interface IConstraintApplicability : ITypeSystemRule {
    /// <summary>True when <paramref name="constraint"/> applies to a property of <paramref name="propertyType"/>.</summary>
    bool Applies(Constraint constraint, ITypeDefinition propertyType);
}

/// <summary>
/// Core's constraints (owner ruling 7): a range applies to every definition a domain number word binds (Number, Int,
/// Int64, Int32, Decimal, Float, Double), a length and a pattern to the one a text word binds. The definition is
/// spelled by the domain system, by identity; what it answers about itself is never asked. Equality and the markers
/// (optional, default, unique) apply to any type.
/// </summary>
internal sealed class CoreConstraintApplicability(ITypeSystem domain) : IConstraintApplicability {
    public bool Applies(Constraint constraint, ITypeDefinition propertyType) => constraint switch {
        RangeConstraint => domain.Spell(propertyType) is "Number" or "Int32" or "Float32" or "Float" or "Decimal",
        LengthConstraint or PatternConstraint => domain.Spell(propertyType) is "Text",
        _ => true
    };
}

/// <summary>The domain's questions about a property's declared word, asked of a type system's registered rule.</summary>
internal static class ConstraintApplicabilityQueries {
    private static readonly RangeConstraint AnyRange = new(null, null);
    private static readonly LengthConstraint AnyLength = new(0, int.MaxValue);

    /// <summary>True when <paramref name="system"/> declares which types a constraint applies to.</summary>
    internal static bool DecidesConstraintApplicability(this ITypeSystem system) =>
        system.Rule<IConstraintApplicability>() is not null;

    /// <summary>True when <paramref name="constraint"/> applies to a property declared with the domain word
    /// <paramref name="typeName"/>: the word is bound on <paramref name="system"/> and its rule answers. False when the
    /// system has no rule or no binding for the word.</summary>
    internal static bool ConstraintApplies(this ITypeSystem system, Constraint constraint, string typeName) =>
        system.Rule<IConstraintApplicability>() is { } rule
        && system.Bind(typeName) is { } definition
        && rule.Applies(constraint, definition);

    /// <summary>True when a range applies to a property declared with <paramref name="typeName"/>.</summary>
    internal static bool RangeApplies(this ITypeSystem system, string typeName) => system.ConstraintApplies(AnyRange, typeName);

    /// <summary>True when a length (and a pattern) applies to a property declared with <paramref name="typeName"/>.</summary>
    internal static bool LengthApplies(this ITypeSystem system, string typeName) => system.ConstraintApplies(AnyLength, typeName);

    /// <summary>True when <paramref name="typeName"/> binds what the domain word <c>Boolean</c> binds.</summary>
    internal static bool IsBooleanWord(this ITypeSystem system, string typeName) =>
        system.Bind(typeName) is { } definition && system.Bind("Boolean") is { } boolean && system.IsSame(definition, boolean);
}