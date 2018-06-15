using System.Text;
using System.Linq;

namespace Poly.Data {
    public class FloatingPointSerializer<T> : Serializer<T> {
        public delegate bool TryParseDelegate(string str, out T value);

        TryParseDelegate try_parse;

        public FloatingPointSerializer(TryParseDelegate parser) : base() {
            try_parse = parser;
        }
        
        public override bool Serialize(StringBuilder json, T value) {
            json.Append(value);
            return true;
        }

        public override bool Deserialize(StringIterator json, out T value) {
            if (json.SelectSection(_ => _.ConsumeFloatingNumeric())) {
                if (try_parse(json, out value)) {
                    json.ConsumeSection();
                    return true;
                }

                json.PopSection();
            }
            
            value = default(T);
            return false;
        }

        public override bool ValidateFormat(StringIterator json) {
            if (json.SelectSection(_ => _.ConsumeFloatingNumeric())) {
                json.ConsumeSection();
                return true;
            }

            return false;
        }
    }

    
    public class FloatSerializer : FloatingPointSerializer<float> {
        public FloatSerializer() : base(float.TryParse) { }
    }

    public class DoubleSerializer : FloatingPointSerializer<double> {
        public DoubleSerializer() : base(double.TryParse) { }
    }
}