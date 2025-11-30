namespace Poly.Extensions;

public static class DictionaryExtensions {
    extension<TKey, TValue>(Dictionary<TKey, TValue> dictionary) where TKey : notnull {
        public TValue GetOrAdd(TKey key, Func<TKey, TValue> valueFactory) {
            if (!dictionary.TryGetValue(key, out var value)) {
                value = valueFactory(key);
                dictionary[key] = value;
            }
            return value;
        }


        public TValue GetOrAdd<TContext>(TKey key, Func<TKey, TContext, TValue> valueFactory, TContext context) {
            if (!dictionary.TryGetValue(key, out var value)) {
                value = valueFactory(key, context);
                dictionary[key] = value;
            }
            return value;
        }
    }
}