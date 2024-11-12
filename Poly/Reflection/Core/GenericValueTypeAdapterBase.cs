using Poly.Serialization;

namespace Poly.Reflection;

public abstract class GenericValueTypeAdapterBase<T> : GenericTypeAdapterBase<T> where T : struct
{
    public override bool TryCreateInstance(out T instance)
    {
        instance = default!;
        return true;
    }

    public override bool Deserialize(IDataReader reader, [NotNullWhen(true)] out object? value)
    {
        var result = Deserialize(reader, out T typedValue);
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