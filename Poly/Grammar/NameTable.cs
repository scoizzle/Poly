namespace Poly.Grammar;

/// <summary>
/// Per-document intern table: equal names return the same string reference, so a name is allocated once, on
/// first sight, and looked up allocation-free afterwards. NOT thread-safe (the line table in
/// <see cref="SourceText"/> is the only benign race); the backing set is allocated on the first
/// <see cref="Intern"/>. The empty name is <see cref="string.Empty"/> and is not counted.
/// </summary>
public sealed class NameTable {
    private HashSet<string>? _names;

    /// <summary>Number of distinct non-empty names interned so far.</summary>
    public int Count => _names?.Count ?? 0;

    public string Intern(ReadOnlySpan<char> name) {
        if (name.IsEmpty) return string.Empty;
        var names = _names ??= new HashSet<string>(StringComparer.Ordinal);
        var lookup = names.GetAlternateLookup<ReadOnlySpan<char>>();
        if (lookup.TryGetValue(name, out var existing)) return existing;
        var created = name.ToString();
        names.Add(created);
        return created;
    }
}