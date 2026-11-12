using Poly.Runtime;

namespace Poly.DomainModeling.Runtime;

/// <summary>
/// Session-scoped <see cref="IStoragePort"/> for module instances: the keys the
/// session's instances have reserved or recorded. The key set is an
/// <see cref="InMemoryStoragePort"/>; every change is journaled as the
/// <c>(entity, property, value)</c> triple so a failed operation can
/// <see cref="RollbackTo"/> its <see cref="Mark"/>.
/// </summary>
/// <remarks>
/// Single-threaded by contract: one session's instances run one at a time until X-03
/// gives the adapter a lock. <see cref="RollbackTo"/> drives the inner port directly, so
/// replaying the journal never writes new journal entries.
/// </remarks>
internal sealed class ModuleStoragePort : IStoragePort {
    private readonly IStoragePort _keys;
    private readonly List<(bool Added, string Entity, string Property, string Value)> _journal = [];

    public ModuleStoragePort() : this(new InMemoryStoragePort()) { }

    internal ModuleStoragePort(IStoragePort keys) => _keys = keys;

    public bool Exists(string entity, string property, string value) {
        ThrowIfNull(entity, property, value);
        return _keys.Exists(entity, property, value);
    }

    public bool TryReserve(string entity, string property, string value) {
        ThrowIfNull(entity, property, value);
        if (!_keys.TryReserve(entity, property, value))
            return false;

        _journal.Add((true, entity, property, value));
        return true;
    }

    public void Record(string entity, string property, string value) {
        ThrowIfNull(entity, property, value);
        if (_keys.TryReserve(entity, property, value))
            _journal.Add((true, entity, property, value));
    }

    public void Release(string entity, string property, string value) {
        ThrowIfNull(entity, property, value);
        if (!_keys.Exists(entity, property, value))
            return;

        _keys.Release(entity, property, value);
        _journal.Add((false, entity, property, value));
    }

    internal int Mark() => _journal.Count;

    internal void RollbackTo(int mark) {
        for (var i = _journal.Count - 1; i >= mark; i--) {
            var (added, entity, property, value) = _journal[i];
            if (added)
                _keys.Release(entity, property, value);
            else
                _keys.Record(entity, property, value);
        }
        _journal.RemoveRange(mark, _journal.Count - mark);
    }

    private static void ThrowIfNull(string entity, string property, string value) {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(property);
        ArgumentNullException.ThrowIfNull(value);
    }
}