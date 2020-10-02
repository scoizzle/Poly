using System;
using System.Collections.Generic;

using Poly.Serialization;

namespace Poly.Reflection.TypeInterfaces {
    internal class IDictionaryMember<TKey, TValue> : TypeMemberInterface
    {
        public IDictionaryMember(TKey key, TypeInterface<TKey> keyInterface, TypeInterface<TValue> valueInterface) {
            Name = string.Empty;

            TypeInterface = valueInterface;

            Get = GetValueDelegate(key);
            Set = SetValueDelegate(key);
        }

        public string Name { get; }

        public TypeInterface TypeInterface { get; }

        public bool CanRead => true;

        public bool CanWrite => true;

        public Func<object, object> Get { get; }

        public Action<object, object> Set { get; }

        public SerializeDelegate Serialize { get; }

        public DeserializeDelegate Deserialize { get; }

        private static Func<object, object> GetValueDelegate(TKey key) =>
            (obj) => 
                obj is IDictionary<TKey, TValue> dictionary &&
                dictionary.TryGetValue(key, out var value) ? 
                    value : 
                    default;

        private static Action<object, object> SetValueDelegate(TKey key) =>
            (obj, value) => {
                if (obj is IDictionary<TKey, TValue> dictionary && 
                    value is TValue typed) {
                        dictionary[key] = typed;
                    }
            };        
    }
}