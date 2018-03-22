using System;

namespace Poly.Collections {

    public partial class KeyValueCollection<T> {

        public class KeyValuePair {
            internal T value;

            public readonly string Key;

            public T Value {
                get => Get();
                set => Set(value);
            }

            public KeyValuePair(string k, T v) {
                Key = k;
                value = v;
            }

            public virtual T Get() =>
                value;

            public virtual void Set(T _) =>
                value = _;
        }
    }
}