namespace Poly.Introspection;

/// <summary>
/// A conversion a platform states from a source type to <see cref="ResultTypeDefinition"/>. <see cref="Method"/> is the
/// declared operator to invoke, or null when the platform applies the conversion itself (a built-in rule).
/// </summary>
internal readonly record struct TypeConversion(
    ConversionOperatorKind Kind,
    ITypeDefinition ResultTypeDefinition,
    ITypeMethod? Method);