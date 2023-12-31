namespace Poly.Reflection;

public static class TypeExtensions {
    public static bool ImplementsInterface(
        this Type type,
             Type interfaceType)
    {
        Guard.IsNotNull(type);
        Guard.IsNotNull(interfaceType);
        
        return type
            .GetInterfaces()
            .Contains(interfaceType);
    }

    public static bool ImplementsGenericInterface(
        this Type type, 
        Type interfaceType, 
        [NotNullWhen(true)] out Type[]? genericArguments)
    {
        Guard.IsNotNull(type);
        Guard.IsNotNull(interfaceType);

        if (!interfaceType.IsGenericType)
            goto failure;

        var implType = type
            .GetInterfaces()
            .Where(t => t.IsGenericType)
            .Where(t => t.GetGenericTypeDefinition() == interfaceType)
            .FirstOrDefault();

        if (implType is null)
            goto failure;

        genericArguments = implType.GetGenericArguments();
        return true;

    failure:
        genericArguments = default;
        return false;
    }
}