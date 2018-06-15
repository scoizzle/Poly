using System;
using System.Collections.Generic;

namespace Poly.Data {

    public partial class TypeInformation {
        public static Dictionary<Type, TypeInformation> Cache;

        static TypeInformation() {
            Cache = new Dictionary<Type, TypeInformation>();
        }

        public static TypeInformation Get(Type type) =>
            type == null ? null :
            Cache.TryGetValue(type, out TypeInformation info) ?
                info : new TypeInformation(type);

        public static TypeInformation Get<T>() =>
            Get(typeof(T));
            
        public static TypeInformation Get(string name) =>
            Get(Type.GetType(name));
    }
}