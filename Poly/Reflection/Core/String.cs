using Poly.Serialization;

namespace Poly.Reflection.Core;

internal class String : ISystemTypeInterface<string>
{
    public String() {
        Type = typeof(string);
        Serialize = GetSerializationDelegate<IDataWriter>().ToGenericDelegate<IDataWriter, string>();
        Deserialize = GetDeserializationDelegate<IDataReader>().ToGenericDelegate<IDataReader, string>();
        SerializeObject = Serialize.ToObjectDelegate();
        DeserializeObject = Deserialize.ToObjectDelegate();
    }

    public Type Type { get; }

    public SerializeObjectDelegate SerializeObject { get; }

    public DeserializeObjectDelegate DeserializeObject { get; }

    public SerializeDelegate<string> Serialize { get; }

    public DeserializeDelegate<string> Deserialize { get; }

    public static DeserializeDelegate<TReader, string> GetDeserializationDelegate<TReader>() where TReader : class, IDataReader
        => (TReader reader, [NotNullWhen(true)] out string? value) => reader.String(out value);
        
    public static SerializeDelegate<TWriter, string> GetSerializationDelegate<TWriter>() where TWriter : class, IDataWriter
        => (TWriter writer, string value) => writer.String(value);
}