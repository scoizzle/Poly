using Poly.Serialization;

namespace Poly.Reflection;

public abstract class GenericReferenceTypeAdapterBase<T> : GenericTypeAdapterBase<T> where T : class
{
    public override bool Deserialize(IDataReader reader, [NotNullWhen(true)] out object? value)
    {
        var result = Deserialize(reader, out T? typedValue);
        value = typedValue;
        return result;
    }

    public override bool Serialize(IDataWriter writer, object? value) => value switch
    {
        null => writer.Null(),
        T typedValue => Serialize(writer, typedValue),
        _ => throw new NotSupportedException($"Serializing {value.GetType().Name} as {typeof(T).Name} is not supported.")
    };
}
