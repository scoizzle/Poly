using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Poly.Data {
    public partial class Serializer {
        private static readonly Registry Cache = new Registry();

        public static Serializer Get(Type T) =>
            Cache.Get(T);

        public static Serializer<T> Get<T>() =>
            Cache.Get<T>();

        protected class Registry : Dictionary<Type, Serializer> {
            public Registry() {
                Register(typeof(char), new CharSerializer());
                Register(typeof(string), new StringSerializer());
                Register(typeof(bool), new BooleanSerializer());

                Register(typeof(byte), new UInt8Serializer());
                Register(typeof(sbyte), new Int8Serializer());
                Register(typeof(ushort), new UInt16Serializer());
                Register(typeof(short), new Int16Serializer());
                Register(typeof(uint), new UInt32Serializer());
                Register(typeof(int), new Int32Serializer());
                Register(typeof(ulong), new UInt64Serializer());
                Register(typeof(long), new Int64Serializer());

                Register(typeof(float), new FloatSerializer());
                Register(typeof(double), new DoubleSerializer());
            }

            private static T MakeGenericInstance<T>(Type generic, Type type) =>
                (T)(Activator.CreateInstance(generic.MakeGenericType(type)));

            private void Register(Type T, Serializer serializer) =>
                this[T] = serializer;

            public Serializer Get(Type type) {
                if (TryGetValue(type, out Serializer serializer))
                    return serializer;

                var generic = typeof(Serializer<>).MakeGenericType(type);
                var definition = TypeInformation.GetTypesInheriting(type.Assembly, generic).SingleOrDefault();

                if (definition == default) {
                    if (type.IsArray)
                        serializer = MakeGenericInstance<Serializer>(typeof(Array<>), type);
                    else
                        serializer = MakeGenericInstance<Serializer>(typeof(Object<>), type);
                }
                else {
                    serializer = Activator.CreateInstance(definition) as Serializer;
                }

                Register(type, serializer);
                return serializer;
            }

            public Serializer<T> Get<T>() {
                var type = typeof(T);
                if (TryGetValue(type, out Serializer serializer))
                    return serializer as Serializer<T>;

                var generic = typeof(Serializer<T>);
                var definition = TypeInformation.GetTypesInheriting(type.Assembly, generic).SingleOrDefault();

                if (definition == default) {
                    if (type.IsArray)
                        serializer = MakeGenericInstance<Serializer>(typeof(Array<>), type);
                    else
                        serializer = MakeGenericInstance<Serializer>(typeof(Object<>), type);
                }
                else {
                    serializer = Activator.CreateInstance(definition) as Serializer;
                }

                Register(type, serializer);
                return serializer as Serializer<T>;
            }
        }
    }
}