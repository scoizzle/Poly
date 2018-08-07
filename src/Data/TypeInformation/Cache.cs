using System;
using System.Collections.Generic;

namespace Poly.Data {

    public partial class TypeInformation {
        private static readonly Registry Cache = new Registry();

        public static TypeInformation Get(Type type) =>
            Cache.Get(type);

        public static TypeInformation Get<T>() =>
            Cache.Get(typeof(T));

        private class Registry : Dictionary<Type, TypeInformation> {
            public TypeInformation Get(Type type) {
                if (type == null)
                    return default;

                if (!TryGetValue(type, out TypeInformation info)) {
                    info = new TypeInformation(type);
                    Add(type, info);
                }

                return info;
            }
        }
    }
}