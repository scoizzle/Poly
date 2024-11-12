using System.Reflection;
using Poly.Serialization;

namespace Poly.Reflection;

public abstract class GenericTypeAdapterBase<T> : ISystemTypeAdapter<T>
{
    public Type Type { get; } = typeof(T);
    public string Name { get; } = typeof(T).Name;
    public string FullName { get; } = typeof(T).FullName ?? typeof(T).Name;
    public Delegate<object>.TryCreateInstance TryInstantiateObject => TryInstantiate.Objectify();
    public Delegate<object>.TrySerialize TrySerializeObject => TrySerialize.Objectify();
    public Delegate<object>.TryDeserialize TryDeserializeObject => TryDeserialize.Objectify();
    public abstract Delegate<T>.TryCreateInstance TryInstantiate { get; }
    public abstract Delegate<T>.TrySerialize TrySerialize { get; }
    public abstract Delegate<T>.TryDeserialize TryDeserialize { get; }
    public abstract bool TryCreateInstance([NotNullWhen(returnValue: true)] out T? instance);

    public abstract bool Deserialize<TReader>(TReader reader, [NotNullWhen(true)] out T? value)
        where TReader : IDataReader;

    public abstract bool Deserialize(IDataReader reader, [NotNullWhen(true)] out object? value);
    public abstract bool Serialize<TWriter>(TWriter writer, T? value) where TWriter : IDataWriter;
    public abstract bool Serialize(IDataWriter writer, object? value);
}