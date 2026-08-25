namespace Poly.DomainModeling.Compile;

/// <summary>
/// A library that registers into a session. The product extension slot is
/// <see cref="SessionBuilder.AddAnalyzer"/>; type maps/conventions are config those
/// passes close over. It does not add language shapes. Duplicate <see cref="Id"/>
/// fails closed. Not a discovery host.
/// </summary>
public interface IDomainLibrary {
    /// <summary>Unique, ordinal-compared identity. Duplicates fail closed.</summary>
    string Id { get; }

    /// <summary>Registers this library onto the session builder.</summary>
    void Register(SessionBuilder builder);
}