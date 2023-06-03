using Poly.Serialization;

namespace Poly.Reflection.Core;

internal class Char : ISystemTypeInterface<char>
{
    public Char() {
        Type = typeof(char);
        Serialize = GetSerializationDelegate<IDataWriter>().ToGenericDelegate<IDataWriter, char>();
        Deserialize = GetDeserializationDelegate<IDataReader>().ToGenericDelegate<IDataReader, char>();
        SerializeObject = Serialize.ToObjectDelegate();
        DeserializeObject = Deserialize.ToObjectDelegate();
    }

    public Type Type { get; }

    public SerializeObjectDelegate SerializeObject { get; }

    public DeserializeObjectDelegate DeserializeObject { get; }

    public SerializeDelegate<char> Serialize { get; }

    public DeserializeDelegate<char> Deserialize { get; }

    public static DeserializeDelegate<TReader, char> GetDeserializationDelegate<TReader>() where TReader : class, IDataReader
        => (TReader reader, out char value) => reader.Char(out value);
        
    public static SerializeDelegate<TWriter, char> GetSerializationDelegate<TWriter>() where TWriter : class, IDataWriter
        => (TWriter writer, char value) => writer.Char(value);
}