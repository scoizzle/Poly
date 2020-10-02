using System;
using System.Globalization;

namespace Poly.Serialization
{
    public class StringReader : ReaderInterface
    {
        StringView view;

        public StringReader(StringView view)
            => this.view = view;

        bool ReaderInterface.IsDone => view.IsDone;

        bool ReaderInterface.Null() => view.Consume("null");

        bool ReaderInterface.Char(out char value) => view.Consume(out value);
        
        bool ReaderInterface.String(out string value) { value = view.ToString(); return true; }

        bool ReaderInterface.StringView(out StringView value) { value = view; return true; }

        bool ReaderInterface.Boolean(out bool value) => view.Extract(out value);

        bool ReaderInterface.Int8(out sbyte value) => view.Extract(out value);

        bool ReaderInterface.Int16(out short value) => view.Extract(out value); 

        bool ReaderInterface.Int32(out int value) => view.Extract(out value);

        bool ReaderInterface.Int64(out long value) => view.Extract(out value);

        bool ReaderInterface.UInt8(out byte value) => view.Extract(out value);

        bool ReaderInterface.UInt16(out ushort value) => view.Extract(out value);

        bool ReaderInterface.UInt32(out uint value) => view.Extract(out value);

        bool ReaderInterface.UInt64(out ulong value) => view.Extract(out value);

        bool ReaderInterface.Float32(out float value) => view.Extract(out value);

        bool ReaderInterface.Float64(out double value) => view.Extract(out value);

        bool ReaderInterface.Decimal(out decimal value) => view.Extract(out value);

        bool ReaderInterface.BeginMember(out StringView name)
        { name = StringView.Empty; return true; }

        bool ReaderInterface.BeginMember<T>(DeserializeDelegate<T> Deserializer, out T name)
        { name = default; return true; }

        bool ReaderInterface.EndValue() => true;

        bool ReaderInterface.BeginObject() => true;

        bool ReaderInterface.EndObject() => true;

        bool ReaderInterface.BeginArray() => true;

        bool ReaderInterface.EndArray() => true;
        
        bool ReaderInterface.DateTime(out DateTime value)
            => System.DateTime.TryParse(view.AsSpan(), out value);

        bool ReaderInterface.TimeSpan(out TimeSpan value)
            => System.TimeSpan.TryParse(view.AsSpan(), out value);
    }
}