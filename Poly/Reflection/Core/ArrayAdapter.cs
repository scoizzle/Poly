using Poly.Serialization;

namespace Poly.Reflection.Core;

internal sealed class ArrayAdapter<TElement> : GenericTypeAdapterBase<TElement[]>
{
    public ArrayAdapter()
    {
        TryInstantiate = TryCreateInstance;
        TryDeserialize = Deserialize;
        TrySerialize = Serialize;
    }

    private static readonly ISystemTypeAdapter<TElement> s_ElementTypeInterface = TypeAdapterRegistry.Get<TElement>()!;

    public override bool Deserialize(IDataReader reader, [NotNullWhen(true)] out object? value)
    {
        var result = Deserialize(reader, out TElement[]? typedValue);
        value = typedValue;
        return result;
    }

    public override Delegate<TElement[]>.TryCreateInstance TryInstantiate { get; }
    public override Delegate<TElement[]>.TrySerialize TrySerialize { get; }
    public override Delegate<TElement[]>.TryDeserialize TryDeserialize { get; }

    public override bool Deserialize(IDataReader reader, [NotNullWhen(true)] out object? value)
    {
        var result = Deserialize(reader, out TElement[]? typedValue);
        value = typedValue;
        return result;
    }

    public override bool Serialize<TWriter>(TWriter writer, TElement[]? value)
    {
        if (value is null) return writer.Null();

        if (!writer.BeginArray()) return false;

        foreach (var element in value)
        {
            if (!s_ElementTypeInterface.Serialize(writer, element))
                return false;

            if (!writer.EndValue())
                break;
        }

        return writer.EndArray();
    }

    public override bool Serialize(IDataWriter writer, object? value) => value switch
    {
        null => writer.Null(),
        TElement[] typedValue => Serialize(writer, typedValue),
        _ => throw new NotSupportedException($"Serializing {value.GetType().Name} as {typeof(TElement).Name} is not supported.")
    };

    public override bool TryCreateInstance([NotNullWhen(true)] out TElement[]? instance)
    {
        instance = Array.Empty<TElement>();
        return true;
    }

    public override bool Deserialize<TReader>(TReader reader, [NotNullWhen(true)] out TElement[]? value)
    {
        if (reader.BeginArray(out var numberOfMembers))
        {
            if (numberOfMembers.HasValue)
            {
                value = new TElement[numberOfMembers.Value];

                var memberNumber = 0;

                while (!reader.IsDone && memberNumber < numberOfMembers)
                {
                    ref var pos = ref value[memberNumber++];

                    if (!s_ElementTypeInterface.Deserialize(reader, out pos))
                    {
                        value = default;
                        return false;
                    }

                    if (!reader.EndValue())
                        break;
                }

                return true;
            }
            else
            {
                var list = new List<TElement>();

                while (!reader.IsDone)
                {
                    if (!s_ElementTypeInterface.Deserialize(reader, out var element))
                    {
                        value = default;
                        return false;
                    }

                    list.Add(element);

                    if (!reader.EndValue())
                        break;
                }

                if (reader.EndArray())
                {
                    value = list.ToArray();
                    return true;
                }
            }
        }

        value = default;
        return reader.Null();
    }
}