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

        private static IEnumerable<Member> GetFields(Type type) =>
            from field in type.GetFields()
                where !field.IsStatic && field.IsPublic && !field.IsLiteral
                    select new Member(field);

                
        private static IEnumerable<Member> GetProperties(Type type) =>
            from prop in type.GetProperties()
                where prop.CanRead && prop.CanWrite
                    select new Member(prop);

        private static IEnumerable<Member> GetMembers(Type type) =>
            Enumerable.Concat(GetFields(type), GetProperties(type));

        public static Dictionary<string, Member> GetMemberList(Type type) =>
            GetMembers(type).ToDictionary(member => member.Name);
    }
}