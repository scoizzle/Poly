namespace Poly.Reflection;

public static class TypeExtensions {
    public static bool ImplementsInterface(
        this Type type,
             Type interfaceType)
    {
        return type
            .GetInterfaces()
            .Contains(interfaceType);
    }

    public static bool ImplementsGenericInterface(
        this Type type, 
             Type interfaceType, 
         out Type[] genericArguments)
    {
        if (interfaceType.IsGenericType)
        {
            foreach (var implType in type.GetInterfaces())
            {
                if (implType.IsGenericType && implType.GetGenericTypeDefinition() == interfaceType)
                {
                    genericArguments = implType.GetGenericArguments();
                    return true;
                }
            }
        }

        genericArguments = default;
        return false;
    }
}