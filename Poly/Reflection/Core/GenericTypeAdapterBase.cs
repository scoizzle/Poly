using Poly.Serialization;

namespace Poly.Reflection;

public abstract class GenericTypeAdapterBase<T> : ISystemTypeAdapter<T>
{
    public Type Type { get; } = typeof(T);
    public string Name { get; } = typeof(T).Name;
    public string FullName { get; } = typeof(T).FullName ?? typeof(T).Name;

    public abstract bool Deserialize(IDataReader reader, [NotNullWhen(true)] out T? value);
    public abstract bool Deserialize(IDataReader reader, [NotNullWhen(true)] out object? value);
    public abstract bool Serialize(IDataWriter writer, T? value);
    public abstract bool Serialize(IDataWriter writer, object? value);
}
