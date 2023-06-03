using Poly.Serialization;

namespace Poly.Reflection.Core;

internal class Int16 : ISystemTypeInterface<short>
{
    public Int16() {
        Type = typeof(short);
        Serialize = GetSerializationDelegate<IDataWriter>().ToGenericDelegate<IDataWriter, short>();
        Deserialize = GetDeserializationDelegate<IDataReader>().ToGenericDelegate<IDataReader, short>();
        SerializeObject = Serialize.ToObjectDelegate();
        DeserializeObject = Deserialize.ToObjectDelegate();
    }

    public Type Type { get; }

    public SerializeObjectDelegate SerializeObject { get; }

    public DeserializeObjectDelegate DeserializeObject { get; }

    public SerializeDelegate<short> Serialize { get; }

    public DeserializeDelegate<short> Deserialize { get; }

    public static DeserializeDelegate<TReader, short> GetDeserializationDelegate<TReader>() where TReader : class, IDataReader
        => (TReader reader, out short value) => reader.Int16(out value);
        
    public static SerializeDelegate<TWriter, short> GetSerializationDelegate<TWriter>() where TWriter : class, IDataWriter
        => (TWriter writer, short value) => writer.Number(value);
}