namespace Poly.Reflection;

public static class TypeExtensions
{
    public static bool HasDefaultConstructor(
        this Type type)
    {
        return type.GetConstructor(Type.EmptyTypes) != null;
    }
    
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
        [NotNullWhen(returnValue: true)] out Type[]? genericArguments)
    {
        Guard.IsNotNull(type);
        Guard.IsNotNull(interfaceType);

        if (!interfaceType.IsGenericType)
            goto failure;

        var implType = type
                      .GetInterfaces()
                      .Where(t => t.IsGenericType)
                      .FirstOrDefault(t => t.GetGenericTypeDefinition() == interfaceType);

        if (implType is null)
            goto failure;

        genericArguments = implType.GetGenericArguments();
        return true;

    failure:
        genericArguments = default;
        return false;
    }
}