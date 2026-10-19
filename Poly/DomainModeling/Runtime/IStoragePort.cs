namespace Poly.DomainModeling.Runtime;

/// <summary>
/// The read-only storage surface a compiled constraint may depend on.
/// <see cref="Exists"/> is the database half; <see cref="Record"/> is the
/// pending-key half a nested create must see before the host saves. There are
/// no writes and no <c>SaveChanges</c> through this port.
/// </summary>
public interface IStoragePort {
    /// <summary>True when <paramref name="value"/> already exists for the property.</summary>
    bool Exists(string entity, string property, string value);

    /// <summary>Records a key created by this invocation but not yet saved.</summary>
    void Record(string entity, string property, string value);
}