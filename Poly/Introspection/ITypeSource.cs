namespace Poly.Introspection;

/// <summary>
/// Something that contains type definitions: an assembly, a unit's declarations, a domain, another platform's library.
/// A source answers what it contains; it never answers what a name means. Which sources a scope sees, and under which
/// names, is the analysis' decision (a <c>using</c>, an import, a <c>uses</c> line).
/// </summary>
public interface ITypeSource {
    /// <summary>
    /// Gets the definitions this source declares directly in <paramref name="namespace"/> (null is the global
    /// namespace). A type nested in another type is not listed. Names are compared ordinally.
    /// </summary>
    IEnumerable<ITypeDefinition> TypesIn(string? @namespace);

    /// <summary>
    /// Gets the definition named <paramref name="name"/> that this source declares directly in
    /// <paramref name="namespace"/>, or null. A generic definition is named with its arity, as its platform names it.
    /// </summary>
    ITypeDefinition? TypeIn(string? @namespace, string name) =>
        TypesIn(@namespace).FirstOrDefault(type => string.Equals(type.Name, name, StringComparison.Ordinal));
}