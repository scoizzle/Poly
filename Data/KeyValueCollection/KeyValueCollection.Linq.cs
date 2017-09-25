using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Poly.Data {
    public partial class KeyValueCollection<T> {
        public bool ContainsValue(T Val) {
            return Values.Contains(Val);
        }

        public bool TryFind(out KeyValuePair pair, Func<string, T, bool> action) {
            foreach (var item in KeyValuePairs) {
                if (action(item.Key, item.Value)) {
                    pair = item;
                    return true;
                }
            }

            pair = default(KeyValuePair);
            return false;
        }

        public void ForEach(Action<KeyValuePair> action) {
            foreach (var pair in KeyValuePairs) {
                action(pair);
            }
        }

        public void ForEach(Action<string, T> action) {
            foreach (var pair in KeyValuePairs) {
                action(pair.Key, pair.Value);
            }
        }
    }
}
