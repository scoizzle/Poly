namespace Poly.Collections {

    public partial class KeyValueCollection<T> {
        public bool Add(string key, T value) {
            if (TryGetStorage(key, out PairCollection collection, out KeyValuePair pair)) {
                if (pair is KeyArrayPair array) {
                    array.Values.Add(value);
                    return true;
                }

                return false;
            }

            ValidateCollection(key, ref collection);

            pair = new KeyValuePair(key, value);
            collection.List.Add(pair);
            Count++;
            return true;
        }

        public bool Remove(string key) {
            if (TryGetStorage(key, out PairCollection collection, out KeyValuePair pair)) {
                collection.List.Remove(pair);
                Count--;
                return true;
            }

            return false;
        }

        public void Clear() {
            List.Clear();
            Count = 0;
        }

        public void CopyTo(KeyValueCollection<T> target) {
            foreach (var pair in this)
                target.Set(pair.Key, pair.value);
        }

        public bool ContainsKey(string key) =>
            TryGetStorage(key, out PairCollection collection, out KeyValuePair pair);

        public T Get(string key) =>
            TryGetStorage(key, out PairCollection collection, out KeyValuePair pair) ?
                pair.value : default;

        public void Set(string key, T value) =>
            TrySet(key, value);

        public bool TrySet(string key, T value) {
            if (TryGetStorage(key, out PairCollection collection, out KeyValuePair pair)) {
                pair.Value = value;
                return true;
            }

            ValidateCollection(key, ref collection);

            pair = new KeyValuePair(key, value);
            collection.List.Add(pair);
            Count++;
            return true;
        }

        public KeyValuePair GetStorage(string key) {
            if (TryGetStorage(key, out PairCollection collection, out KeyValuePair pair))
                return pair;
            
            ValidateCollection(key, ref collection);

            pair = new KeyValuePair(key, default);
            collection.List.Add(pair);
            Count++;
            return pair;
        }

        public KeyArrayPair GetArrayStorage(string key) {
            if (TryGetStorage(key, out PairCollection collection, out KeyValuePair pair) && pair is KeyArrayPair array)
                return array;
            
            ValidateCollection(key, ref collection);

            array = new KeyArrayPair(key);
            collection.List.Add(array);
            Count++;
            return array;
        }

        public CachedValue<U> GetCachedStorage<U>(string key, TryConvert<T, U> read, TryConvert<U, T> write) {
            if (TryGetStorage(key, out PairCollection collection, out KeyValuePair pair))
                return new CachedValue<U>(key, pair.value, read, write);
            
            ValidateCollection(key, ref collection);

            var cached = new CachedValue<U>(key, read, write);
            collection.List.Add(cached);
            Count++;
            return cached;
        }

        public bool TryGetValue(string key, out T value) {
            if (TryGetStorage(key, out PairCollection collection, out KeyValuePair pair)) {
                value = pair.Value;
                return true;
            }

            value = default;
            return false;
        }

        private bool TryGetStorage(string key, out PairCollection collection, out KeyValuePair pair) {
            if (TryGetCollection(key.Length, out collection))
                return TryGetStorage(collection, key, out pair);

            pair = default;
            return false;
        }

        private bool TryGetStorage(PairCollection collection, string key, out KeyValuePair pair) {
            var length = collection.Length;
            var list = collection.List;
            var len = list.Count;

            for (int i = 0; i < len; i++) {
                var element = list.Elements[i];

                if (CompareStrings(element.Key, 0, key, 0, length)) {
                    pair = element;
                    return true;
                }
            }

            pair = default;
            return false;
        }

        private bool TryGetCollection(int length, out PairCollection collection) {
            var list = List;
            var len = list.Count;

            for (int i = 0; i < len; i++) {
                var element = list.Elements[i];

                if (element.Length == length) {
                    collection = element;
                    return true;
                }
            }

            collection = default;
            return false;
        }

        private void ValidateCollection(string key, ref PairCollection collection) =>
            ValidateCollection(key.Length, ref collection);

        private void ValidateCollection(int length, ref PairCollection collection) {
            if (collection != null)
                return;

            if (TryGetCollection(length, out collection))
                return;

            collection = new PairCollection(length);
            List.Add(collection);
        }
    }
}