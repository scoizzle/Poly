namespace Poly.Introspection;

/// <summary>
/// Poly's modeled-type operator convention: a static member named <c>op_*</c> in <see cref="ITypeDefinition.Methods"/>,
/// the same convention <c>op_Implicit</c> and <c>op_Explicit</c> use. A convention of Poly, not a platform fact.
/// </summary>
internal static class ModeledOperators {
    public static string? NameOf(OperatorKind kind) => throw new NotImplementedException();

    public static IEnumerable<DeclaredOperator> Find(ITypeDefinition definition, OperatorKind kind) =>
        throw new NotImplementedException();
}