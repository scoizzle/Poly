using Poly.Serialization;

namespace Poly.Reflection;

public static class Delegate<T>
{
    public delegate bool TryCreateInstance([NotNullWhen(returnValue: true)] out T? instance);
    public delegate bool TryCreateInstance<TArg>(TArg arg, [NotNullWhen(returnValue: true)] out T? instance);
    public delegate bool TrySerialize(IDataWriter writer, T? value);
    public delegate bool TryDeserialize(IDataReader reader, [NotNullWhen(returnValue: true)] out T? value);
}
