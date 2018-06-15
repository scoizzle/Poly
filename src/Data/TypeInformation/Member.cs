using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Poly.Data {

    public partial class TypeInformation { 
        public class Member {
            public string Name;
            public Type Type;
            public Func<object, object> Get;
            public Action<object, object> Set;
            public Serializer Serializer;

            public Member(FieldInfo info) {
                Name = info.Name;
                Get = info.GetValue;
                Set = info.SetValue;
                Type = info.FieldType;
                Serializer = Serializer.Get(info.FieldType);
            }

            public Member(PropertyInfo info) {
                Name = info.Name;
                Get = info.GetValue;
                Set = info.SetValue;
                Type = info.PropertyType;
                Serializer = Serializer.Get(info.PropertyType);
            }

            public static Dictionary<string, Member> GetMembers(Type type) {
                var members = new Dictionary<string, Member>();

                if (type == null)
                    return members;

                var info = type.GetTypeInfo();
                var fields = info.GetFields();
                var props = info.GetProperties();

                foreach (var item in fields) {
                    if (!item.IsStatic && item.IsPublic && !item.IsLiteral) {
                        members[item.Name] = new Member(item);
                    }
                }

                foreach (var item in props) {
                    if (item.CanRead && item.CanWrite) {
                        members[item.Name] = new Member(item);
                    }
                }
                
                return members;
            }
        }
    }
}