namespace Poly.Serialization
{
    public class StringReader : IDataReader
    {
        StringView view;

        public StringReader(StringView view)
            => this.view = view;

        bool IDataReader.IsDone => view.IsEmpty;

        bool IDataReader.Null() => view.Consume("null");

        bool IDataReader.Char(out char value) => view.Consume(out value);

        bool IDataReader.String(out string value) { value = view.ToString(); return true; }

        bool IDataReader.StringView(out StringView value) { value = view; return true; }

        bool IDataReader.Boolean(out bool value) => view.TryParse(out value);

        bool IDataReader.Int8(out sbyte value) => view.TryParse(out value);

        bool IDataReader.Int16(out short value) => view.TryParse(out value);

        bool IDataReader.Int32(out int value) => view.TryParse(out value);

        bool IDataReader.Int64(out long value) => view.TryParse(out value);

        bool IDataReader.UInt8(out byte value) => view.TryParse(out value);

        bool IDataReader.UInt16(out ushort value) => view.TryParse(out value);

        bool IDataReader.UInt32(out uint value) => view.TryParse(out value);

        bool IDataReader.UInt64(out ulong value) => view.TryParse(out value);

        bool IDataReader.Float32(out float value) => view.TryParse(out value);

        bool IDataReader.Float64(out double value) => view.TryParse(out value);

        bool IDataReader.Decimal(out decimal value) => view.TryParse(out value);

        bool IDataReader.BeginMember(out StringView name)
        { name = StringView.Empty; return true; }

        bool IDataReader.BeginMember<T>(DeserializeDelegate<T> Deserializer, out T name)
        { name = default; return true; }

        bool IDataReader.EndValue() => true;

        bool IDataReader.BeginObject() => true;

        bool IDataReader.EndObject() => true;

        bool IDataReader.BeginArray(out int? numberOfMembers)
        {
            numberOfMembers = default;
            return true;
        }

        bool IDataReader.EndArray() => true;

        bool IDataReader.DateTime(out DateTime value)
            => DateTime.TryParse(view.AsSpan(), out value);

        bool IDataReader.TimeSpan(out TimeSpan value)
            => TimeSpan.TryParse(view.AsSpan(), out value);

        public bool Read<T>([NotNullWhen(returnValue: true)] out T? value)
            where T : ISpanParsable<T>
        {
            return view.TryParse(out value);
        }

        public bool ReadString<T>(out T? value) where T : ISpanParsable<T>
            => view.TryParse(out value);
    }
}