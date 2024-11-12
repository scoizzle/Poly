using System.Reflection;

namespace Poly.Reflection;

public static class AssemblyExtensions
{
    public static string GetAssemblyVersionString(this Assembly assembly)
    {
        var versionAttributeString = assembly
            .GetCustomAttribute<AssemblyVersionAttribute>()?.Version;

        return versionAttributeString ?? string.Empty;
    }

    public static Version? GetAssemblyVersion(this Assembly assembly)
    {
        return Version.TryParse(GetAssemblyVersionString(assembly), out var version)
            ? version
            : default;
    }

    public static IEnumerable<TypeInfo> GetAllDefinedTypes(this Assembly assembly)
        => assembly
            .DefinedTypes
            .Select(t => t.GetTypeInfo());

    public static IEnumerable<TypeInfo> GetTypesInheriting(this Assembly assembly, Type type)
        => GetAllDefinedTypes(assembly)
            .Where(t => type.IsAssignableFrom(t) && t != type);

    public static IEnumerable<TypeInfo> GetTypesImplementing(this Assembly assembly, Type interfaceType)
        => GetAllDefinedTypes(assembly)
            .Where(t => t.ImplementsInterface(interfaceType));

    public static IEnumerable<TypeInfo> GetTypesWithAttribute(this Assembly assembly, Type attributeType)
        => GetAllDefinedTypes(assembly)
            .Where(t => t.GetCustomAttributes(attributeType, true).Length > 0);
}