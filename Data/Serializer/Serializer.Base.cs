using System;
using System.Linq;
using System.Reflection;
using System.Text;

namespace Poly.Data {
    public partial class Serializer {
        public delegate object SerializeObjectDelegate(object obj);
        public delegate void DeserializeObjectDelegate(object obj, object val);

        public Type                         Type;
        public KeyValueCollection<Member>   Members;

        public Serializer(Type type) {
            Type = type;
            Members = GetMemberList(type);

            Cache[type.Name] = this;
        }

        public object CreateInstance() {
            return Activator.CreateInstance(Type);
        }

        public bool GetMemberValue(object This, string name, out object value) {
            if (!Members.TryGetValue(name, out Member member)) {
                value = null;
                return false;
            }

            value = member.Get(This);
            return true;
        }

        public bool SetMemberValue(object This, string name, object value) {
            if (!Members.TryGetValue(name, out Member member))
                return false;

            member.Set(This, value);
            return true;
        }

        public virtual bool SerializeObject(StringBuilder json, object obj) {
            if (obj == null)
                return false;

            json.Append(obj);
            return true;
        }

        public virtual bool DeserializeObject(StringIterator json, out object obj) {
            obj = null;
            return false;
        }
    }
}