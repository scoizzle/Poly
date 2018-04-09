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

            public IEnumerator<Item> GetEnumerator() =>
                Items.Elements.GetEnumerator();
            
            IEnumerator IEnumerable.GetEnumerator() =>
                Items.Elements.GetEnumerator();
        }
    }
}