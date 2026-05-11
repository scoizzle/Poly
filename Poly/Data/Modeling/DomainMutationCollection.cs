namespace Poly.Data.Modeling;

internal static class DomainMutationCollection {
    public static int RemoveAt<T>(List<T> items, T item) {
        ArgumentNullException.ThrowIfNull(items);

        var index = items.IndexOf(item);
        if (index < 0) {
            throw new InvalidOperationException($"Cannot remove '{typeof(T).Name}' because it is not present in the collection.");
        }

        items.RemoveAt(index);
        return index;
    }

    public static void Restore<T>(List<T> items, T item, int index) {
        ArgumentNullException.ThrowIfNull(items);

        if (index < 0 || index > items.Count) {
            throw new InvalidOperationException($"Cannot restore '{typeof(T).Name}' at index {index}.");
        }

        items.Insert(index, item);
    }
}