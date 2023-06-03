namespace Poly.Serialization;

public delegate bool SerializeObjectDelegate(IDataWriter writer, object value);
public delegate bool SerializeDelegate<T>(IDataWriter writer, T value);
public delegate bool SerializeDelegate<TSerializer, TValue>(TSerializer writer, TValue value) where TSerializer : IDataWriter;

public static class SerializeDelegateExtensions
{
    public static SerializeDelegate<TValue> ToGenericDelegate<TWriter, TValue>(this SerializeDelegate<IDataWriter, TValue> @delegate) where TWriter : IDataWriter
        => (IDataWriter writer, TValue value) => @delegate(writer, value);

        
    public static SerializeObjectDelegate ToObjectDelegate<TValue>(this SerializeDelegate<TValue> @delegate)
        => (IDataWriter writer, object obj) =>
        {
            if (writer is null)
                return false;

            if (obj is null)
                return writer.Null();

            if (obj is TValue typed)
                return @delegate(writer, typed);

            return false;
        };    
}

public interface IDataWriter 
{
    bool Null();
    bool Boolean(bool value);
    bool Char(char value);
    bool String(in ReadOnlySpan<char> value);
    bool Number<T>(in T value) where T : INumber<T>;

    bool DateTime(in DateTime value);
    bool TimeSpan(in TimeSpan value);

    bool BeginMember(ReadOnlySpan<char> value);
    bool BeginMember<T>(SerializeDelegate<T> serializer, in T name);
    bool BeginMember<TWriter, T>(SerializeDelegate<TWriter, T> serializer, in T name) where TWriter : class, IDataWriter;

    bool BeginValue();
    bool EndValue();

    bool BeginObject();
    bool EndObject();

    bool BeginArray();
    bool EndArray();
}