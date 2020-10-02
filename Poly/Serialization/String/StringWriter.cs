using System;
using System.Text;

namespace Poly.Serialization {
    public class StringWriter : WriterInterface
    {
        public StringWriter() {
            Text = new StringBuilder();
        }

        public StringWriter(int capacity) {
            Text = new StringBuilder(capacity);
        }

        public StringWriter(StringBuilder builder) {
            Text = builder;
        }

        public StringBuilder Text { get; }
        
        public bool BeginArray()
            => true;

        public bool EndArray()
            => true;

        public bool BeginMember(string name)
            => true;

        public bool BeginMember(StringView name)
            => true;

        public bool BeginMember(SerializeDelegate serialize, object name)
            => true;

        public bool BeginMember<T>(SerializeDelegate<T> serialize, T name)
            => true;
        
        public bool EndValue()
            => true;

        public bool BeginObject()
            => true;

        public bool EndObject()
            => true;

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

        public bool String(string value)
        {
            Text.Append(value);
            return true;
        }

        public bool StringView(StringView value)
        {
            Text.Append(value.String, value.Index, value.Length);
            return true;
        }

        public bool DateTime(DateTime value)
        {
            Text.Append(value);
            return true;
        }

        public bool TimeSpan(TimeSpan value)
        {
            Text.Append(value);
            return true;
        }
    }
}
