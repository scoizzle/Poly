namespace Poly.Collections {
    using String;
    
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