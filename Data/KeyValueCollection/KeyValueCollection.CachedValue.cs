using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Poly.Data {
    public delegate bool TryConvert<From, To>(From from, out To to);

    public partial class KeyValueCollection<T> {
        public struct CachedValue<U> {
            private T old;
            private U cached;

            public KeyValuePair     Pair;
            public TryConvert<T, U> Read;
            public TryConvert<U, T> Write;

            public U Value { 
                get {
                    if (Object.ReferenceEquals(old, Pair.Value))
                        return cached;

                    if (Read(Pair.Value, out U result)) {
                        cached = result;
                        old = Pair.Value;
                    }

                    return default(U);
                }

                set {
                    cached = value;

                    Pair.Value = old = Write(value, out T result) ? result
                                                                  : default(T);
                }
            }

            public CachedValue(KeyValuePair pair, TryConvert<T, U> read, TryConvert<U, T> write) {
                Pair = pair;
                Read = read;
                Write = write;

                old = default(T);
                cached = default(U);
            }

			public override string ToString() {
                return Pair.ToString();
			}
        }
    }
}
