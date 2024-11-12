namespace Poly.Serialization
{
    public class StringWriter : IDataWriter
    {
        public StringWriter()
        {
            Text = new StringBuilder();
        }

        public StringWriter(int capacity)
        {
            Text = new StringBuilder(capacity);
        }

        public StringWriter(StringBuilder builder)
        {
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

        public bool BeginMember(SerializeFromObjectDelegate serialize, object name)
            => true;

        public bool BeginMember<T>(SerializeDelegate<T> serialize, T name)
            => true;

        public bool BeginMember<TWriter, T>(SerializeDelegate<TWriter, T> serialize, in T name) where TWriter : class, IDataWriter
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

        public bool DateTime(in DateTime value)
        {
            Text.Append(value);
            return true;
        }

        public bool TimeSpan(in TimeSpan value)
        {
            Text.Append(value);
            return true;
        }

        public bool String(in ReadOnlySpan<char> value)
        {
            throw new NotImplementedException();
        }

        public bool String(in ReadOnlySequence<char> value)
        {
            throw new NotImplementedException();
        }

        public bool Number<T>(T value) where T : INumber<T>
        {
            throw new NotImplementedException();
        }

        public bool BeginMember<T>(SerializeDelegate<T> serializer, in T name)
        {
            throw new NotImplementedException();
        }

        public bool BeginValue()
        {
            throw new NotImplementedException();
        }

        public bool Write<T>(T value, ReadOnlySpan<char> format = default, IFormatProvider? formatProvider = default) where T : ISpanFormattable
        {
            throw new NotImplementedException();
        }

        public bool DateTime(DateTime value)
        {
            throw new NotImplementedException();
        }

        public bool TimeSpan(TimeSpan value)
        {
            throw new NotImplementedException();
        }
    }
}
