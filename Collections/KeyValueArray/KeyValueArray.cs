using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;

namespace Poly.Collections {
    public partial class KeyValueArray<TKey, TValue> {
        int count;
        ManagedArray<TKey> keys;
        ManagedArray<TValue> values;

        public Func<TKey, TKey, bool> KeysAreEqual;
        public Func<TValue, TValue, bool> ValuesAreEqual;

        public KeyValueArray(Func<TKey, TKey, bool> keyComparison, Func<TValue, TValue, bool> valueComparison) {
            keys = new ManagedArray<TKey>();
            values = new ManagedArray<TValue>();
            KeysAreEqual = keyComparison;
            ValuesAreEqual = valueComparison;
        }

        public IEnumerable<KeyValuePair<TKey, TValue>> KeyValuePairs {
            get {
                var key_array = keys;
                var val_array = values;

                for (var i = 0; i < count; i++) 
                    yield return new KeyValuePair<TKey, TValue>(key_array[i], val_array[i]);
            }
        }

        public int IndexOf(TKey key) {
            var key_array = keys;

            for (var i = 0; i < count; i++)
                if (KeysAreEqual(key_array[i], key))
                    return i;

            return -1;
        }
    }
}
