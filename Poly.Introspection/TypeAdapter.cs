namespace Poly.Introspection;

public static class TypeAdapter
{
    private static Lazy<TypeAdapterRegistry> registry = new(() => new TypeAdapterRegistry());

    public static IEnumerable<ITypeAdapter> GetTypesBasedOn<T>()
    {
        var type = typeof(T);
        var types = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type.IsAssignableFrom);

        foreach (var t in types)
        {
            yield return registry.Value.GetTypeInfo(t);
        }
    }

    public static IEnumerable<ITypeAdapter> GetTypesImplementingInterface<T>()
    {
        var interfaceType = typeof(T);
        var types = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(assembly => assembly.GetTypes())
            .SelectMany(type => type.GetInterfaces());

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
