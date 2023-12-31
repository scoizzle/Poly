using Poly.Serialization;

namespace Poly.Reflection;

public interface ISystemTypeInterface : ITypeInterface 
{
    Type Type { get; }
}

public interface ISystemTypeInterface<T> : ISystemTypeInterface
{
    bool Serialize<TWriter>(TWriter writer, T value) where TWriter : class, IDataWriter;
    
    bool Deserialize<TReader>(TReader writer, [NotNullWhen(true)] out T? value) where TReader : class, IDataReader;

    // static abstract SerializeDelegate<TWriter, T> GetSerializationDelegate<TWriter>() where TWriter : class, IDataWriter;

    // static abstract DeserializeDelegate<TReader, T> GetDeserializationDelegate<TReader>() where TReader : class, IDataReader;
}