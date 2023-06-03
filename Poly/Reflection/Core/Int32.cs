using Poly.Serialization;

namespace Poly.Reflection.Core;

internal class Int32 : ISystemTypeInterface<int>
{
    public Int32() {
        Type = typeof(int);
        Serialize = GetSerializationDelegate<IDataWriter>().ToGenericDelegate<IDataWriter, int>();
        Deserialize = GetDeserializationDelegate<IDataReader>().ToGenericDelegate<IDataReader, int>();
        SerializeObject = Serialize.ToObjectDelegate();
        DeserializeObject = Deserialize.ToObjectDelegate();
    }

    public Type Type { get; }

    public SerializeObjectDelegate SerializeObject { get; }

    public DeserializeObjectDelegate DeserializeObject { get; }

    public SerializeDelegate<int> Serialize { get; }

    public DeserializeDelegate<int> Deserialize { get; }

    public static DeserializeDelegate<TReader, int> GetDeserializationDelegate<TReader>() where TReader : class, IDataReader
        => (TReader reader, out int value) => reader.Int32(out value);
        
    public static SerializeDelegate<TWriter, int> GetSerializationDelegate<TWriter>() where TWriter : class, IDataWriter
        => (TWriter writer, int value) => writer.Number(value);
}