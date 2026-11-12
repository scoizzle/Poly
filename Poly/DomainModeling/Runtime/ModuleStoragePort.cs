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
/// Atomic under its own lock: one gate covers the journal and the inner port's call as a single
/// critical section, so a journal entry always describes the key set it was written against.
/// <see cref="RollbackTo"/> holds that gate across its whole revert loop and validates its mark
/// before it changes anything, and it drives the inner port directly, so replaying the journal
/// never writes new journal entries. The journal is flat, so one mark is in flight per port.
/// <see cref="IStorageJournal"/> is the explicit seam <see cref="ActionScope"/> rolls a failed
/// action back through: a scope takes its root mark when it opens and holds it until it closes,
/// so the executor's own mark must sit outside the scope's. The public storage property may
/// still reserve inside a mark.
/// </remarks>
internal sealed class ModuleStoragePort : IStoragePort, IStorageJournal {
    private readonly object _gate = new();
    private readonly IStoragePort _keys;
    private readonly List<(bool Added, string Entity, string Property, string Value)> _journal = [];

    public ModuleStoragePort() : this(new InMemoryStoragePort()) { }

    internal ModuleStoragePort(IStoragePort keys) => _keys = keys;

    public bool Exists(string entity, string property, string value) {
        ThrowIfNull(entity, property, value);
        lock (_gate)
            return _keys.Exists(entity, property, value);
    }

    public bool TryReserve(string entity, string property, string value) {
        ThrowIfNull(entity, property, value);
        lock (_gate) {
            if (!_keys.TryReserve(entity, property, value))
                return false;

            _journal.Add((true, entity, property, value));
            return true;
        }
    }

    public void Record(string entity, string property, string value) {
        ThrowIfNull(entity, property, value);
        lock (_gate) {
            if (_keys.TryReserve(entity, property, value))
                _journal.Add((true, entity, property, value));
        }
    }

    public void Release(string entity, string property, string value) {
        ThrowIfNull(entity, property, value);
        lock (_gate) {
            if (!_keys.Exists(entity, property, value))
                return;

            _keys.Release(entity, property, value);
            _journal.Add((false, entity, property, value));
        }
    }

    internal int Mark() {
        lock (_gate)
            return _journal.Count;
    }

    internal void RollbackTo(int mark) {
        lock (_gate) {
            // Fail closed: a mark outside the journal would revert every entry and then throw
            // from the indexer, leaving keys released and the journal unreverted.
            if (mark < 0 || mark > _journal.Count)
                throw new ArgumentOutOfRangeException(nameof(mark), mark,
                    $"The mark must be between 0 and the journal length ({_journal.Count}).");
            for (var i = _journal.Count - 1; i >= mark; i--) {
                var (added, entity, property, value) = _journal[i];
                if (added)
                    _keys.Release(entity, property, value);
                else
                    _ = _keys.TryReserve(entity, property, value);
            }
            _journal.RemoveRange(mark, _journal.Count - mark);
        }
    }

    // The explicit seam: Poly.Runtime may not name this internal type, and IStoragePort callers
    // must not acquire a second, wider journal surface by accident.
    int IStorageJournal.Mark() => Mark();

    void IStorageJournal.RollbackTo(int mark) => RollbackTo(mark);

    private static void ThrowIfNull(string entity, string property, string value) {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(property);
        ArgumentNullException.ThrowIfNull(value);
    }
}