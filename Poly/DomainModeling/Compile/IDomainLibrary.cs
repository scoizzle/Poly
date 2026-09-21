namespace Poly.DomainModeling.Compile;

/// <summary>
/// A library that registers into a session. Expression meaning, type maps, and
/// conventions are session tables core passes read. Analyzers append after the
/// core list (flags only). Duplicate <see cref="Id"/> fails closed.
/// </summary>
public interface IDomainLibrary {
    /// <summary>Unique, ordinal-compared identity. Duplicates fail closed.</summary>
    string Id { get; }

    /// <summary>Registers this library onto the session builder.</summary>
    void Register(SessionBuilder builder);

    /// <summary>
    /// Primitive types this library seeds onto a domain that lists its id.
    /// Empty when the library adds no catalog types.
    /// </summary>
    IReadOnlyList<(string Name, TypeCategory Category)> PrimitiveSeeds => [];
}