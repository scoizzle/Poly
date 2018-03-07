using System;

namespace Poly.Data {

    public partial class KeyValueCollection<T> {

        public class KeyValuePair {
            internal T value;

            public readonly string Key;
            public Action<T, T> OnSet = (T old_value, T new_value) => { };

            public T Value {
                get => value;
                set {
                    OnSet(this.value, value);
                    this.value = value;
                }
            }

            public KeyValuePair(string k, T v) {
                Key = k;
                value = v;
            }

            public override string ToString() {
                return $"{Key}: {Value}";
            }
        }
    }
}