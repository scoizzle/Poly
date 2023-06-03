using Poly.Serialization;

namespace Poly.Reflection.Core;

internal class Decimal : ISystemTypeInterface<decimal>
{
    public Decimal() {
        Type = typeof(decimal);
        Serialize = GetSerializationDelegate<IDataWriter>().ToGenericDelegate<IDataWriter, decimal>();
        Deserialize = GetDeserializationDelegate<IDataReader>().ToGenericDelegate<IDataReader, decimal>();
        SerializeObject = Serialize.ToObjectDelegate();
        DeserializeObject = Deserialize.ToObjectDelegate();
    }

    public Type Type { get; }

    public SerializeObjectDelegate SerializeObject { get; }

    public DeserializeObjectDelegate DeserializeObject { get; }

    public SerializeDelegate<decimal> Serialize { get; }

    public DeserializeDelegate<decimal> Deserialize { get; }

    public static DeserializeDelegate<TReader, decimal> GetDeserializationDelegate<TReader>() where TReader : class, IDataReader
        => (TReader reader, out decimal value) => reader.Decimal(out value);
        
    public static SerializeDelegate<TWriter, decimal> GetSerializationDelegate<TWriter>() where TWriter : class, IDataWriter
        => (TWriter writer, decimal value) => writer.Number(value);
}