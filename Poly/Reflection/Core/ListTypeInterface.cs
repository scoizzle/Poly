using Poly.Serialization;

namespace Poly.Reflection.Core;

internal sealed class ListTypeInterface<TList, TElement> : GenericReferenceTypeAdapterBase<TList>
    where TList : class, IList<TElement>
{
    private static readonly ISystemTypeAdapter<TElement> s_ElementTypeInterface = TypeAdapterRegistry.Get<TElement>()!;

    public override bool Deserialize(IDataReader reader, [NotNullWhen(true)] out TList? list)
    {
        if (reader.BeginArray(out var __))
        {
            var instance = Activator.CreateInstance(typeof(TList));
            Guard.IsNotNull(instance);
            list = (TList)instance;

            while (!reader.IsDone)
            {
                if (!s_ElementTypeInterface.Deserialize(reader, out var value))
                    return false;

                list.Add(value);

                if (!reader.EndValue())
                    break;
            }

            return reader.EndArray();
        }

        list = default;
        return reader.Null();
    }

    public override bool Serialize(IDataWriter writer, TList? list)
    {
        if (list is null) return writer.Null();

        if (!writer.BeginArray()) return false;

        foreach (var element in list)
        {
            if (!s_ElementTypeInterface.Serialize(writer, element))
                return false;

            if (!writer.EndValue())
                break;
        }

        return writer.EndArray();
    }
}