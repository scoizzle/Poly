using Poly.Serialization;

namespace Poly.Reflection.Core;

internal class DictionaryTypeInterface<TDictionary, TKey, TKeyInterface, TValue, TValueInterface> : ISystemTypeInterface<TDictionary> 
    where TDictionary : IDictionary<TKey, TValue> 
    where TKeyInterface : ISystemTypeInterface<TKey>
    where TValueInterface : ISystemTypeInterface<TValue>
{
    public DictionaryTypeInterface() {
        Type = typeof(TDictionary);
        Serialize = GetSerializationDelegate<IDataWriter>().ToGenericDelegate<IDataWriter, TDictionary>();
        Deserialize = GetDeserializationDelegate<IDataReader>().ToGenericDelegate<IDataReader, TDictionary>();
        SerializeObject = Serialize.ToObjectDelegate();
        DeserializeObject = Deserialize.ToObjectDelegate();
    }

    public Type Type { get; }

    public SerializeObjectDelegate SerializeObject { get; }

    public DeserializeObjectDelegate DeserializeObject { get; }

    public SerializeDelegate<TDictionary> Serialize { get; }

    public DeserializeDelegate<TDictionary> Deserialize { get; }

    public static DeserializeDelegate<TReader, TDictionary> GetDeserializationDelegate<TReader>() where TReader : class, IDataReader
    {
        var deserializeKey = TKeyInterface.GetDeserializationDelegate<TReader>();
        var deserializeValue = TValueInterface.GetDeserializationDelegate<TReader>();

        return (TReader reader, [NotNullWhen(true)] out TDictionary? result) => 
        {
            Guard.IsNotNull(reader);

            if (reader.BeginObject())
            {
                var instance = Activator.CreateInstance(typeof(TDictionary));
                Guard.IsNotNull(instance);

                result = (TDictionary)instance;

                while (!reader.IsDone)
                {
                    if (!reader.BeginMember(deserializeKey, out var key))
                        return false;

                    if (!deserializeValue(reader, out var value))
                        return false;

                    result.Add(key, value);

                    if (!reader.EndValue())
                        break;
                }

                return reader.EndObject();
            }

            result = default;
            return reader.Null();
        };
    }
        
    public static SerializeDelegate<TWriter, TDictionary> GetSerializationDelegate<TWriter>() where TWriter : class, IDataWriter
    {
        var serializeKey = TKeyInterface.GetSerializationDelegate<TWriter>();
        var serializeValue = TValueInterface.GetSerializationDelegate<TWriter>();

        return (TWriter writer, TDictionary value) => {
            if (writer is null) return false;
            if (value is null) return writer.Null();
                
            if (!writer.BeginObject()) return false;

            foreach (var pair in value)
            {
                if (!writer.BeginMember(serializeKey, pair.Key))
                    return false;

                if (!serializeValue(writer, pair.Value))
                    return false;

                if (!writer.EndValue())
                    break;
            }

            return writer.EndObject();
        };
    }


    static readonly ISystemTypeInterface<TKey> KeyInterface = TypeInterfaceRegistry.Get<TKey>()!;

    static readonly ISystemTypeInterface<TValue> ValueInterface = TypeInterfaceRegistry.Get<TValue>()!;

    public bool TryGetMemberInterface(StringView name, out IMemberInterface? member) {
        if (KeyInterface.Deserialize(new Serialization.StringReader(name), out var key)) {
            member = new DictionaryTypeMemberInterface<TKey, TValue>(key, ValueInterface);
            return true;
        }

        member = default;
        return false;
    }
}