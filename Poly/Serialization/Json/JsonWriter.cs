using System;
using System.Text;

namespace Poly.Serialization
{
    public class JsonWriter : WriterInterface
    {
        private int _memberCount;
        
        public JsonWriter() : this(capacity: Environment.SystemPageSize) { }

        public JsonWriter(int capacity) : this(new StringBuilder(capacity)) { }

        public JsonWriter(StringBuilder builder) {
            _memberCount = 0;
            Text = builder;
        }

        public StringBuilder Text { get; }
        
        public bool BeginArray()
        {
            Text.Append('[');
            _memberCount = 0;
            return true;
        }

        public bool EndArray()
        {
            if (_memberCount > 0) {
                Text.Length--;
            }

            _memberCount = 1;
            
            Text.Append(']');
            return true;
        }

        public bool BeginMember(string name) {
            Text.AppendStringLiteral(name)
                   .Append(':');

            return true;
        }

        public bool BeginMember(StringView view) {
            Text.AppendStringLiteral(view)
                   .Append(':');

            return true;
        }

        public bool BeginMember<T>(SerializeDelegate<T> serialize, T name)
        {
            if (name is string str)
            {
                Text.AppendStringLiteral(str)
                    .Append(':');

                return true;
            }
            else
            {
                Text.Append('"');

                if (!serialize(this, name))
                    return false;

                Text.Append("\":");
                return true;
            }
        }
        
        public bool EndValue()
        {
            Text.Append(',');
            _memberCount++;
            return true;
        }

        public bool BeginObject()
        {
            Text.Append('{');
            _memberCount = 0;
            return true;
        }

        public bool EndObject()
        {
            if (_memberCount > 0) {
                Text.Length--;
            }

            _memberCount = 1;
        
            Text.Append('}');
            return true;
        }

        public bool Boolean(bool value)
        {
            Text.Append(value ? "true" : "false");
            return true;
        }

        public bool Float32(float value)
        {
            Text.Append(value);
            return true;
        }

        public bool Float64(double value)
        {
            Text.Append(value);
            return true;
        }

        public bool Decimal(decimal value)
        {
            Text.Append(value);
            return true;
        }
        
        public bool Int8(sbyte value)
        {
            Text.Append(value);
            return true;
        }

        public bool Int16(short value)
        {
            Text.Append(value);
            return true;
        }

        public bool Int32(int value)
        {
            Text.Append(value);
            return true;
        }

        public bool Int64(long value)
        {
            Text.Append(value);
            return true;
        }

        public bool UInt8(byte value)
        {
            Text.Append(value);
            return true;
        }

        public bool UInt16(ushort value)
        {
            Text.Append(value);
            return true;
        }

        public bool UInt32(uint value)
        {
            Text.Append(value);
            return true;
        }

        public bool UInt64(ulong value)
        {
            Text.Append(value);
            return true;
        }

        public bool Null()
        {
            Text.Append("null");
            return true;
        }

        public bool Char(char value)
        {
            Text.Append(value);
            return true;
        }

        public bool String(String value)
        {
            Text.AppendStringLiteral(value);
            return true;
        }

        public bool StringView(StringView value)
        {
            Text.AppendStringLiteral(value);
            return true;
        }

        public bool DateTime(DateTime value)
        {
            Text.Append('"').Append(value).Append('"');
            return true;
        }

        public bool TimeSpan(TimeSpan value)
        {
            Text.Append('"').Append(value).Append('"');
            return true;
        }
    }
}
