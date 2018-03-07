using System.Collections;

namespace Poly.Data {

    public partial class JSON : KeyValueCollection<object> {
        public char KeySeperatorCharacter = '.';

        public bool IsArray = false;

        public void Add(object obj) {
            Add(Count.ToString(), obj);
        }

        public void Add(IEnumerable Elements) {
            foreach (var e in Elements)
                Add(e);
        }

        public override string ToString() {
            return Serializer.Serialize(this);
        }

        public static JSON operator +(JSON Js, object Obj) {
            Js.Add(Obj);
            return Js;
        }

        public static JSON operator -(JSON Js, string Key) {
            Js.Remove(Key);
            return Js;
        }
    }
}