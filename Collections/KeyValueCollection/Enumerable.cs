using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Poly.Collections {

    public partial class KeyValueCollection<T> {

        public IEnumerable<string> Keys {
            get {
                return from pair in KeyValuePairs select pair.Key;
            }
        }

        public IEnumerable<T> Values {
            get {
                return from pair in KeyValuePairs select pair.Value;
            }
        }

        public IEnumerable<KeyValuePair> KeyValuePairs {
            get {
                var list = List;
                var count = list.Count;

                for (var i = 0; i < count; i++) {
                    var collection = list[i];
                    var collection_list = collection.List;
                    var collection_elements = collection_list.Elements;
                    var collection_count = collection_list.Count;

                    for (var j = 0; j < collection_count; j++) {
                        yield return collection_elements[j];
                    }
                }
            }
        }

        public IEnumerator<KeyValuePair> GetEnumerator() {
            return new Enumerator(this);
        }

        IEnumerator IEnumerable.GetEnumerator() {
            return new Enumerator(this);
        }

        public struct Enumerator : IEnumerator<KeyValuePair> {
            private int Index, Count;
            private KeyValuePair[] Pairs;
            private KeyArrayPair CurrentArray;

            public KeyValuePair Current { get; private set; }
            object IEnumerator.Current { get { return Current; } }

            internal Enumerator(KeyValueCollection<T> collection) {
                Pairs = collection.KeyValuePairs.ToArray();
                Count = Pairs.Length;

                CurrentArray = default;
                Current = default;
                Index = -1;
            }

            public void Dispose() { }

            public bool MoveNext() {
                if (CurrentArray != null) {
                    if (CurrentArray.Next())
                        return true;

                    CurrentArray = null;
                }

                var not_out_of_bounds = (++Index < Count);

                if (not_out_of_bounds) {
                    var next = Pairs[Index];

                    if (next is KeyArrayPair array && array.Next()) {
                        CurrentArray = array;
                    }

                    Current = next;
                    return true;
                }

                return false;
            }

            void IEnumerator.Reset() {
                Index = -1;
            }
        }
    }
}