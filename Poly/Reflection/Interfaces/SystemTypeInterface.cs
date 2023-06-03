using Poly.Serialization;

#pragma warning disable IDE1006 // Naming rule violation: Missing prefix: 'I'

namespace Poly.Reflection;

public interface ISystemTypeInterface : ITypeInterface 
{
    Type Type { get; }
}

public interface ISystemTypeInterface<T> : ISystemTypeInterface
{
    SerializeDelegate<T> Serialize { get; }

    DeserializeDelegate<T> Deserialize { get; }

    // static abstract bool TrySerialize_<TWriter>(TWriter writer, T value) where TWriter : IDataWriter;
    
    // static abstract bool TryDeserialize_<TReader>(TReader writer, [NotNullWhen(true)] out T? value) where TReader : IDataReader;

    static abstract SerializeDelegate<TWriter, T> GetSerializationDelegate<TWriter>() where TWriter : class, IDataWriter;

    static abstract DeserializeDelegate<TReader, T> GetDeserializationDelegate<TReader>() where TReader : class, IDataReader;
}