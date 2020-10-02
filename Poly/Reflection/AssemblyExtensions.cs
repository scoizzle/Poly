using System;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;

namespace Poly.Reflection {
    public static class AssemblyExtensions {
        public static IEnumerable<TypeInfo> GetAllDefinedTypes(this Assembly assembly) =>
            Enumerable.Select(assembly.ExportedTypes, type => type.GetTypeInfo());

        public static IEnumerable<TypeInfo> GetTypesInheriting(this Assembly assembly, Type type) =>
            GetAllDefinedTypes(assembly)
                .Where(type_info => type.IsAssignableFrom(type_info) && type_info != type);

        public static IEnumerable<TypeInfo> GetTypesImplementing(this Assembly assembly, Type interfaceType) =>
            GetAllDefinedTypes(assembly)
                .Where(type_info => type_info.ImplementedInterfaces.Contains(interfaceType));

        public static IEnumerable<TypeInfo> GetTypesWithAttribute(this Assembly assembly, Type attributeType) =>
            GetAllDefinedTypes(assembly)
                .Where(type_info => type_info.GetCustomAttributes(attributeType, true).Length > 0);
    }
}