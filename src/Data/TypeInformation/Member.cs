using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Poly.Data {
    public class Member {
        public readonly string Name;
        public readonly Type Type;
        public readonly GetDelegate Get;
        public readonly SetDelegate Set;
        public readonly Serializer Serializer;

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

        public Member(string name, Type type, GetDelegate get, SetDelegate set, Serializer serializer) {
            Name = name;
            Type = type;
            Get = get;
            Set = set;
            Serializer = serializer;
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