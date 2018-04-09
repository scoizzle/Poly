using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;

namespace Poly.Collections {
    public partial class KeyValueArray<TKey, TValue> : IDictionary<TKey, TValue> {
        public TValue this[TKey key] {
            get {
                var index = IndexOf(key);

                if (index == -1)
                    return default;

                return values[index];
            }

            set {
                var index = IndexOf(key);

                if (index == -1)
                    Add(key, value);
                else
                    values[index] = value;
            }
        }

        public int Count => count;

        public ICollection<TKey> Keys => keys;

        public ICollection<TValue> Values => values;

        bool ICollection<KeyValuePair<TKey, TValue>>.IsReadOnly => false;

        public void Add(TKey key, TValue value) {
            var index = IndexOf(key);

            if (index == -1) {
                keys.Add(key);
                values.Add(value);
                count++;
            }
            else throw new ArgumentException($"An item with the same key has already been added. Key: {key}");
        }

        public void Add(KeyValuePair<TKey, TValue> item) {
            keys.Add(item.Key);
            values.Add(item.Value);
            count++;
        }

        public void Clear() {
            keys.Clear();
            values.Clear();
            count = 0;
        }

        public bool Contains(KeyValuePair<TKey, TValue> item) {
            var key_array = keys;
            var val_array = values;

            for (var i = 0; i < count; i++) {
                if (KeysAreEqual(key_array[i], item.Key)) {
                    if (ValuesAreEqual(val_array[i], item.Value))
                        return true;
                    else
                        break;
                }
            }

            return false;
        }

        public bool ContainsKey(TKey key) {
            return IndexOf(key) != -1;
        }

        public void CopyTo(KeyValuePair<TKey, TValue>[] array, int arrayIndex) {
            foreach (var item in KeyValuePairs)
                array[arrayIndex++] = item;
        }

        public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator() {
            return KeyValuePairs.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator() {
            return KeyValuePairs.GetEnumerator();
        }

        public bool Remove(TKey key) {
            var key_array = keys;

            for (var i = 0; i < count; i++) {
                if (KeysAreEqual(key_array[i], key)) {
                    keys.RemoveAt(i);
                    values.RemoveAt(i);
                    count--;
                }
            }

            return false;
        }

        public bool Remove(KeyValuePair<TKey, TValue> item) {
            var key_array = keys;
            var val_array = values;

            for (var i = 0; i < count; i++) {
                if (KeysAreEqual(key_array[i], item.Key)) {
                    if (ValuesAreEqual(val_array[i], item.Value)) {
                        keys.RemoveAt(i);
                        values.RemoveAt(i);
                        count--;
                        return true;
                    }
                    else break;
                }
            }

            return false;
        }

        public bool TryGetValue(TKey key, out TValue value) {
            var index = IndexOf(key);

            if (index == -1) {
                value = default;
                return false;
            }

            value = values[index];
            return true;
        }
    }
}
