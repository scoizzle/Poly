using System.Collections;
using System.Diagnostics.CodeAnalysis;

using Poly.DomainModeling.Ontology;

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
        get {
            if (_values.TryGetValue(key, out var stored))
                return stored;
            if (TryGetOneToOneNavigation(key, out var nav))
                return nav;
            if (MatchCollectionNavigation(key) is { } collection)
                return ReadLinkedTargets(collection);
            if (MatchCollectionField(key) is { } field)
                return new LinkedCollectionView(this, field);
            return _values[key];
        }
        set {
            if (MatchOneToOneNavigation(key) is { } nav && value is DomainEntityInstance child) {
                _values[key] = value;
                var store = StoreOrDefault();
                if (!ReferenceEquals(child.Store, store) && !store.TryAdd(child, out var error))
                    throw new InvalidOperationException(error ?? "Failed to register linked instance.");
                store.Link(nav.Name, this, child);
                TryLinkCreateInBackReference(child);
                return;
            }
            _values[key] = value;
        }
    }

    void IDictionary<string, object?>.Add(string key, object? value) => _values.Add(key, value);

    bool IDictionary<string, object?>.ContainsKey(string key) =>
        _values.ContainsKey(key)
        || MatchOneToOneNavigation(key) is not null
        || MatchCollectionNavigation(key) is not null
        || MatchCollectionField(key) is not null;

    bool IDictionary<string, object?>.Remove(string key) => _values.Remove(key);

    bool IDictionary<string, object?>.TryGetValue(string key, [MaybeNullWhen(false)] out object? value) {
        if (_values.TryGetValue(key, out value))
            return true;
        if (TryGetOneToOneNavigation(key, out value))
            return true;
        if (MatchCollectionNavigation(key) is { } collection) {
            value = ReadLinkedTargets(collection);
            return true;
        }
        if (MatchCollectionField(key) is { } field) {
            value = new LinkedCollectionView(this, field);
            return true;
        }
        value = null;
        return false;
    }

    /// <summary>
    /// Printed <c>_permits.Add(child)</c>. The class field is the relationship.
    /// Adding a child records the store link the interpreter already keeps.
    /// </summary>
    private sealed class LinkedCollectionView(DomainEntityInstance owner, Relationship navigation) {
        public void Add(object? item) {
            if (item is not DomainEntityInstance child)
                throw new InvalidOperationException(
                    $"'{navigation.Name}' add expects a domain instance.");
            owner.LinkConstructedChild(navigation, child);
        }
    }

    private void LinkConstructedChild(Relationship navigation, DomainEntityInstance child) {
        var store = StoreOrDefault();
        if (!ReferenceEquals(child.Store, store) && !store.TryAdd(child, out var error))
            throw new InvalidOperationException(error ?? "Failed to register linked instance.");
        store.Link(navigation.Name, this, child);
        TryLinkCreateInBackReference(child);
    }

    /// <summary>
    /// Constructor collection argument. Items are linked on the relationship
    /// instead of stored as a <c>List</c> that would hide the field view.
    /// </summary>
    private void AdoptCollection(string pascalName, object? raw) {
        var navigation = MatchCollectionNavigation(pascalName);
        if (navigation is null || raw is not System.Collections.IEnumerable items || raw is string)
            return;
        foreach (var item in items) {
            if (item is DomainEntityInstance child)
                LinkConstructedChild(navigation, child);
        }
    }

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