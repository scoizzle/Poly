namespace Poly.DomainModeling.Runtime;

/// <summary>
/// The storage surface a compiled unique constraint depends on.
/// <see cref="Exists"/> reads the database and the pending keys.
/// <see cref="Record"/> adds a pending key a later check in this invocation
/// must see. <see cref="Release"/> drops a pending key this invocation
/// replaced. There are no other writes and no <c>SaveChanges</c>.
/// </summary>
public interface IStoragePort {
    /// <summary>True when <paramref name="value"/> already exists for the property.</summary>
    bool Exists(string entity, string property, string value);

    /// <summary>Records a key created by this invocation but not yet saved.</summary>
    void Record(string entity, string property, string value);

    /// <summary>Drops a pending key <see cref="Record"/> added.</summary>
    void Release(string entity, string property, string value);
}