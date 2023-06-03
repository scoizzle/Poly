using Poly.Serialization;

namespace Poly.Reflection.Core;

internal class UInt16 : ISystemTypeInterface<ushort>
{
    public UInt16() {
        Type = typeof(ushort);
        Serialize = GetSerializationDelegate<IDataWriter>().ToGenericDelegate<IDataWriter, ushort>();
        Deserialize = GetDeserializationDelegate<IDataReader>().ToGenericDelegate<IDataReader, ushort>();
        SerializeObject = Serialize.ToObjectDelegate();
        DeserializeObject = Deserialize.ToObjectDelegate();
    }

    public Type Type { get; }

    public SerializeObjectDelegate SerializeObject { get; }

    public DeserializeObjectDelegate DeserializeObject { get; }

    public SerializeDelegate<ushort> Serialize { get; }

    public DeserializeDelegate<ushort> Deserialize { get; }

    public static DeserializeDelegate<TReader, ushort> GetDeserializationDelegate<TReader>() where TReader : class, IDataReader
        => (TReader reader, out ushort value) => reader.UInt16(out value);
        
    public static SerializeDelegate<TWriter, ushort> GetSerializationDelegate<TWriter>() where TWriter : class, IDataWriter
        => (TWriter writer, ushort value) => writer.Number(value);
}