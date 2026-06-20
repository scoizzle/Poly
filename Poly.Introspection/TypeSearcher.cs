namespace Poly.Introspection;

public class TypeSearcher {
    public static IEnumerable<Type> GetTypesInheriting<T>() => GetTypesInheriting(typeof(T));
    public static IEnumerable<Type> GetTypesInheriting(Type type) {
        var types = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type.IsAssignableFrom);

        return types;
    }

    public static IEnumerable<Type> GetTypesImplementingInterface<T>() => GetTypesImplementingInterface(typeof(T));
    public static IEnumerable<Type> GetTypesImplementingInterface(Type interfaceType) {
        if (!interfaceType.IsInterface)
            throw new ArgumentException("The provided type is not an interface.", nameof(interfaceType));

        var types = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(assembly => assembly.GetTypes())
            .SelectMany(type => type.GetInterfaces())
            .Where(type => !string.IsNullOrEmpty(type.FullName));

        if (interfaceType.IsGenericType) {
            types = types
                .Where(t => t.IsGenericType)
                .Where(t => t.GetGenericTypeDefinition() == interfaceType);
        }
        else {
            types = types
                .Where(t => t == interfaceType);
        }

        return types;
    }
}
