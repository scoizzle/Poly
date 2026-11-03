namespace Poly.DomainModeling.Runtime;

/// <summary>
/// Session-scoped <see cref="IStoragePort"/> for module instances: the keys the
/// session's instances have recorded. Every change is journaled so a failed
/// operation can <see cref="RollbackTo"/> its <see cref="Mark"/>.
/// </summary>
internal sealed class ModuleStoragePort : IStoragePort {
    private readonly HashSet<string> _keys = new(StringComparer.Ordinal);
    private readonly List<(bool Added, string Key)> _journal = [];

    public bool Exists(string entity, string property, string value) =>
        _keys.Contains(Key(entity, property, value));

    public void Record(string entity, string property, string value) {
        var key = Key(entity, property, value);
        if (_keys.Add(key))
            _journal.Add((true, key));
    }

    public void Release(string entity, string property, string value) {
        var key = Key(entity, property, value);
        if (_keys.Remove(key))
            _journal.Add((false, key));
    }

    internal int Mark() => _journal.Count;

    internal void RollbackTo(int mark) {
        for (var i = _journal.Count - 1; i >= mark; i--) {
            var (added, key) = _journal[i];
            if (added)
                _keys.Remove(key);
            else
                _keys.Add(key);
        }
        _journal.RemoveRange(mark, _journal.Count - mark);
    }

    private static string Key(string entity, string property, string value) =>
        entity + "\u001f" + property + "\u001f" + value;
}