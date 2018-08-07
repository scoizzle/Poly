using System;
using System.Text;

namespace Poly.Data {
    public abstract partial class Serializer {
        public Serializer(TypeInformation info) {
            Type = info.Type;
            TypeInfo = info;
        }

        public Type Type { get; }
        public TypeInformation TypeInfo { get; private set; }

        public abstract bool SerializeObject(StringBuilder json, object obj);

        public abstract bool DeserializeObject(StringIterator json, out object obj);

        public abstract bool ValidateFormat(StringIterator json);
    }
}