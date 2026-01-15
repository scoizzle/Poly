namespace Poly.Extensions;

public static class DictionaryExtensions {
    extension<TKey, TValue>(Dictionary<TKey, TValue> dictionary) where TKey : notnull {
        /// <summary>
        /// Gets the value associated with the specified key. If the key does not exist, invokes the valueFactory to create the value, adds it to the dictionary, and returns it.
        /// </summary>
        /// <param name="key">The key whose value to get or add.</param>
        /// <param name="valueFactory">The function to create a value if the key does not exist.</param>
        /// <returns>The value associated with the specified key.</returns>
        public TValue GetOrAdd(TKey key, Func<TKey, TValue> valueFactory)
        {
            if (!dictionary.TryGetValue(key, out var value)) {
                value = valueFactory(key);
                dictionary[key] = value;
            }
            return value;
        }

        /// <summary>
        /// Gets the value associated with the specified key. If the key does not exist, invokes the valueFactory to create the value, adds it to the dictionary, and returns it.
        /// </summary>
        /// <param name="key">The key whose value to get or add.</param>
        /// <param name="valueFactory">The function to create a value if the key does not exist.</param>
        /// <param name="context">The context to pass to the valueFactory.</param>
        /// <returns>The value associated with the specified key.</returns>
        public TValue GetOrAdd<TContext>(TKey key, Func<TKey, TContext, TValue> valueFactory, TContext context)
        {
            if (!dictionary.TryGetValue(key, out var value)) {
                value = valueFactory(key, context);
                dictionary[key] = value;
            }
            return value;
        }
    }
}