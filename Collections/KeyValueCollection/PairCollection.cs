namespace Poly.Collections {

    public partial class KeyValueCollection<T> {

        private class PairCollection {
            public int Length;
            public ManagedArray<KeyValuePair> List;

            public PairCollection(int length) {
                Length = length;
                List = new ManagedArray<KeyValuePair>();
            }
        }
    }
}