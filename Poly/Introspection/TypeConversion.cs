namespace Poly.Introspection;

/// <summary>
/// A conversion a platform defines between two types. <see cref="Method"/> is the declared operator
/// to invoke, or null when the platform applies the conversion itself (a built-in rule).
/// </summary>
internal readonly record struct TypeConversion(ConversionOperatorKind Kind, ITypeMethod? Method);

/// <summary>
/// An operator a definition declares. <see cref="PlatformApplied"/> is true when the platform applies the
/// operator itself and no member is invoked; <see cref="Signature"/> still names its parameter and result types.
/// The flag is a fact about the platform's built-in set, not about the result: Float32's comparisons are answered as
/// declared operators (false) while Float64's and Int32's are platform-applied (true), with identical results, so a
/// consumer must accept either form. T-24 widens the built-in set.
/// </summary>
internal readonly record struct DeclaredOperator(ITypeMethod Signature, bool PlatformApplied);