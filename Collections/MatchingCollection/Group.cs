using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Poly.Collections {
    public partial class MatchingCollection<T> {
        public class Group : IEnumerable<Item> {
            internal Matcher Matcher;

            internal List<Group> Groups;
            internal List<Item> Items;

            public Group(string format) {
                Matcher = new Matcher(format);
                Groups = new List<Group>();
                Items = new List<Item>();
            }


            public IEnumerable<Matcher> Keys {
                get => Items.Select(_ => _.Matcher);
            }

            public IEnumerable<T> Values {
                get => Items.Select(_ => _.Value);
            }

            public IEnumerator<Item> GetEnumerator() =>
                Items.GetEnumerator();
            
            IEnumerator IEnumerable.GetEnumerator() =>
                Items.GetEnumerator();
        }
    }
}