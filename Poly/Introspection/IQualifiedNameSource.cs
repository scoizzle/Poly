namespace Poly.Introspection;

/// <summary>
/// A source that also answers a name it holds, by the spelling a scope looks one up by (#998 T12b). A source states what
/// it contains (<see cref="ITypeSource"/>); this adds the one question the analysis layer asks its own sources in
/// precedence order: "do you hold this name". It replaces the retired provider's by-name half without carrying the
/// fallback stack or the CLR handle that interface also carried.
/// </summary>
internal interface IQualifiedNameSource : ITypeSource {
    /// <summary>
    /// The definition this source holds under <paramref name="name"/>, or null. The name is whatever a scope spells:
    /// a qualified name, an arity key, or a bare name the source chooses to answer.
    /// </summary>
    ITypeDefinition? ByQualifiedName(string name);
}