namespace Poly.Runtime;

/// <summary>
/// The thread-safe in-memory reference <see cref="IStoragePort"/>: one lock over an
/// ordinal key set, so <see cref="TryReserve"/> is a real check-and-add. No persistence,
/// no <c>SaveChanges</c>; a saved store is the host's job.
/// </summary>
public sealed class InMemoryStoragePort : IStoragePort {
    private readonly object _gate = new();
    private readonly HashSet<string> _keys = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public bool Exists(string entity, string property, string value) {
        var key = Key(entity, property, value);
        lock (_gate)
            return _keys.Contains(key);
    }

    /// <inheritdoc />
    public bool TryReserve(string entity, string property, string value) {
        var key = Key(entity, property, value);
        lock (_gate)
            return _keys.Add(key);
    }

    /// <inheritdoc />
    public void Release(string entity, string property, string value) {
        var key = Key(entity, property, value);
        lock (_gate)
            _keys.Remove(key);
    }

    /// <inheritdoc />
    public void Record(string entity, string property, string value) {
        var key = Key(entity, property, value);
        lock (_gate)
            _keys.Add(key);
    }

    private static string Key(string entity, string property, string value) {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(property);
        ArgumentNullException.ThrowIfNull(value);
        return KeyCodec.Canonical(entity, property, value);
    }
}