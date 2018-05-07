using System.Linq;
using System.Text;

namespace Poly.Data {
    public partial class JSON {
        public static Serializer<JSON> Serializer = new Serializer<JSON>(Serialize, Deserialize);

        public static implicit operator JSON(string text) =>
            Deserialize(text, out JSON value) ? value : default;

        private static bool Serialize(StringBuilder json, JSON obj) {
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
                    var serial = Data.Serializer.GetCached(value.GetType());

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

                var i = 0;
                var l = obj.Count;

                foreach (var element in obj) {
                    if (element.Value == null)
                        continue;

                    var value = element.Value;
                    var serial = Data.Serializer.GetCached(value.GetType());

                    json.Append('"').Append(element.Key).Append("\":");

                    if (!serial.SerializeObject(json, value))
                        return false;

                    if (++i != l)
                        json.Append(',');
                }

                json.Append('}');
                return true;
            }
        }

        private static bool Deserialize(StringIterator json, out JSON obj) {
            if (json.SelectSection('{', '}')) {
                obj = new JSON();

                while (!json.IsDone) {
                    json.ConsumeWhitespace();

                    if (!Data.Serializer.String.TryDeserialize(json, out string key))
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

        private static bool DeserializeValue(StringIterator json, out object obj) {
            return
                Serializer.DeserializeObject(json, out obj) ||
                Data.Serializer.String.DeserializeObject(json, out obj) ||
                Data.Serializer.Boolean.DeserializeObject(json, out obj) ||
                Data.Serializer.Int.DeserializeObject(json, out obj) ||
                Data.Serializer.Long.DeserializeObject(json, out obj) ||
                Data.Serializer.Float.DeserializeObject(json, out obj) ||
                Data.Serializer.Double.DeserializeObject(json, out obj);
        }
    }
}