using System;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;

namespace Poly.Data {
    using Collections;  

    public partial class TypeInformation {
        private Dictionary<string, Member> member_list;

        public readonly Type Type;

        private TypeInformation(Type type) =>
            Type = type;
        
        public Dictionary<string, Member> Members {
            get => member_list ?? (member_list = Member.GetMembers(Type));
        }

        public object CreateInstance(params object[] args) =>
            Activator.CreateInstance(Type, args);
            
        public T CreateInstance<T>(params object[] args) =>
            typeof(T).IsAssignableFrom(Type) ?
                (T)(Activator.CreateInstance(Type, args)) : default;

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

        public static IEnumerable<TypeInfo> GetAllDefinedTypes(Assembly assembly) =>
            assembly.ExportedTypes.TrySelect(type => type.GetTypeInfo());

        public static IEnumerable<TypeInfo> GetTypesInheriting(Assembly assembly, Type type) =>
            GetAllDefinedTypes(assembly)
                .Where(type_info => type.IsAssignableFrom(type_info) && type_info != type);
            
        public static IEnumerable<TypeInfo> GetTypesImplementing(Assembly assembly, Type type) =>
            GetAllDefinedTypes(assembly)
                .Where(type_info => type_info.ImplementedInterfaces.Contains(type));

        public static IEnumerable<TypeInfo> GetTypesWithAttribute(Assembly assembly, Type type)=>
            GetAllDefinedTypes(assembly)
                .Where(type_info => type_info.GetCustomAttributes(type, true).Length > 0);
    }
}
