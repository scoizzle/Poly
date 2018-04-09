using System;

namespace Poly.Data {
    using Collections;

    public partial class TypeInformation {
        public static MatchingCollection<TypeInformation> Cache;

        static TypeInformation() {
            Cache = new MatchingCollection<TypeInformation>('.');
            Serializer.InitDefaultSerializers();
        }

        public static TypeInformation Get<T>() =>
            Cache.Get(typeof(T).FullName) ?? new TypeInformation(typeof(T));

        public static TypeInformation Get(Type type) =>
            Cache.Get(type.FullName) ?? new TypeInformation(type);

        public static TypeInformation Get(string name) =>
            string.IsNullOrEmpty(name) ? default :
            Cache.Get(name) ?? new TypeInformation(Type.GetType(name));

        internal static bool TryGet(Type type, out TypeInformation info) =>
            (info = Cache.Get(type.FullName)) != null;
    }
}