namespace Poly {
    using Data;
    using System.Text;

    public partial class Matcher {
        public class RawStringSerializer : Serializer<string> {
            public RawStringSerializer() : base(false) { }
            
            public override bool Deserialize(StringIterator json, out string obj) {
                obj = json.ToString();
                return true;
            }

            public override bool Serialize(StringBuilder json, string obj) {
                json.Append(obj);
                return true;
            }

            public override bool ValidateFormat(StringIterator json) => 
                true;

            public static RawStringSerializer Singleton = new RawStringSerializer();
        }
    }
}