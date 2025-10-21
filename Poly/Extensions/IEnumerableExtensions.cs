namespace Poly;

public static class IEnumerableExtensions {
    /// <summary>
    /// Iterates through the entire enumerable and applies the action provided to every item.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="set"></param>
    /// <param name="action"></param>
    public static void ForEach<T>(this IEnumerable<T> set, Action<T> action) {
        ArgumentNullException.ThrowIfNull(set);
        ArgumentNullException.ThrowIfNull(action);

        foreach (var value in set)
            action(value);
    }
}