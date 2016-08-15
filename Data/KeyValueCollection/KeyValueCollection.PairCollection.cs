using System.Collections.Generic;

namespace Poly.Data {
    public partial class KeyValueCollection<T> {
        private class PairCollection {
            public int Len;
            public List<KeyValuePair> List;

            public PairCollection(int Length) {
                Len = Length;
                List = new List<KeyValuePair>();
            }
        }
    }
}
