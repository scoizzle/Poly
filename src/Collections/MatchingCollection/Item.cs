namespace Poly.Collections {
    public partial class MatchingCollection<T> {
        public class Item {
            public Matcher Matcher;
            public T Value;

            public Item(string format, T value) {
                Matcher = new Matcher(format);
                Value = value;
            }
        }
    }
}