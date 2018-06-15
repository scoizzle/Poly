using System.Text;
using System.Linq;

namespace Poly.Data {
    public class StringSerializer : Serializer<string> {
        public override bool Serialize(StringBuilder json, string text) {
            json.Append('"').Append(text).Append('"'); return true;
        }

        public override bool Deserialize(StringIterator json, out string text) {
            if (json.SelectSection('"', '"')) {
                text = json;
                json.ConsumeSection();
                return true;
            }
            text = null;
            return false;
        }

        public override bool ValidateFormat(StringIterator json) {
            if (json.SelectSection('"', '"')) {
                json.ConsumeSection();
                return true;
            }
            return false;
        }
    }
}