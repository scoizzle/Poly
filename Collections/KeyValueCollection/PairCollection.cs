namespace Poly.Collections {

    public partial class KeyValueCollection<T> {
        public class PairCollection {
            public readonly int Length;
            public readonly ManagedArray<KeyValuePair> List;

            public PairCollection(int length) {
                Length = length;
                List = new ManagedArray<KeyValuePair>();
            }
        }
    }
}