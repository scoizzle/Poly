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

        public void ForEach(Action<string, T> action) {
            var c = List.Count;
            for (int x = 0; x < c; x++) {
                var l = List[x].List;
                var lc = l.Count;

                for (int y = 0; y < lc; y++) {
                    var i = l[y];
                    action(i.Key, i.Value);
                }
            }
        }
    }
}
