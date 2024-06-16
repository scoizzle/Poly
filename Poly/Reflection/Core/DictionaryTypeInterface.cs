using Poly.Serialization;

namespace Poly.Reflection.Core;

internal sealed class DictionaryTypeInterface<TDictionary, TKey, TValue> : GenericReferenceTypeAdapterBase<TDictionary>
    where TDictionary : class, IDictionary<TKey, TValue>
{
    public override bool Deserialize(IDataReader reader, [NotNullWhen(true)] out TDictionary? result)
    {
        if (reader.BeginObject())
        {
            var instance = Activator.CreateInstance(typeof(TDictionary));
            Guard.IsNotNull(instance);

            result = (TDictionary)instance;

            while (!reader.IsDone)
            {
                if (!reader.BeginMember<TKey>(KeyTypeInterface.Deserialize, out var key) || key is null)
                    return false;

                if (!ValueTypeInterface.Deserialize(reader, out var value))
                    return false;

                result.Add(key, value);

                if (!reader.EndValue())
                    break;
            }

            return reader.EndObject();
        }

        result = default;
        return reader.Null();
    }

    public override bool Serialize(IDataWriter writer, TDictionary? value)
    {
        if (value is null) return writer.Null();

        if (!writer.BeginObject()) return false;

        foreach (var pair in value)
        {
            if (!writer.BeginMember(KeyTypeInterface.Serialize, pair.Key))
                return false;

            if (!ValueTypeInterface.Serialize(writer, pair.Value))
                return false;

            if (!writer.EndValue())
                break;
        }

        return writer.EndObject();
    }


    static readonly ISystemTypeAdapter<TKey> KeyTypeInterface = TypeAdapterRegistry.Get<TKey>()!;

    static readonly ISystemTypeAdapter<TValue> ValueTypeInterface = TypeAdapterRegistry.Get<TValue>()!;

    public bool TryGetMemberInterface(StringView name, out IMemberAdapter? member)
    {
        if (KeyTypeInterface.Deserialize(new Serialization.StringReader(name), out var key))
        {
            member = new DictionaryTypeMemberInterface<TKey, TValue>(key, ValueTypeInterface);
            return true;
        }

        member = default;
        return false;
    }
}