namespace Poly.Data {

    public partial class MatchingCollection<T> {

        private class Group {
            public Matcher Matcher { get; set; }

            public ManagedArray<Group> Groups;
            public ManagedArray<Item> Items;

            public Group(string format) {
                Matcher = new Matcher(format);
                Groups = new ManagedArray<Group>();
                Items = new ManagedArray<Item>();
            }
        }
    }
}