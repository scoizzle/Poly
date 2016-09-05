using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Poly.Data {
    public partial class KeyValueCollection<T> {
        public IEnumerable<string> Keys
        {
            get
            {
                foreach (var group in List) {
                        foreach (var pair in group.List) {
                        yield return pair.Key;
                    }
                }
            }
        }

        public IEnumerable<T> Values
        {
            get
            {
                foreach (var group in List) {
                    foreach (var pair in group.List) {
                        yield return pair.Value;
                    }
                }
            }
        }

        public IEnumerable<KeyValuePair> KeyValuePairs
        {
            get
            {
                foreach (var group in List) {
                        foreach (var pair in group.List) {
                        yield return pair;
                    }
                }
            }
        }

        public IEnumerator<KeyValuePair> GetEnumerator() {
            return new Enumerator(this);
        }

        IEnumerator IEnumerable.GetEnumerator() {
            return (IEnumerator)new Enumerator(this);
        }
        
        public struct Enumerator : IEnumerator<KeyValuePair> {
            int index;
            KeyValuePair[] list;

            public KeyValuePair Current { get; private set; }
            object IEnumerator.Current { get { return Current; } }

            internal Enumerator(KeyValueCollection<T> Coll) {
                index = 0;
                Current = null;

                list = Coll.KeyValuePairs.ToArray();
            }

            public void Dispose() { }

            public bool MoveNext() {
                if (index < list.Length) {
                    Current = list[index];
                    index++;
                    return true;
                }
                else {
                    Current = null;
                    return false;
                }
            }
            

            void IEnumerator.Reset() {
                index = 0;
                Current = default(KeyValuePair);
            }
        }
    }
}
