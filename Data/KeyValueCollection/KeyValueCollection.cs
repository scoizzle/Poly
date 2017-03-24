using System.Collections.Generic;

namespace Poly.Data {
    public partial class KeyValueCollection<T> : IEnumerable<KeyValueCollection<T>.KeyValuePair> {
        ManagedArray<PairCollection> List;

        public int Count { get; private set; }

        public KeyValueCollection() {
            List = new ManagedArray<PairCollection>();
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
