using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Poly.Data {
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

        public IEnumerable<KeyValueCollection<T>.KeyValuePair> KeyValuePairs
        {
            get
            {
                var list = List;
                var count = list.Count;

                for (var i = 0; i < count; i++) {
                    var collection = list[i];
                    var collection_list = collection.List;
                    var collection_count = collection_list.Count;

                    for (var j = 0; j < collection_count; j++) {
                        yield return collection_list[j];
                    }
                }
            }
        }

        public IEnumerator<KeyValueCollection<T>.KeyValuePair> GetEnumerator() {
            return new Enumerator(this);
        }

        IEnumerator IEnumerable.GetEnumerator() {
            return (IEnumerator)new Enumerator(this);
        }
        
        public struct Enumerator : IEnumerator<KeyValueCollection<T>.KeyValuePair> {
            int Index, Count;
            KeyValuePair[] Pairs;
            KeyValueCollection<T> Collection;

            public KeyValuePair Current { get; private set; }
            object IEnumerator.Current { get { return Current; } }

            internal Enumerator(KeyValueCollection<T> collection) {
                Collection = collection;

                Pairs = collection.KeyValuePairs.ToArray();
                Count = Pairs.Length;

                if (Count > 0){
                    Current = Pairs[0];
                    Index = 1;
                }
                else {
                    Current = default(KeyValuePair);
                    Index = 0;
                }
            }

            public void Dispose() { }

            public bool MoveNext() {
                if (Index < Count) {
                    Current = Pairs[Index++];
                    return true;
                }
                
                return false;
            }

            void IEnumerator.Reset() {
                Index = 0;
            }
        }
    }
}
