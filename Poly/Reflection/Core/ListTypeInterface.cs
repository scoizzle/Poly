using Poly.Serialization;

namespace Poly.Reflection.Core;

internal sealed class ListTypeInterface<TList, TElement> : GenericReferenceTypeAdapterBase<TList>
    where TList : class, IList<TElement>, new()
{
    private static readonly ISystemTypeAdapter<TElement> s_ElementTypeInterface = TypeAdapterRegistry.Get<TElement>()!;

    public override Delegate<TList>.TryCreateInstance TryInstantiate { get; } =
        static ([NotNullWhen(returnValue: true)] out TList? instance) =>
        {
            instance = new();
            return true;
        };

    public override Delegate<TList>.TryDeserialize TryDeserialize { get; } =
        static (IDataReader reader, [NotNullWhen(returnValue: true)] out TList? value) =>
        {
            ISystemTypeAdapter<TElement> elementTypeAdapter = s_ElementTypeInterface;

            if (!reader.BeginArray(out var _))
            {
                value = default;
                return false;
            }

            value = new();

            while (!reader.IsDone)
            {
                if (!elementTypeAdapter.Deserialize(reader, out var element))
                    return false;

                value.Add(element);

                if (!reader.EndValue())
                    break;
            }

            return reader.EndArray();
        };

    public override Delegate<TList>.TrySerialize TrySerialize { get; } =
        static (IDataWriter writer, TList? value) =>
        {
            if (value is null) return writer.Null();

            if (!writer.BeginArray()) return false;

            ISystemTypeAdapter<TElement> elementTypeAdapter = s_ElementTypeInterface;

            foreach (var element in value)
            {
                if (!elementTypeAdapter.Serialize(writer, element))
                    return false;

                if (!writer.EndValue())
                    break;
            }

            return writer.EndArray();
        };

    public override bool Serialize<TWriter>(TWriter writer, TList? value)
    {
        if (value is null) return writer.Null();

        if (!writer.BeginArray()) return false;

        ISystemTypeAdapter<TElement> elementTypeAdapter = s_ElementTypeInterface;

        foreach (var element in value)
        {
            if (!elementTypeAdapter.Serialize(writer, element))
                return false;

            if (!writer.EndValue())
                break;
        }

        return writer.EndArray();
    }

    public override bool TryCreateInstance([NotNullWhen(true)] out TList? instance)
    {
        instance = new TList();
        return true;
    }

    public override bool Deserialize<TReader>(TReader reader, [NotNullWhen(true)] out TList? list)
    {
        ISystemTypeAdapter<TElement> elementTypeAdapter = s_ElementTypeInterface;

        if (!TryCreateInstance(out list))
            return false;

        if (!reader.BeginArray(out var _))
            return false;

        while (!reader.IsDone)
        {
            if (!elementTypeAdapter.Deserialize(reader, out var value))
                return false;

            list.Add(value);

            if (!reader.EndValue())
                break;
        }

        return reader.EndArray();
    }
}