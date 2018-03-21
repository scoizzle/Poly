using System;
using System.Reflection;

namespace Poly.Data {
    using Collections;

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
                Serializer = Serializer.GetCached(info.FieldType);
            }

            public Member(PropertyInfo info) {
                Name = info.Name;
                Get = info.GetValue;
                Set = info.SetValue;
                Type = info.PropertyType;
                Serializer = Serializer.GetCached(info.PropertyType);
            }

            public static Member[] GetMembers(Type type) {
                return GetMemberList(type).ToArray();
            }

            private static ManagedArray<Member> GetMemberList(Type type) {
                var info = type.GetTypeInfo();
                var fields = info.GetFields();
                var props = info.GetProperties();
                var members = new ManagedArray<Member>(fields.Length + props.Length);

                foreach (var item in fields) {
                    if (!item.IsStatic && item.IsPublic && !item.IsLiteral) {
                        members.Add(new Member(item));
                    }
                }

                foreach (var item in props) {
                    if (item.CanRead && item.CanWrite) {
                        members.Add(new Member(item));
                    }
                }

                var parent = info.BaseType;

                if (parent != null) {
                    members.Add(GetMemberList(parent));
                }

                return members;
            }
        }
    }
}