using Poly.Serialization;

namespace Poly.Reflection.Core;

internal class UInt64 : ISystemTypeInterface<ulong>
{
    public UInt64() {
        Type = typeof(ulong);
        Serialize = GetSerializationDelegate<IDataWriter>().ToGenericDelegate<IDataWriter, ulong>();
        Deserialize = GetDeserializationDelegate<IDataReader>().ToGenericDelegate<IDataReader, ulong>();
        SerializeObject = Serialize.ToObjectDelegate();
        DeserializeObject = Deserialize.ToObjectDelegate();
    }

    public Type Type { get; }

    public SerializeObjectDelegate SerializeObject { get; }

    public DeserializeObjectDelegate DeserializeObject { get; }

    public SerializeDelegate<ulong> Serialize { get; }

    public DeserializeDelegate<ulong> Deserialize { get; }

    public static DeserializeDelegate<TReader, ulong> GetDeserializationDelegate<TReader>() where TReader : class, IDataReader
        => (TReader reader, out ulong value) => reader.UInt64(out value);
        
    public static SerializeDelegate<TWriter, ulong> GetSerializationDelegate<TWriter>() where TWriter : class, IDataWriter
        => (TWriter writer, ulong value) => writer.Number(value);
}