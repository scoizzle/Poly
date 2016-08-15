using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Poly.Data {
    public partial class KeyValueCollection<T> {
        public class KeyValuePair {
            public string Key;
            public T Value;

            public KeyValuePair(string k, T v) {
                Key = k;
                Value = v;
            }

			public override string ToString() {
				return string.Format("{0}: {1}", Key, Value);
			}
        }
    }
}
