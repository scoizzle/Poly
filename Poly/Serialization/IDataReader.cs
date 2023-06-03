namespace Poly.Serialization;

public delegate bool DeserializeObjectDelegate(IDataReader view, [NotNullWhen(true)] out object? value);
public delegate bool DeserializeDelegate<T>(IDataReader view, [NotNullWhen(true)] out T? value);
public delegate bool DeserializeDelegate<TDeserializer, TValue>(TDeserializer view, [NotNullWhen(true)] out TValue? value) where TDeserializer : IDataReader;

public static class DeserializeDelegateExtensions
{
    public static DeserializeDelegate<TValue> ToGenericDelegate<TReader, TValue>(this DeserializeDelegate<IDataReader, TValue> @delegate) where TReader : IDataReader
        => (IDataReader reader, [NotNullWhen(true)] out TValue? value) => @delegate(reader, out value);

    public static DeserializeObjectDelegate ToObjectDelegate<TValue>(this DeserializeDelegate<TValue> @delegate)
        => (IDataReader reader, [NotNullWhen(true)] out object? obj) => 
        {
            if (reader is null) { obj = default; return false; }

            if (@delegate(reader, out var value))
            {
                obj = value;
                return true;
            }

            obj = default;
            reader.Null();

            return false;
        };
}

public interface IDataReader {
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
    bool BeginMember<TReader, T>(DeserializeDelegate<TReader, T> serializer, out T? name) where TReader : class, IDataReader;
    bool EndValue();

    bool BeginObject();
    bool EndObject();

    bool BeginArray(out int? numberOfMembers);
    bool EndArray();
}