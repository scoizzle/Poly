using System;

namespace Poly.Serialization {
    public delegate bool DeserializeDelegate(ReaderInterface view, out object value);
    public delegate bool DeserializeDelegate<T>(ReaderInterface view, out T value);

    public interface ReaderInterface {
        bool IsDone { get; }
        
        bool Null();

        bool Char(out char value);
        bool String(out string value);
        bool StringView(out StringView value);
        bool Boolean(out bool value);

        bool Int8(out sbyte value);
        bool Int16(out short value);
        bool Int32(out int value);
        bool Int64(out long value);

        bool UInt8(out byte value);
        bool UInt16(out ushort value);
        bool UInt32(out uint value);
        bool UInt64(out ulong value);

        bool Float32(out float value);
        bool Float64(out double value);
        bool Decimal(out decimal value);

        bool DateTime(out DateTime value);
        bool TimeSpan(out TimeSpan value);


        bool BeginMember(out StringView name);
        bool BeginMember<T>(DeserializeDelegate<T> serializer, out T name);
        bool EndValue();

        bool BeginObject();
        bool EndObject();

        bool BeginArray();
        bool EndArray();
    }
}