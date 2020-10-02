using System;

namespace Poly.Serialization {

    public delegate bool SerializeDelegate(WriterInterface view, object value);
    public delegate bool SerializeDelegate<T>(WriterInterface view, T value);

    public interface WriterInterface {
        bool Null();

        bool Char(char value);
        bool String(string value);
        bool StringView(StringView value);

        bool Boolean(bool value);

        bool Int8(sbyte value);
        bool Int16(short value);
        bool Int32(int value);
        bool Int64(long value);

        bool UInt8(byte value);
        bool UInt16(ushort value);
        bool UInt32(uint value);
        bool UInt64(ulong value);

        bool Float32(float value);
        bool Float64(double value);
        bool Decimal(decimal value);

        bool DateTime(DateTime value);
        bool TimeSpan(TimeSpan value);

        bool BeginMember(string name);
        bool BeginMember(StringView name);
        bool BeginMember<T>(SerializeDelegate<T> serializer, T name);
        bool EndValue();

        bool BeginObject();
        bool EndObject();

        bool BeginArray();
        bool EndArray();
    }
}