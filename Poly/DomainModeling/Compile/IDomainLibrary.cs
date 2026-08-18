namespace Poly.DomainModeling.Compile;

/// <summary>
/// A library that registers <b>concepts</b> into a session: meaning for existing
/// spellings, type maps, conventions, and/or artifact files. It does not add
/// language shapes. Duplicate <see cref="Id"/> fails closed. Not a discovery host.
/// </summary>
public interface IDomainLibrary {
    /// <summary>Unique, ordinal-compared identity. Duplicates fail closed.</summary>
    string Id { get; }

    /// <summary>Registers this library onto the session builder.</summary>
    void Register(SessionBuilder builder);
}