using Poly.Serialization;

namespace Poly.Reflection;

public static class DelegateExtensions
{

    public static Delegate<object>.TryCreateInstance Objectify<T>(this Delegate<T>.TryCreateInstance createInstance)
    {
        return CreateInstance;

        bool CreateInstance([NotNullWhen(returnValue: true)] out object? value)
        {
            if (!createInstance(out var instance))
            {
                value = default;
                return false;
            }

            value = instance;
            return true;
        }
    }

    public static Delegate<object>.TrySerialize Objectify<T>(this Delegate<T>.TrySerialize serialize)
    {
        return SerializeObject;

        bool SerializeObject(IDataWriter writer, object? value) => value switch
        {
            null => serialize(writer, default),
            T t => serialize(writer, t),
            _ => throw new NotSupportedException($"Serializing an object of type {value.GetType().Name} with typed serializer for type {typeof(T).Name} is not supported.")
        };
    }


    public static Delegate<object>.TryDeserialize Objectify<T>(this Delegate<T>.TryDeserialize deserialize)
    {
        return DeserializeObject;

        bool DeserializeObject(IDataReader reader, [NotNullWhen(returnValue: true)] out object? value)
        {
            if (!deserialize(reader, out var typedValue))
            {
                value = default;
                return false;
            }

            value = typedValue;
            return true;
        }
    }
}