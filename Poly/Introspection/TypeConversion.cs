namespace Poly.Introspection;

/// <summary>
/// A conversion a platform defines between two types. <see cref="Method"/> is the declared operator
/// to invoke, or null when the platform applies the conversion itself (a built-in rule).
/// </summary>
internal readonly record struct TypeConversion(ConversionOperatorKind Kind, ITypeMethod? Method);