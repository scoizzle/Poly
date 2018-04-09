using System.Collections.Generic;

namespace Poly.Collections {

    public partial class KeyValueCollection<T> {
        public class KeyArrayPair : KeyValuePair {
            public KeyArrayPair(string key, params T[] values) : base(key, default) {
                Values = new ManagedArray<T>();
                Values.Add(values);
            }

            public ManagedArray<T> Values { get; private set; }

            //public override T Value {
            //    get => default;
            //    set => Values.Add(value);
            //}

            public virtual IEnumerable<KeyValuePair> GetEnumerator() {
                var elements = Values;
                var count = Values.Count;

                for (var i = 0; i < count; i++) {
                    yield return new KeyValuePair(Key, elements[i]);
                }
            }

            protected void SetStorage(KeyValueCollection<T> collection) {
                collection.SetStorage(Key, this);
            }
        }
    }
}
