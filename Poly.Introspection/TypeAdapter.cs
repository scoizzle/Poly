namespace Poly.Introspection;

public static class TypeAdapter
{
    private static readonly Lazy<TypeAdapterRegistry> registry = new(() => new TypeAdapterRegistry());

    public static IEnumerable<ITypeAdapter> GetTypesInheriting<T>() => GetTypesInheriting(typeof(T));
    public static IEnumerable<ITypeAdapter> GetTypesInheriting(Type type)
    {
        var types = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type.IsAssignableFrom);

        foreach (var t in types)
        {
            yield return registry.Value.GetTypeInfo(t);
        }
    }

    public static IEnumerable<ITypeAdapter> GetTypesImplementingInterface<T>() => GetTypesImplementingInterface(typeof(T));

    public static IEnumerable<ITypeAdapter> GetTypesImplementingInterface(Type interfaceType)
    {
        if (!interfaceType.IsInterface)
            throw new ArgumentException("The provided type is not an interface.", nameof(interfaceType));

        var types = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(assembly => assembly.GetTypes())
            .SelectMany(type => type.GetInterfaces())
            .Where(type => !string.IsNullOrEmpty(type.FullName));

        if (interfaceType.IsGenericType)
        {
            types = types
                .Where(t => t.IsGenericType)
                .Where(t => t.GetGenericTypeDefinition() == interfaceType);
        }
        else
        {
            types = types
                .Where(t => t == interfaceType);
        }

        foreach (var t in types)
        {
            yield return registry.Value.GetTypeInfo(t);
        }
    }
}
