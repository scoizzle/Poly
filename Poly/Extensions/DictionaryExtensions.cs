namespace Poly.Extensions;

public static class DictionaryExtensions {
    public static TValue GetOrAdd<TKey, TValue>(
        this Dictionary<TKey, TValue> dictionary,
        TKey key,
        Func<TKey, TValue> valueFactory) where TKey : notnull {
        if (!dictionary.TryGetValue(key, out var value)) {
            value = valueFactory(key);
            dictionary[key] = value;
        }
        return value;
    }


    public static TValue GetOrAdd<TKey, TValue, TContext>(
        this Dictionary<TKey, TValue> dictionary,
        TKey key,
        Func<TKey, TContext, TValue> valueFactory,
        TContext context) where TKey : notnull {
        if (!dictionary.TryGetValue(key, out var value)) {
            value = valueFactory(key, context);
            dictionary[key] = value;
        }
        return value;
    }
}