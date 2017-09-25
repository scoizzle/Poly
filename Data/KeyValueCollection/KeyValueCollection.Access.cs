using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Poly.Data {
    public partial class KeyValueCollection<T> {
        public bool Add(string key, T value) {
            PairCollection collection;
            KeyValuePair   pair;

            if (TryGetCollection(key.Length, out collection)) {
                if (TryGetStorage(collection, key, out pair)) {
                    return false;
                }

                pair = new KeyValuePair(key, value);
                collection.List.Add(pair);
                Count++;
            }
            else {
                pair = new KeyValuePair(key, value);
                collection = new PairCollection(key.Length);

                collection.List.Add(pair);
                List.Add(collection);
                Count ++;
            }
            
            return true;
        }

        public bool Remove(string key) {
            PairCollection collection;
            KeyValuePair   pair;

            if (TryGetCollection(key.Length, out collection)) {
                if (TryGetStorage(collection, key, out pair)) {
                    collection.List.Remove(pair);
                    Count --;
                    return true;
                }
            }

            return false;
        }

        public void Clear() {
            List.Clear();
            Count = 0;
        }

        public bool ContainsKey(string key) {
            PairCollection collection;
            KeyValuePair   pair;

            if (TryGetCollection(key.Length, out collection)) {
                if (TryGetStorage(collection, key, out pair)) {
                    return true;
                }
            }

            return false;
        }

        public T Get(string key) {
            return TryGetValue(key, out T value) ? value 
                                                 : default(T);
        }

        public void Set(string key, T value) {
            PairCollection collection;
            KeyValuePair   pair;

            if (TryGetCollection(key.Length, out collection)) {
                if (TryGetStorage(collection, key, out pair)) {
                    pair.Value = value;
                }
                else {
                    pair = new KeyValuePair(key, value);
                    collection.List.Add(pair);
                    Count++;
                }
            }
            else {
                pair = new KeyValuePair(key, value);
                collection = new PairCollection(key.Length);

                collection.List.Add(pair);
                List.Add(collection);
                Count ++;
            }
        }

        public KeyValuePair GetStorage(string key) {
            PairCollection collection;
            KeyValuePair   pair;

            if (TryGetCollection(key.Length, out collection)) {
                if (TryGetStorage(collection, key, out pair)) {
                    return pair;
                }
                else {
                    pair = new KeyValuePair(key, default(T));
                    collection.List.Add(pair);
                    Count++;
                    return pair;
                }
            }
            else {
                pair = new KeyValuePair(key, default(T));
                collection = new PairCollection(key.Length);

                collection.List.Add(pair);
                List.Add(collection);
                Count ++;
                return pair;
            }
        }

        public CachedValue<U> GetCachedStorage<U>(string key, TryConvert<T, U> read, TryConvert<U, T> write) {
            return new CachedValue<U>(GetStorage(key), read, write);
        }

        public bool TryGetValue(string key, out T value) {
            PairCollection      collection;

            if (TryGetCollection(key.Length, out collection)) {
                return TryGetValue(collection, key, out value);
            }

            value = default(T);
            return false;
        }

        private bool TryGetValue(PairCollection collection, string key, out T value) {
            if (TryGetStorage(collection, key, out KeyValuePair pair)) {
                value = pair.Value;
                return true;
            }

            value = default(T);
            return false;
        }

        private bool TryGetStorage(PairCollection collection, string key, out KeyValuePair pair) {
            var list = collection.List;
            var length = collection.Length;
            var len = list.Count;

            for (int i = 0; i < len; i++) {
                var element = list[i];

                if (StringExtensions.Compare(element.Key, 0, key, 0, length)) {
                    pair = element;
                    return true;
                }
            }

            pair = default(KeyValuePair);
            return false;
        }

        private bool TryGetCollection(int length, out PairCollection collection) {
            var list = List;
            var len = list.Count;

            for (int i = 0; i < len; i++) {
                var element = list[i];

                if (element.Length == length) {
                    collection = element;
                    return true;
                }
            }

            collection = default(PairCollection);
            return false;
        }
    }
}
