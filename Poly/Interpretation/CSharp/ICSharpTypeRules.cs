namespace Poly.Interpretation.CSharp;

/// <summary>
/// The questions C#'s own rules answer about a definition (#998 T6a; decision §3.3, §3.4): which C# simple type it is,
/// and whether it is an enum type or has value semantics. Declared by the C# surface, which owns the questions; asked through
/// <see cref="ITypeSystem.Rule{TRule}"/>, so a target that is not C# answers none of them and a foreign definition is no
/// C# simple type, enum or value type.
/// </summary>
internal interface ICSharpTypeRules : ITypeSystemRule {
    /// <summary>The C# simple type <paramref name="definition"/> is, or null.</summary>
    SimpleType? SimpleTypeOf(ITypeDefinition definition);

    /// <summary>True when <paramref name="definition"/> is a C# enum type.</summary>
    bool IsEnumType(ITypeDefinition definition);

    /// <summary>True when <paramref name="definition"/> is a C# value type (an enum and a simple type included).</summary>
    bool HasValueSemantics(ITypeDefinition definition);
}