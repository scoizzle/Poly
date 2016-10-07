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
            int indexMajor, indexMinor;
            KeyValueCollection<T> list;
            PairCollection pc;

            public KeyValuePair Current { get; private set; }
            object IEnumerator.Current { get { return Current; } }

            internal Enumerator(KeyValueCollection<T> Coll) {
                indexMajor = indexMinor = 0;
                Current = null;

                list = Coll;

                if (Coll.Count == 0) pc = null;
                else {
                    pc = list.List.Elements[indexMinor];
                }
            }

            public void Dispose() { }

            public bool MoveNext() {
                if (pc == null) return false;

                if (indexMinor < pc.List.Count) {
                    Current = pc.List.Elements[indexMinor];
                    indexMinor++;
                    return true;
                }

                indexMinor = 0;
                indexMajor++;
                
                if (indexMajor < list.List.Count) {
                    pc = list.List.Elements[indexMajor];

                    if (pc != null) {
                        Current = pc.List.Elements[indexMinor];
                        indexMinor++;
                        return true;
                    }
                }

                Current = null;
                return false;
            }

            void IEnumerator.Reset() {
                indexMajor = indexMinor = 0;
                Current = null;
                list = null;
                pc = null;
            }
        }
    }
}
