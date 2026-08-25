using System.Collections;
using System.Diagnostics.CodeAnalysis;

namespace Poly.DomainModeling.Runtime;

/// <summary>
/// Explicit <see cref="IDictionary{TKey,TValue}"/> so VM property EmitRead/Write
/// can cast This to the interface. Public API is unchanged; the bag remains
/// <c>_values</c>. Concrete <see cref="Dictionary{TKey,TValue}"/> still works
/// because it implements the same interface.
/// </summary>
public sealed partial record DomainEntityInstance : IDictionary<string, object?> {
    ICollection<string> IDictionary<string, object?>.Keys => _values.Keys;
    ICollection<object?> IDictionary<string, object?>.Values => _values.Values;
    int ICollection<KeyValuePair<string, object?>>.Count => _values.Count;
    bool ICollection<KeyValuePair<string, object?>>.IsReadOnly => false;

    object? IDictionary<string, object?>.this[string key] {
        get => _values[key];
        set => _values[key] = value;
    }

    void IDictionary<string, object?>.Add(string key, object? value) => _values.Add(key, value);

    bool IDictionary<string, object?>.ContainsKey(string key) => _values.ContainsKey(key);

    bool IDictionary<string, object?>.Remove(string key) => _values.Remove(key);

    bool IDictionary<string, object?>.TryGetValue(string key, [MaybeNullWhen(false)] out object? value) =>
        _values.TryGetValue(key, out value);

    void ICollection<KeyValuePair<string, object?>>.Add(KeyValuePair<string, object?> item) =>
        ((ICollection<KeyValuePair<string, object?>>)_values).Add(item);

    void ICollection<KeyValuePair<string, object?>>.Clear() => _values.Clear();

    bool ICollection<KeyValuePair<string, object?>>.Contains(KeyValuePair<string, object?> item) =>
        ((ICollection<KeyValuePair<string, object?>>)_values).Contains(item);

    void ICollection<KeyValuePair<string, object?>>.CopyTo(KeyValuePair<string, object?>[] array, int arrayIndex) =>
        ((ICollection<KeyValuePair<string, object?>>)_values).CopyTo(array, arrayIndex);

    bool ICollection<KeyValuePair<string, object?>>.Remove(KeyValuePair<string, object?> item) =>
        ((ICollection<KeyValuePair<string, object?>>)_values).Remove(item);

    IEnumerator<KeyValuePair<string, object?>> IEnumerable<KeyValuePair<string, object?>>.GetEnumerator() =>
        _values.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => ((IEnumerable)_values).GetEnumerator();
}
