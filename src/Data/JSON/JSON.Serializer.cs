using System.Linq;
using System.Text;

namespace Poly.Data {
    public class JSONSerializer : Serializer<JSON> {
        public override bool Serialize(StringBuilder json, JSON obj) {
            if (obj == null)
                return false;

            if (obj.IsArray) {
                json.Append('[');

                var i = 0;
                var l = obj.Count;

                foreach (var element in obj) {
                    if (element.Value == null)
                        continue;

                    var value = element.Value;
                    var serial = Data.Serializer.Get(value.GetType());

                    if (!serial.SerializeObject(json, value))
                        return false;

                    if (++i != l)
                        json.Append(',');
                }

                json.Append(']');
                return true;
            }
            else {
                json.Append('{');

                var l = obj.Count;
                foreach (var element in obj) {
                    if (element.Value == null)
                        continue;

                    var value = element.Value;
                    var serial = Data.Serializer.Get(value.GetType());

                    json.Append('"').Append(element.Key).Append("\":");

                    if (!serial.SerializeObject(json, value))
                        return false;

                    if (--l != 0)
                        json.Append(',');
                }

                json.Append('}');
                return true;
            }
        }

        public override bool Deserialize(StringIterator json, out JSON obj) {
            if (json.SelectSection('{', '}')) {
                obj = new JSON();

                while (!json.IsDone) {
                    json.ConsumeWhitespace();

                    if (!String.Deserialize(json, out string key))
                        return false;

                    json.ConsumeWhitespace();

                    if (!json.Consume(':'))
                        return false;

                    json.ConsumeWhitespace();

                    if (!DeserializeValue(json, out object value))
                        return false;

                    obj.Add(key, value);

                    json.ConsumeWhitespace();

                    if (!json.Consume(','))
                        break;
                }

                json.ConsumeSection();
                return true;
            }
            else
            if (json.SelectSection('[', ']')) {
                obj = new JSON();
                obj.IsArray = true;

                while (!json.IsDone) {
                    json.ConsumeWhitespace();

                    if (!DeserializeValue(json, out object value))
                        return false;

                    obj.Add(obj.Count.ToString(), value);

                    json.ConsumeWhitespace();

                    if (!json.Consume(','))
                        break;
                }

                json.ConsumeSection();
                return true;
            }

            obj = null;
            return false;
        }

        public override bool ValidateFormat(StringIterator json) {
            if (json.SelectSection('{', '}')) {
                while (!json.IsDone) {
                    json.ConsumeWhitespace();

                    if (!String.ValidateFormat(json))
                        return false;

                    json.ConsumeWhitespace();

                    if (!json.Consume(':'))
                        return false;

                    json.ConsumeWhitespace();

                    if (!ValidateValue(json))
                        return false;

                    json.ConsumeWhitespace();

                    if (!json.Consume(','))
                        break;
                }

                json.ConsumeSection();
                return true;
            }
            else
            if (json.SelectSection('[', ']')) {
                while (!json.IsDone) {
                    json.ConsumeWhitespace();

                    if (!ValidateValue(json))
                        return false;

                    json.ConsumeWhitespace();

                    if (!json.Consume(','))
                        break;
                }

                json.ConsumeSection();
                return true;
            }
            
            return false;
        }

        private Serializer<string> String = Get<string>();
        private Serializer Bool = Get<bool>();
        private Serializer Int = Get<int>();
        private Serializer Long = Get<long>();
        private Serializer Float = Get<float>();
        private Serializer Double = Get<double>();

        internal bool DeserializeValue(StringIterator json, out object obj) =>
            DeserializeObject(json, out obj) ||
            String.DeserializeObject(json, out obj) ||
            Bool.DeserializeObject(json, out obj) ||
            Int.DeserializeObject(json, out obj) ||
            Long.DeserializeObject(json, out obj) ||
            Float.DeserializeObject(json, out obj) ||
            Double.DeserializeObject(json, out obj);
        
        internal bool ValidateValue(StringIterator json) =>
            ValidateFormat(json) ||
            String.ValidateFormat(json) ||
            Bool.ValidateFormat(json) ||
            Int.ValidateFormat(json) ||
            Long.ValidateFormat(json) ||
            Float.ValidateFormat(json) ||
            Double.ValidateFormat(json);
    }
}