using Poly.Serialization;

namespace Poly.Reflection.Core;

internal sealed class Array<TElement> : GenericReferenceTypeAdapterBase<TElement[]>
{
    private static readonly ISystemTypeAdapter<TElement> s_ElementTypeInterface = TypeAdapterRegistry.Get<TElement>()!;

    public override bool Deserialize(IDataReader reader, [NotNullWhen(true)] out TElement[]? value)
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
                    value = new TElement[list.Count];
                    list.CopyTo(0, value, 0, list.Count);
                    return true;
                }
            }
        }

        value = default;
        return reader.Null();
    }

    public override bool Serialize(IDataWriter writer, [NotNullWhen(returnValue: true)] TElement[]? value)
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
}