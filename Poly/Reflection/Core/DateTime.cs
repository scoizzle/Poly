using Poly.Serialization;

namespace Poly.Reflection.Core;

internal class DateTime : ISystemTypeInterface<System.DateTime>
{
    public DateTime() {
        Type = typeof(System.DateTime);
        Serialize = GetSerializationDelegate<IDataWriter>().ToGenericDelegate<IDataWriter, System.DateTime>();
        Deserialize = GetDeserializationDelegate<IDataReader>().ToGenericDelegate<IDataReader, System.DateTime>();
        SerializeObject = Serialize.ToObjectDelegate();
        DeserializeObject = Deserialize.ToObjectDelegate();
    }

    public Type Type { get; }

    public SerializeObjectDelegate SerializeObject { get; }

    public DeserializeObjectDelegate DeserializeObject { get; }

    public SerializeDelegate<System.DateTime> Serialize { get; }

    public DeserializeDelegate<System.DateTime> Deserialize { get; }

    public static DeserializeDelegate<TReader, System.DateTime> GetDeserializationDelegate<TReader>() where TReader : class, IDataReader
        => (TReader reader, out System.DateTime value) => reader.DateTime(out value);
        
    public static SerializeDelegate<TWriter, System.DateTime> GetSerializationDelegate<TWriter>() where TWriter : class, IDataWriter
        => (TWriter writer, System.DateTime value) => writer.DateTime(value);
}