using System;
using System.Text;

namespace Poly.Data {
    public abstract partial class Serializer {
        public Serializer(TypeInformation info, bool register = true) {
            Type = info.Type;
            TypeInfo = info;

            if (register)
                Cache.Register(info.Type, this);
        }

        public Type Type { get; }
        public TypeInformation TypeInfo { get; private set; }

        public abstract bool SerializeObject(StringBuilder json, object obj);

        public abstract bool DeserializeObject(StringIterator json, out object obj);

        public abstract bool ValidateFormat(StringIterator json);
    }
}