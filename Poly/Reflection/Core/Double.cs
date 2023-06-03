using Poly.Serialization;

namespace Poly.Reflection.Core;

internal class Float64 : ISystemTypeInterface<double>
{
    public Float64() {
        Type = typeof(double);
        Serialize = GetSerializationDelegate<IDataWriter>().ToGenericDelegate<IDataWriter, double>();
        Deserialize = GetDeserializationDelegate<IDataReader>().ToGenericDelegate<IDataReader, double>();
        SerializeObject = Serialize.ToObjectDelegate();
        DeserializeObject = Deserialize.ToObjectDelegate();
    }

    public Type Type { get; }

    public SerializeObjectDelegate SerializeObject { get; }

    public DeserializeObjectDelegate DeserializeObject { get; }

    public SerializeDelegate<double> Serialize { get; }

    public DeserializeDelegate<double> Deserialize { get; }

    public static DeserializeDelegate<TReader, double> GetDeserializationDelegate<TReader>() where TReader : class, IDataReader
        => (TReader reader, out double value) => reader.Float64(out value);
        
    public static SerializeDelegate<TWriter, double> GetSerializationDelegate<TWriter>() where TWriter : class, IDataWriter
        => (TWriter writer, double value) => writer.Number(value);
}