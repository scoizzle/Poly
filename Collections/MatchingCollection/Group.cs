using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Poly.Collections {
    public partial class MatchingCollection<T> {
        public class Group : IEnumerable<Item> {
            internal Matcher Matcher;

            internal ManagedArray<Group> Groups;
            internal ManagedArray<Item> Items;

            public Group(string format) {
                Matcher = new Matcher(format);
                Groups = new ManagedArray<Group>();
                Items = new ManagedArray<Item>();
            }


            public IEnumerable<Matcher> Keys {
                get => Items.Select(_ => _.Matcher);
            }

            public IEnumerable<T> Values {
                get => Items.Select(_ => _.Value);
            }

            public IEnumerator<Item> GetEnumerator() {
                return new Enumerator(this);
            }

            IEnumerator IEnumerable.GetEnumerator() {
                return new Enumerator(this);
            }

            public struct Enumerator : IEnumerator<Item> {
                private int Index, Count;
                private Item[] Elements;

                public Item Current { get { return Elements[Index]; } }
                object IEnumerator.Current { get { return Current; } }

                internal Enumerator(Group collection) {
                    Elements = collection.Items.ToArray();
                    Index = -1;
                    Count = Elements.Length;
                }

                public void Dispose() {
                }

                public bool MoveNext() {
                    return (++Index < Count);
                }

                void IEnumerator.Reset() {
                    Index = -1;
                }
            }
        }
    }
}