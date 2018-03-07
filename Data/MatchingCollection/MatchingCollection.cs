namespace Poly.Data {

    public partial class MatchingCollection<T> {
        private Group Items;

        public int Count { get; private set; }
        public char KeySeperatorCharacter { get; private set; }

        public MatchingCollection(char key_seperator = '/') {
            Items = new Group("");
            KeySeperatorCharacter = key_seperator;
        }
    }
}