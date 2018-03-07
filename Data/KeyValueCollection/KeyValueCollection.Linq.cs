using System;
using System.Linq;

namespace Poly.Data {

    public partial class KeyValueCollection<T> {

        public bool ContainsValue(T Val) {
            return Values.Contains(Val);
        }

        public void All(Action<KeyValuePair> action) {
            foreach (var pair in KeyValuePairs) {
                action(pair);
            }
        }

        public void All(Action<string, T> action) {
            foreach (var pair in KeyValuePairs) {
                action(pair.Key, pair.Value);
            }
        }
    }
}