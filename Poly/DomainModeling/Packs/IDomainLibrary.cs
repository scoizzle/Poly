namespace Poly.DomainModeling.Packs;

/// <summary>
/// A library that registers into a <see cref="DomainHost"/>. Duplicate
/// <see cref="Id"/> fails closed. Not a plugin host.
/// </summary>
public interface IDomainLibrary {
    /// <summary>Unique, ordinal-compared identity. Duplicates fail closed.</summary>
    string Id { get; }

    /// <summary>Registers this library into the host's spell and mean surfaces.</summary>
    void Register(HostSurfaces surfaces);
}