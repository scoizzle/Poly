using System;
using System.Collections.Generic;
using System.Text;

namespace Poly.Data {
    public abstract partial class Serializer {
        public Serializer(Type type) {
            Type = type;

            if (TypeInformation.TryGet(type, out TypeInformation info)) {
                TypeInfo = info;
                TypeInfo.Serializer = this;
            }
            else {
                TypeInfo = new TypeInformation(type, this);
            }
        }

        public Type Type { get; }
        public TypeInformation TypeInfo { get; private set; }

        public object CreateInstance(params object[] args) =>
            TypeInfo.CreateInstance(args);

        public virtual bool SerializeObject(StringBuilder json, object obj) {
            return false;
        }

        public virtual bool DeserializeObject(StringIterator json, out object obj) {
            obj = null;
            return false;
        }
    }
}