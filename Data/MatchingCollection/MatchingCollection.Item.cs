namespace Poly.Data {
    public partial class MatchingCollection<T> {
        private class Item {
            public Matcher Matcher { get; set; }
            public T Value { get; set; }

            public Item(string format, T value) {
                Matcher = new Matcher(format);
                Value = value;
            }
        }
    }
}