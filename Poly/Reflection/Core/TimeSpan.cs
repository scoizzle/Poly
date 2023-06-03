using Poly.Serialization;

namespace Poly.Reflection.Core;

internal class TimeSpan : ISystemTypeInterface<System.TimeSpan>
{
    public TimeSpan() {
        Type = typeof(System.TimeSpan);
        Serialize = GetSerializationDelegate<IDataWriter>().ToGenericDelegate<IDataWriter, System.TimeSpan>();
        Deserialize = GetDeserializationDelegate<IDataReader>().ToGenericDelegate<IDataReader, System.TimeSpan>();
        SerializeObject = Serialize.ToObjectDelegate();
        DeserializeObject = Deserialize.ToObjectDelegate();
    }

    public Type Type { get; }

    public SerializeObjectDelegate SerializeObject { get; }

    public DeserializeObjectDelegate DeserializeObject { get; }

    public SerializeDelegate<System.TimeSpan> Serialize { get; }

    public DeserializeDelegate<System.TimeSpan> Deserialize { get; }

    public static DeserializeDelegate<TReader, System.TimeSpan> GetDeserializationDelegate<TReader>() where TReader : class, IDataReader
        => (TReader reader, out System.TimeSpan value) => reader.TimeSpan(out value);
        
    public static SerializeDelegate<TWriter, System.TimeSpan> GetSerializationDelegate<TWriter>() where TWriter : class, IDataWriter
        => (TWriter writer, System.TimeSpan value) => writer.TimeSpan(value);
}