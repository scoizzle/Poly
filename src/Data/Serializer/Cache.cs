using System;
using System.Collections.Generic;

namespace Poly.Data {
    public partial class Serializer {
        public static class Cache {
            static readonly Dictionary<Type, Serializer> dictionary;

            static Cache() {
                dictionary = new Dictionary<Type, Serializer>();

                foreach (var type in TypeInformation.GetTypesInheriting<Serializer>()) {
                    if (type.IsAbstract || type.IsGenericType)
                        continue;
                    
                    Activator.CreateInstance(type);
                }
            }

            internal static void Register(Type type, Serializer serializer) {
                dictionary[type] = serializer;
            }
            
            static T MakeGenericInstance<T>(Type generic, Type type) =>
                (T)(Activator.CreateInstance(generic.MakeGenericType(type)));

            public static Serializer Get(Type type) {
                if (dictionary.TryGetValue(type, out Serializer serializer))
                    return serializer;
                    
                if (type.IsArray)
                    serializer = MakeGenericInstance<Serializer>(typeof(Array<>), type);
                else
                    serializer = MakeGenericInstance<Serializer>(typeof(Object<>), type);

                Register(type, serializer);
                return serializer;
            }

            public static Serializer<T> Get<T>() {
                var type = typeof(T);
                if (dictionary.TryGetValue(type, out Serializer serializer))
                    return serializer as Serializer<T>;
                    
                if (type.IsArray)
                    serializer = MakeGenericInstance<Serializer>(typeof(Array<>), type);
                else
                    serializer = MakeGenericInstance<Serializer>(typeof(Object<>), type);

                Register(type, serializer);
                return serializer as Serializer<T>;
            }
        }

        public static Serializer Get(Type T) =>
            Cache.Get(T);

        public static Serializer<T> Get<T>() =>
            Cache.Get<T>();
    }
}