using Poly.Serialization;

namespace Poly.Reflection;

public interface ISystemTypeAdapter : ITypeAdapter
{
    public Type Type { get; }
}

public interface ISystemTypeAdapter<T> : ISystemTypeAdapter
{
    public Delegate<T>.TrySerialize TrySerialize { get; }
    public Delegate<T>.TryDeserialize TryDeserialize { get; }

    public bool Serialize<TReader>(TReader writer, T? value)
        where TReader : IDataWriter;

    public bool Deserialize<TReader>(TReader reader, [NotNullWhen(returnValue: true)] out T? value)
        where TReader : IDataReader;

    public bool TryCreateInstance([NotNullWhen(returnValue: true)] out T? value);

    bool ITypeAdapter.TryCreateInstance([NotNullWhen(returnValue: true)] out object? value)
    {
        if (TryCreateInstance(out var typedValue))
        {
            value = typedValue;
            return true;
        }
        value = default;
        return false;
    }
}