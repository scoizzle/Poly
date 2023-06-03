using Poly.Serialization;

namespace Poly.Reflection.Core;

internal class Int8 : ISystemTypeInterface<sbyte>
{
    public Int8() {
        Type = typeof(sbyte);
        Serialize = GetSerializationDelegate<IDataWriter>().ToGenericDelegate<IDataWriter, sbyte>();
        Deserialize = GetDeserializationDelegate<IDataReader>().ToGenericDelegate<IDataReader, sbyte>();
        SerializeObject = Serialize.ToObjectDelegate();
        DeserializeObject = Deserialize.ToObjectDelegate();
    }

    public Type Type { get; }

    public SerializeObjectDelegate SerializeObject { get; }

    public DeserializeObjectDelegate DeserializeObject { get; }

    public SerializeDelegate<sbyte> Serialize { get; }

    public DeserializeDelegate<sbyte> Deserialize { get; }

    public static DeserializeDelegate<TReader, sbyte> GetDeserializationDelegate<TReader>() where TReader : class, IDataReader
        => (TReader reader, out sbyte value) => reader.Int8(out value);
        
    public static SerializeDelegate<TWriter, sbyte> GetSerializationDelegate<TWriter>() where TWriter : class, IDataWriter
        => (TWriter writer, sbyte value) => writer.Number(value);
}