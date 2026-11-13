namespace Poly.Introspection;

/// <summary>
/// A conversion a platform defines between two types. <see cref="Method"/> is the declared operator
/// to invoke, or null when the platform applies the conversion itself (a built-in rule).
/// </summary>
internal readonly record struct TypeConversion(ConversionOperatorKind Kind, ITypeMethod? Method);

/// <summary>
/// An operator a definition declares. <see cref="PlatformApplied"/> is true when the platform applies the
/// operator itself and no member is invoked; <see cref="Signature"/> still names its parameter and result types.
/// For the .NET platform it is true for every operator of the simple types except decimal's, which the CLR declares
/// (false), and for string concatenation; string equality is the CLR's declared member. A consumer must accept either form.
/// </summary>
internal readonly record struct DeclaredOperator(ITypeMethod Signature, bool PlatformApplied);