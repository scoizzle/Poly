using Poly.Serialization;

namespace Poly.Reflection.Core;

internal class Int64 : ISystemTypeInterface<long>
{
    public Int64() {
        Type = typeof(long);
        Serialize = GetSerializationDelegate<IDataWriter>().ToGenericDelegate<IDataWriter, long>();
        Deserialize = GetDeserializationDelegate<IDataReader>().ToGenericDelegate<IDataReader, long>();
        SerializeObject = Serialize.ToObjectDelegate();
        DeserializeObject = Deserialize.ToObjectDelegate();
    }

    public Type Type { get; }

    public SerializeObjectDelegate SerializeObject { get; }

    public DeserializeObjectDelegate DeserializeObject { get; }

    public SerializeDelegate<long> Serialize { get; }

    public DeserializeDelegate<long> Deserialize { get; }

    public static DeserializeDelegate<TReader, long> GetDeserializationDelegate<TReader>() where TReader : class, IDataReader
        => (TReader reader, out long value) => reader.Int64(out value);
        
    public static SerializeDelegate<TWriter, long> GetSerializationDelegate<TWriter>() where TWriter : class, IDataWriter
        => (TWriter writer, long value) => writer.Number(value);
}