namespace Poly.Collections {

    public partial class KeyValueCollection<T> {

        public class KeyArrayPair : KeyValuePair {
            internal int Index;

            public KeyArrayPair(string key, params T[] values) : base(key, default) {
                Values = new ManagedArray<T>();
                Values.Add(values);
                Index = -1;
            }

            public ManagedArray<T> Values { get; private set; }
            
            public override T Get() =>
                Values[Index];

            public override void Set(T _) =>
                Values.Add(_);

            internal bool Next() =>
                (++Index < Values.Count);

            internal void Reset() =>
                Index = -1;
        }
    }
}
