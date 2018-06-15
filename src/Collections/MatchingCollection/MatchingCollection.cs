namespace Poly.Collections {

    public partial class MatchingCollection<T> {
        Group storage;
        
        public MatchingCollection(char key_seperator) {
            KeySeperatorCharacter = key_seperator;
            storage = new Group(string.Empty);
        }

        public int Count { get; private set; }
        public char KeySeperatorCharacter { get; private set; }
    }
}