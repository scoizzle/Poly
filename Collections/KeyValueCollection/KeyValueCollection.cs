using System;
using System.Collections.Generic;

namespace Poly.Collections {

    public partial class KeyValueCollection<T> : IEnumerable<KeyValueCollection<T>.KeyValuePair> {
        private ManagedArray<PairCollection> List;

        public delegate bool StringComparisonDelegate(string text, int index, string sub, int sub_index, int length);

        public StringComparisonDelegate CompareStrings = StringExtensions.Compare;

        public int Count { get; private set; }

        public KeyValueCollection() : this(StringExtensions.Compare) {
            List = new ManagedArray<PairCollection>();
        }

        public KeyValueCollection(T[] items, Func<T, string> get_key) : this() {
            foreach (var item in items)
                Add(get_key(item), item);
        }

        public KeyValueCollection(StringComparisonDelegate string_compare) {
            List = new ManagedArray<PairCollection>();
            CompareStrings = string_compare;
        }

        public T this[string Key] {
            get {
                return Get(Key);
            }
            set {
                Set(Key, value);
            }
        }
    }
}