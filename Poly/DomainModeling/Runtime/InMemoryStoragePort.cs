namespace Poly.DomainModeling.Runtime;

/// <summary>
/// In-memory <see cref="IStoragePort"/> the simulator supplies when an action
/// carries an unbound storage dependency. The database half reads the attached
/// <see cref="DomainInstanceStore"/>; the pending half holds keys recorded by
/// this invocation so a nested second create fails before any host save.
/// </summary>
public sealed class InMemoryStoragePort : IStoragePort {
    private readonly HashSet<string> _pending = new(StringComparer.Ordinal);
    private readonly DomainInstanceStore? _store;

    public InMemoryStoragePort(DomainInstanceStore? store = null) {
        _store = store;
    }

    public bool Exists(string entity, string property, string value) =>
        _pending.Contains(Key(entity, property, value))
        || (_store?.ExistsValue(entity, property, value) ?? false);

    public void Record(string entity, string property, string value) =>
        _pending.Add(Key(entity, property, value));

    /// <summary>Removes a pending key. The attached store reads the instance's current value.</summary>
    public void Release(string entity, string property, string value) =>
        _pending.Remove(Key(entity, property, value));

    private static string Key(string entity, string property, string value) =>
        entity + "\u001f" + property + "\u001f" + value;
}