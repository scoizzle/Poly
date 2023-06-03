using System;

namespace Poly.Serialization
{
    public class StringReader : IDataReader
    {
        StringView view;

        public StringReader(StringView view)
            => this.view = view;

        bool IDataReader.IsDone => view.IsDone;

        bool IDataReader.Null() => view.Consume("null");

        bool IDataReader.Char(out char value) => view.Consume(out value);
        
        bool IDataReader.String(out string value) { value = view.ToString(); return true; }

        bool IDataReader.StringView(out StringView value) { value = view; return true; }

        bool IDataReader.Boolean(out bool value) => view.Extract(out value);

        bool IDataReader.Int8(out sbyte value) => view.Extract(out value);

        bool IDataReader.Int16(out short value) => view.Extract(out value); 

        bool IDataReader.Int32(out int value) => view.Extract(out value);

        bool IDataReader.Int64(out long value) => view.Extract(out value);

        bool IDataReader.UInt8(out byte value) => view.Extract(out value);

        bool IDataReader.UInt16(out ushort value) => view.Extract(out value);

        bool IDataReader.UInt32(out uint value) => view.Extract(out value);

        bool IDataReader.UInt64(out ulong value) => view.Extract(out value);

        bool IDataReader.Float32(out float value) => view.Extract(out value);

        bool IDataReader.Float64(out double value) => view.Extract(out value);

        bool IDataReader.Decimal(out decimal value) => view.Extract(out value);

        bool IDataReader.BeginMember(out StringView name)
        { name = StringView.Empty; return true; }

        bool IDataReader.BeginMember<T>(DeserializeDelegate<T> Deserializer, out T name)
        { name = default; return true; }

        bool IDataReader.BeginMember<TReader, T>(DeserializeDelegate<TReader, T> Deserializer, out T name)
        { name = default; return true; }

        bool IDataReader.EndValue() => true;

        bool IDataReader.BeginObject() => true;

        bool IDataReader.EndObject() => true;

        bool IDataReader.BeginArray(out int? numberOfMembers) {
            numberOfMembers = default;
            return true;
        }

        bool IDataReader.EndArray() => true;
        
        bool IDataReader.DateTime(out DateTime value)
            => System.DateTime.TryParse(view.AsSpan(), out value);

        bool IDataReader.TimeSpan(out TimeSpan value)
            => System.TimeSpan.TryParse(view.AsSpan(), out value);
    }
}