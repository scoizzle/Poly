using System;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;

namespace Poly.Data {
    using Collections;

    public partial class TypeInformation {
        public readonly Type Type;

        internal TypeInformation(Type type) {
            Type = type;
            Members = new KeyValueCollection<Member>(Member.GetMembers(type), _ => _.Name);

            Cache.Set(type.FullName, this);
        }

        internal TypeInformation(Type type, Serializer serializer) : this(type) {
            Serializer = serializer;
        }

        public Serializer Serializer { get; internal set; }

        public KeyValueCollection<Member> Members { get; private set; }

        public object CreateInstance(params object[] args) =>
            Activator.CreateInstance(Type, args);

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

        public static IEnumerable<TypeInfo> GetAllDefinedTypes() =>
            Assembly.GetEntryAssembly()
                    .GetReferencedAssemblies()
                    .Select(Assembly.Load)
                    .SelectMany(_ => _.DefinedTypes);

        public static IEnumerable<TypeInfo> GetTypesInheriting<T>() =>
            GetAllDefinedTypes().Where(_ => typeof(T).IsAssignableFrom(_) && _ != typeof(T));

        public static IEnumerable<TypeInfo> GetTypesImplementing<T>() =>
            GetAllDefinedTypes().Where(_ => _.ImplementedInterfaces.Contains(typeof(T)));

        public static IEnumerable<TypeInfo> GetTypesWithAttribute<T>() =>
            GetAllDefinedTypes().Where(_ => _.GetCustomAttributes(typeof(T), true).Length > 0);
    }
}
