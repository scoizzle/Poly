namespace Poly.Data {

    public delegate bool TryConvert<From, To>(From from, out To to);

    public partial class KeyValueCollection<T> {

        public class CachedValue<U> {
            private U cached;

            public KeyValuePair Pair;
            public TryConvert<T, U> Read;
            public TryConvert<U, T> Write;

            public bool HasValue { get; private set; }

            public U Value {
                get => cached;
                set => UpdateValue(value);
            }

            public CachedValue(KeyValuePair pair, TryConvert<T, U> read, TryConvert<U, T> write) {
                Pair = pair;
                Read = read;
                Write = write;

                pair.OnSet = OnPairValueSet;

                cached = default;
                HasValue = false;
            }

            private void OnPairValueSet(T old_value, T new_value) {
                if (Read(new_value, out U result)) {
                    cached = result;
                    HasValue = true;
                }
                else {
                    cached = default;
                    HasValue = false;
                }
            }

            private void UpdateValue(U value) {
                if (Write(value, out T result)) {
                    cached = value;
                    Pair.value = result;
                    HasValue = true;
                }
                else {
                    cached = default;
                    Pair.value = default;
                    HasValue = false;
                }
            }

            public override string ToString() {
                return Pair.ToString();
            }
        }
    }
}