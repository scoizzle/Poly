namespace Poly.Introspection.CommonLanguageRuntime;

/// <summary>
/// The CLR host's factory for a tree reference to a runtime type. The AST node carries the type; the AST itself names
/// no <c>typeof</c>, so a host that has no CLR types never sees one.
/// </summary>
public static class ClrTypeReferences {
    /// <summary>A reference to <typeparamref name="T"/>.</summary>
    public static TypeReference To<T>() => new ClrTypeReference(typeof(T));

    /// <summary>A reference to <paramref name="type"/>.</summary>
    public static TypeReference Of(Type type) => new ClrTypeReference(type);
}