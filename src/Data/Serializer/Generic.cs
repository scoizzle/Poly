using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Poly.Data {
    using Collections;
    
    public abstract class Serializer<T> : Serializer {
        protected Serializer() : base(TypeInformation.Get<T>()) { }

        protected Serializer(bool register) : base(TypeInformation.Get<T>(), register) { }

        public string Serialize(T obj) {
            var output = new StringBuilder();

            if (Serialize(output, obj))
                return output.ToString();

            return null;
        }

        public T Deserialize(string str) {
            if (Deserialize(str, out T result))
                return result;

            return default;
        }

        public override bool SerializeObject(StringBuilder json, object obj) {
            if (obj is T typed)
                return Serialize(json, typed);

            return false;
        }

        public override bool DeserializeObject(StringIterator json, out object obj) {
            if (Deserialize(json, out T result)) {
                obj = result;
                return true;
            }

            obj = null;
            return false;
        }

        public abstract bool Serialize(StringBuilder json, T obj);

        public abstract bool Deserialize(StringIterator json, out T obj);
    }
}