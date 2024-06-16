using Poly.Serialization;

namespace Poly.Reflection;

public interface ISystemTypeAdapter : ITypeAdapter
{
    public Type Type { get; }
}

public interface ISystemTypeAdapter<T> : ISystemTypeAdapter
{
    public bool Serialize(IDataWriter writer, T? value);

    public bool Deserialize(IDataReader reader, [NotNullWhen(returnValue: true)] out T? value);
}