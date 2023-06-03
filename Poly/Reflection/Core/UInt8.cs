using Poly.Serialization;

namespace Poly.Reflection.Core;

internal class UInt8 : ISystemTypeInterface<byte>
{
    public UInt8() {
        Type = typeof(byte);
        Serialize = GetSerializationDelegate<IDataWriter>().ToGenericDelegate<IDataWriter, byte>();
        Deserialize = GetDeserializationDelegate<IDataReader>().ToGenericDelegate<IDataReader, byte>();
        SerializeObject = Serialize.ToObjectDelegate();
        DeserializeObject = Deserialize.ToObjectDelegate();
    }

    public Type Type { get; }

    public SerializeObjectDelegate SerializeObject { get; }

    public DeserializeObjectDelegate DeserializeObject { get; }

    public SerializeDelegate<byte> Serialize { get; }

    public DeserializeDelegate<byte> Deserialize { get; }

    public static DeserializeDelegate<TReader, byte> GetDeserializationDelegate<TReader>() where TReader : class, IDataReader
        => (TReader reader, out byte value) => reader.UInt8(out value);
        
    public static SerializeDelegate<TWriter, byte> GetSerializationDelegate<TWriter>() where TWriter : class, IDataWriter
        => (TWriter writer, byte value) => writer.Number(value);
}