using System;

namespace Poly.Collections {

    public partial class KeyValueCollection<T> {

        public class KeyValuePair {
            internal T value;

            public readonly string Key;

            public Func<T> OnGet;
            public Action<T> OnSet;

            public T Value {
                get => OnGet();
                set => OnSet(value);
            }

            public KeyValuePair(string k, T v) {
                Key = k;
                value = v;

                OnGet = DefaultGet();
                OnSet = DefaultSet();
            }

            Func<T> DefaultGet() =>
                () => value;

            Action<T> DefaultSet() =>
                _ => value = _;
        }
    }
}