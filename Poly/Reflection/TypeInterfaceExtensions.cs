using System;
using System.Collections.Generic;
using System.Reflection;

namespace Poly.Reflection {
    public static class TypeInterfaceExtensions {
        public static IEnumerable<TypeMemberInterface> GetTypeMembers(this TypeInterface declaringType, Type type) {
            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
                if (field.IsPublic)
                    yield return new TypeMember(declaringType, field);

            foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                if (prop.CanRead)
                    yield return new TypeMember(declaringType, prop);
        }
    }
}