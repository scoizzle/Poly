using Poly.Serialization;

namespace Poly.Reflection.Core;

internal class DictionaryTypeInterface<TDictionary, TKey, TValue> : ISystemTypeInterface<TDictionary>
    where TDictionary : IDictionary<TKey, TValue>
{
    public DictionaryTypeInterface() {
        Type = typeof(TDictionary);
        SerializeObject = new SerializeDelegate<TDictionary>(Serialize).ToObjectDelegate();
        DeserializeObject = new DeserializeDelegate<TDictionary>(Deserialize).ToObjectDelegate();
    }

    public Type Type { get; }

    public SerializeObjectDelegate SerializeObject { get; }

    public DeserializeObjectDelegate DeserializeObject { get; }

    public bool Deserialize<TReader>(TReader reader, [NotNullWhen(true)] out TDictionary? result) where TReader : class, IDataReader
    {
		using var _ = Instrumentation.AddEvent();

        if (reader is null) {
            result = default;
            return false;
        }

        if (reader.BeginObject())
        {
            var instance = Activator.CreateInstance(typeof(TDictionary));
            Guard.IsNotNull(instance);

            result = (TDictionary)instance;

            while (!reader.IsDone)
            {
                if (!reader.BeginMember<TReader, TKey>(KeyTypeInterface.Deserialize, out var key) || key is null)
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
        
    public bool Serialize<TWriter>(TWriter writer, TDictionary value) where TWriter : class, IDataWriter
    {
		using var _ = Instrumentation.AddEvent();
        Guard.IsNotNull(writer);

        if (value is null) return writer.Null();
            
        if (!writer.BeginObject()) return false;

        foreach (var pair in value)
        {
            if (!writer.BeginMember<TWriter, TKey>(KeyTypeInterface.Serialize, pair.Key))
                return false;

            if (!ValueTypeInterface.Serialize(writer, pair.Value))
                return false;

            if (!writer.EndValue())
                break;
        }

        return writer.EndObject();
    }


    static readonly ISystemTypeInterface<TKey> KeyTypeInterface = TypeInterfaceRegistry.Get<TKey>()!;

    static readonly ISystemTypeInterface<TValue> ValueTypeInterface = TypeInterfaceRegistry.Get<TValue>()!;

    public bool TryGetMemberInterface(StringView name, out IMemberInterface? member) {
        if (KeyTypeInterface.Deserialize(new Serialization.StringReader(name), out var key)) {
            member = new DictionaryTypeMemberInterface<TKey, TValue>(key, ValueTypeInterface);
            return true;
        }

        member = default;
        return false;
    }
}