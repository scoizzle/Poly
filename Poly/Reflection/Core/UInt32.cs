using Poly.Serialization;

namespace Poly.Reflection.Core;

internal class UInt32 : ISystemTypeInterface<uint>
{
    public UInt32() {
        Type = typeof(uint);
        Serialize = GetSerializationDelegate<IDataWriter>().ToGenericDelegate<IDataWriter, uint>();
        Deserialize = GetDeserializationDelegate<IDataReader>().ToGenericDelegate<IDataReader, uint>();
        SerializeObject = Serialize.ToObjectDelegate();
        DeserializeObject = Deserialize.ToObjectDelegate();
    }

    public Type Type { get; }

    public SerializeObjectDelegate SerializeObject { get; }

    public DeserializeObjectDelegate DeserializeObject { get; }

    public SerializeDelegate<uint> Serialize { get; }

    public DeserializeDelegate<uint> Deserialize { get; }

    public static DeserializeDelegate<TReader, uint> GetDeserializationDelegate<TReader>() where TReader : class, IDataReader
        => (TReader reader, out uint value) => reader.UInt32(out value);
        
    public static SerializeDelegate<TWriter, uint> GetSerializationDelegate<TWriter>() where TWriter : class, IDataWriter
        => (TWriter writer, uint value) => writer.Number(value);
}