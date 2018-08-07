using System;
using System.Text;
using System.Linq;

namespace Poly.Data {
    public class IntegerSerializer<T> : Serializer<T> {
        public delegate bool TryParseDelegate(string str, int index, int last_index, out T value);

        TryParseDelegate try_parse;

        public IntegerSerializer(TryParseDelegate parser) : base() {
            try_parse = parser;
        }
        
        public override bool Serialize(StringBuilder json, T value) {
            json.Append(value);
            return true;
        }

        public override bool Deserialize(StringIterator json, out T value) {
            if (json.SelectSection(_ => _.ConsumeIntegerNumeric())) {
                if (try_parse(json.String, json.Index, json.LastIndex, out value)) {
                    json.ConsumeSection();
                    return true;
                }

                json.PopSection();
            }
            
            value = default(T);
            return false;
        }

        public override bool ValidateFormat(StringIterator json) {
            if (json.SelectSection(_ => _.ConsumeIntegerNumeric())) {
                json.ConsumeSection();
                return true;
            }

            return false;
        }
    }

    public class Int8Serializer : IntegerSerializer<sbyte> {
        public Int8Serializer() : base(StringInt8Parser.TryParse) { }
    }
    
    public class UInt8Serializer : IntegerSerializer<byte> {
        public UInt8Serializer() : base(StringInt8Parser.TryParse) { }
    }


    public class Int16Serializer : IntegerSerializer<short> {
        public Int16Serializer() : base(StringInt16Parser.TryParse) { }
    }

    public class UInt16Serializer : IntegerSerializer<ushort> {
        public UInt16Serializer() : base(StringInt16Parser.TryParse) { }
    }

    public class Int32Serializer : IntegerSerializer<int> {
        public Int32Serializer() : base(StringInt32Parser.TryParse) { }
    }

    public class UInt32Serializer : IntegerSerializer<uint> {
        public UInt32Serializer() : base(StringInt32Parser.TryParse) { }
    }

    public class Int64Serializer : IntegerSerializer<long> {
        public Int64Serializer() : base(StringInt64Parser.TryParse) { }
    }

    public class UInt64Serializer : IntegerSerializer<ulong> {
        public UInt64Serializer() : base(StringInt64Parser.TryParse) { }
    }
}