using Poly.Serialization;

namespace Poly.Reflection.Core;

internal class Float32 : ISystemTypeInterface<float>
{
    public Float32() {
        Type = typeof(float);
        Serialize = GetSerializationDelegate<IDataWriter>().ToGenericDelegate<IDataWriter, float>();
        Deserialize = GetDeserializationDelegate<IDataReader>().ToGenericDelegate<IDataReader, float>();
        SerializeObject = Serialize.ToObjectDelegate();
        DeserializeObject = Deserialize.ToObjectDelegate();
    }

    public Type Type { get; }

    public SerializeObjectDelegate SerializeObject { get; }

    public DeserializeObjectDelegate DeserializeObject { get; }

    public SerializeDelegate<float> Serialize { get; }

    public DeserializeDelegate<float> Deserialize { get; }

    public static DeserializeDelegate<TReader, float> GetDeserializationDelegate<TReader>() where TReader : class, IDataReader
        => (TReader reader, out float value) => reader.Float32(out value);
        
    public static SerializeDelegate<TWriter, float> GetSerializationDelegate<TWriter>() where TWriter : class, IDataWriter
        => (TWriter writer, float value) => writer.Number(value);
}