using System;
using System.Text;

namespace Poly.Data {
    using Collections;

    public partial class Serializer {
        public static Serializer GetCached(string name) =>
            TypeInformation.Get(name)?.Serializer;

        public static Serializer GetCached(Type type) =>
            TypeInformation.Get(type).Serializer ??
            Activator.CreateInstance(typeof(Serializer<>).MakeGenericType(type)) as Serializer;

        public static Serializer<T> GetCached<T>() =>
            TypeInformation.Get<T>().Serializer as Serializer<T> ?? new Serializer<T>();
    }
}