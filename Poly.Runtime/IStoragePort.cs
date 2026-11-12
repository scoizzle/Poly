namespace Poly.Runtime;

/// <summary>
/// The storage surface a compiled unique constraint depends on.
/// <see cref="Exists"/> is the advisory pre-check: it reads the pending keys and, in a host
/// with a database, the store. <see cref="TryReserve"/> is the atomic check-and-reserve in
/// the port's own in-flight set. <see cref="Release"/> drops a key this invocation reserved
/// or replaced. <see cref="Record"/> is the reserved-key shim kept until L-11 and P-10
/// migrate. There are no other writes and no <c>SaveChanges</c>.
/// </summary>
/// <remarks>
/// <see cref="TryReserve"/> is local to the port: it reserves in the port's own in-flight
/// set, so a host whose authority is a persisted unique index may return <c>true</c> for a
/// duplicate that is already persisted and fail at commit. <see cref="Exists"/> stays
/// advisory for the same reason; neither call owns the store's verdict.
/// </remarks>
public interface IStoragePort {
    /// <summary>Advisory: true when <paramref name="value"/> already exists for the property.</summary>
    bool Exists(string entity, string property, string value);

    /// <summary>
    /// Atomically reserves the key in this port's in-flight set. True when the key was
    /// absent and is now reserved; false when it was already present, and then nothing is
    /// mutated. A persisted unique index is the authority, so a host may still fail at commit.
    /// </summary>
    bool TryReserve(string entity, string property, string value);

    /// <summary>Drops a key this invocation reserved or recorded.</summary>
    void Release(string entity, string property, string value);

    /// <summary>
    /// Records a key created by this invocation but not yet saved. Kept as a shim: the
    /// compiler flips to <see cref="TryReserve"/> in L-11 and P-10 removes this member.
    /// </summary>
    void Record(string entity, string property, string value);
}