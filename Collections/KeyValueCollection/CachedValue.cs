namespace Poly.Collections {

    public delegate bool TryConvert<From, To>(From from, out To to);

    public partial class KeyValueCollection<T> {

        public class CachedValue<U> : KeyValuePair {
            protected U cached;
            
            public TryConvert<T, U> Read;
            public TryConvert<U, T> Write;

            public bool HasValue { get; private set; }

            new public U Value {
                get => cached;
                set => UpdateValue(value);
            }

            public CachedValue(string key, TryConvert<T, U> read, TryConvert<U, T> write) : base(key, default) {
                Read = read;
                Write = write;

                cached = default;
                HasValue = false;
            }

            public CachedValue(string key, T value, TryConvert<T, U> read, TryConvert<U, T> write) : base(key, default) {
                Read = read;
                Write = write;

                cached = default;
                HasValue = false;

                Set(value);
            }

            public override void Set(T _) {
                if (Read(_, out U result)) {
                    cached = result;
                    HasValue = true;
                }
                else {
                    cached = default;
                    HasValue = false;
                }
            }

            private void UpdateValue(U _) {
                if (Write(_, out T result)) {
                    cached = _;
                    value = result;
                    HasValue = true;
                }
                else {
                    cached = default;
                    _ = default;
                    HasValue = false;
                }
            }
        }
    }
}