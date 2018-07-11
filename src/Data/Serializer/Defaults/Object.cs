using System.Collections.Generic;
using System.Text;
using System.Linq;

namespace Poly.Data {
    public class Object<T> : Serializer<T> {
        Serializer<string> String;
        Dictionary<string, Member> Members;

        public Object() {
            Members = TypeInfo.Members;
            String = Get<string>();
        }

        public override bool Serialize(StringBuilder json, T obj) {
            json.Append('{');

            var count = Members.Count;
            foreach (var pair in Members) {
                json.Append('"').Append(pair.Key).Append("\":");
                
                var member = pair.Value;
                var value = member.Get(obj);

                if (!member.Serializer.SerializeObject(json, value))
                    return false;

                if (--count != 0)
                    json.Append(',');
            }

            json.Append('}');
            return true;
        }

        public override bool Deserialize(StringIterator json, out T obj) {
            if (!json.SelectSection('{', '}')) {
                obj = default(T);
                return false;
            }

            obj = (T)TypeInfo.CreateInstance();

            while (!json.IsDone) {
                json.ConsumeWhitespace();

                if (!String.Deserialize(json, out string name))
                    return false;

                if (!Members.TryGetValue(name, out Member member))
                    return false;

                json.ConsumeWhitespace();

                if (!json.Consume(':'))
                    return false;

                json.ConsumeWhitespace();

                if (!member.Serializer.DeserializeObject(json, out object value))
                    return false;

                member.Set(obj, value);

                json.ConsumeWhitespace();

                if (!json.Consume(',')) {
                    json.ConsumeSection();
                    break;
                }
            }

            return true;
        }

        public override bool ValidateFormat(StringIterator json) {
            if (!json.SelectSection('{', '}'))
                return false;

            while (!json.IsDone) {
                json.ConsumeWhitespace();

                if (!String.Deserialize(json, out string name))
                    return false;

                if (!Members.TryGetValue(name, out Member member))
                    return false;

                json.ConsumeWhitespace();

                if (!json.Consume(':'))
                    return false;

                json.ConsumeWhitespace();

                if (!member.Serializer.ValidateFormat(json))
                    return false;

                json.ConsumeWhitespace();

                if (!json.Consume(',')) 
                    break;
            }

            json.ConsumeSection();
            return true;
        }
    }
}