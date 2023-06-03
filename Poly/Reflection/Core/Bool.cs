using Poly.Serialization;

namespace Poly.Reflection.Core;    

internal class Boolean : ISystemTypeInterface<bool>
{
    public Boolean() {
        Type = typeof(bool);
        Serialize = GetSerializationDelegate<IDataWriter>().ToGenericDelegate<IDataWriter, bool>();
        Deserialize = GetDeserializationDelegate<IDataReader>().ToGenericDelegate<IDataReader, bool>();
        SerializeObject = Serialize.ToObjectDelegate();
        DeserializeObject = Deserialize.ToObjectDelegate();
    }

    public Type Type { get; }

    public SerializeObjectDelegate SerializeObject { get; }

    public DeserializeObjectDelegate DeserializeObject { get; }

    public SerializeDelegate<bool> Serialize { get; }

    public DeserializeDelegate<bool> Deserialize { get; }

    public static DeserializeDelegate<TReader, bool> GetDeserializationDelegate<TReader>() where TReader : class, IDataReader
        => (TReader reader, out bool value) => reader.Boolean(out value);
        
    public static SerializeDelegate<TWriter, bool> GetSerializationDelegate<TWriter>() where TWriter : class, IDataWriter
        => (TWriter writer, bool value) => writer.Boolean(value);
}