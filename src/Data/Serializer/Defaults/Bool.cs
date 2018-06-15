using System.Text;
using System.Linq;

namespace Poly.Data {
    public class Bool : Serializer<bool> {
        public override bool Serialize(StringBuilder json, bool value) {
            json.Append(value ? "true" : "false"); 
            return true;
        }

        public override bool Deserialize(StringIterator json, out bool value) {
            if (json.ConsumeIgnoreCase("true")) { 
                value = true; 
                return true; 
            }
            else
            if (json.ConsumeIgnoreCase("false")) { 
                value = false; 
                return true; 
            }
            else
            return value = false;
        }

        public override bool ValidateFormat(StringIterator json)  =>
            json.ConsumeIgnoreCase("true") || json.ConsumeIgnoreCase("false");
    }
}