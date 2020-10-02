using System;
using System.Collections.Generic;

using Poly.Serialization;

namespace Poly.Reflection.TypeInterfaces {
    internal class IDictionary<T, TKey, TValue> : TypeInterface<T> where T : IDictionary<TKey, TValue> {
        TypeInterface<TKey> KeyInterface { get; } = TypeInterface<TKey>.Get();

        TypeInterface<TValue> ValueInterface { get; } = TypeInterface<TValue>.Get();

        public bool TryGetMember(StringView name, out TypeMemberInterface member) {
            if (KeyInterface.Deserialize(new StringReader(name), out var key)) {
                member = new IDictionaryMember<TKey, TValue>(key, KeyInterface, ValueInterface);
                return true;
            }

            member = default;
            return false;
        }

        public SerializeDelegate<T> Serialize
            => (WriterInterface writer, T dictionary) => {
                if (writer is null) return false;
                if (dictionary is null) return writer.Null();
                    
                if (!writer.BeginObject()) return false;

                foreach (var pair in dictionary)
                {
                    if (!writer.BeginMember(KeyInterface.Serialize, pair.Key))
                        return false;

                    if (!ValueInterface.Serialize(writer, pair.Value))
                        return false;

                    if (!writer.EndValue())
                        break;
                }

                return writer.EndObject();
            };

        public DeserializeDelegate<T> Deserialize
            => (ReaderInterface reader, out T dictionary) => {
                if (reader != null && reader.BeginObject())
                {
                    dictionary = (T)Activator.CreateInstance(typeof(T));

                    while (!reader.IsDone)
                    {
                        if (!reader.BeginMember(KeyInterface.Deserialize, out var key))
                            return false;

                        if (!ValueInterface.Deserialize(reader, out var value))
                            return false;

                        dictionary.Add(key, value);

                        if (!reader.EndValue())
                            break;
                    }

                    return reader.EndObject();
                }

                dictionary = default;
                return reader.Null();
            };
    }
}