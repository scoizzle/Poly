using System.Reflection;

namespace Poly.Reflection;

public static class TypeInterfaceRegistry {
    static readonly ConcurrentDictionary<Type, ISystemTypeInterface> _registered = new();

    static TypeInterfaceRegistry() => RegisterTypesFromAssembly(typeof(ISystemTypeInterface).Assembly);

    public static void RegisterTypesFromAssembly(Assembly asm) => asm
        .GetTypesImplementing(typeof(ISystemTypeInterface))
        .Where(t => !t.IsGenericType && t.DeclaredConstructors.Any(c => c.GetParameters().Length == 0))
        .Select(t => Activator.CreateInstance(t) as ISystemTypeInterface)
        .ForEach(Register!);

    public static void Register(ISystemTypeInterface value) {
        if (value is null) throw new ArgumentNullException(nameof(value));
        if (value.Type is null) throw new NullReferenceException("TypeInterface must define a valid Type before registration.");

        _registered[value.Type] = value ?? throw new ArgumentNullException(nameof(value));
    }

    public static ISystemTypeInterface<T>? Get<T>()
        => TryGet<T>(out var value) 
            ? value 
            : default;

    public static ITypeInterface? Get(Type type)
        => TryGet(type, out var value) 
            ? value 
            : default;

    public static bool TryGet(Type type, out ISystemTypeInterface value)
    {
        static ISystemTypeInterface GetInterface(Type t) {
            return TryGetInterface(t, out var v)
                ? v
                : ThrowHelper.ThrowArgumentException<ISystemTypeInterface>();
        };

        value = _registered.GetOrAdd(
            type, 
            GetInterface!
        );

        return value is not null;
    }

    public static bool TryGet<T>(out ISystemTypeInterface<T>? value) {
        var type = typeof(T);

        if (TryGet(type, out var iface) && iface is ISystemTypeInterface<T> typed) {
            value = typed;
            return true;
        }

        value = default;
        return false;
    }

    private static bool TryGetInterface(Type type, [NotNullWhen(true)] out ISystemTypeInterface? value)
        => TryGetArrayInterface(type, out value)
        || TryGetDictionaryInterface(type, out value)
        || TryGetListInterface(type, out value)
        || TryGetGenericObjectInterface(type, out value);

    private static bool TryGetGenericObjectInterface(Type type, out ISystemTypeInterface? value) {
        var generic = typeof(Core.CoreType<>).MakeGenericType(type);
        value = Activator.CreateInstance(generic) as ISystemTypeInterface;
        return true;
    }

    private static bool TryGetArrayInterface(Type type, out ISystemTypeInterface? value) {
        if (!type.IsArray) {
            value = default;
            return false;
        }

        var elementType = type.GetElementType();
        Guard.IsNotNull(elementType);

        var systemTypeInterfaceForElement = typeof(ISystemTypeInterface<>).MakeGenericType(elementType);
        if (!TryGet(elementType, out var elementTypeInterface))
        {
            value = default;
            return false;
        }

        Guard.IsAssignableToType(elementTypeInterface, systemTypeInterfaceForElement);

        var generic = typeof(Core.Array<,>).MakeGenericType(elementType, elementTypeInterface.GetType());
        value = Activator.CreateInstance(generic) as ISystemTypeInterface;
        return true;        
    }
    
    private static bool TryGetDictionaryInterface(Type type, out ISystemTypeInterface? value) {
        if (!type.ImplementsGenericInterface(typeof(IDictionary<,>), out var genericArguments)) {        
            value = default;
            return false;
        }

        Guard.HasSizeEqualTo(genericArguments, 2);

        var keyType = genericArguments[0];
        var valueType = genericArguments[1];

        var systemTypeInterfaceForKey = typeof(ISystemTypeInterface<>).MakeGenericType(keyType);
        if (!TryGet(keyType, out var keyTypeInterface))
        {
            value = default;
            return false;
        }

        var systemTypeInterfaceForValue = typeof(ISystemTypeInterface<>).MakeGenericType(valueType);
        if (!TryGet(valueType, out var valueTypeInterface))
        {
            value = default;
            return false;
        }

        Guard.IsAssignableToType(keyTypeInterface, systemTypeInterfaceForKey);
        Guard.IsAssignableToType(valueTypeInterface, systemTypeInterfaceForValue);

        var generic = typeof(Core.DictionaryTypeInterface<,,,,>).MakeGenericType(type, keyType, systemTypeInterfaceForKey, valueType, systemTypeInterfaceForValue);
        value = Activator.CreateInstance(generic) as ISystemTypeInterface;
        return true;        
    }
    
    private static bool TryGetListInterface(Type type, out ISystemTypeInterface? value) {
        if (!type.ImplementsGenericInterface(typeof(IList<>), out var genericArguments)) {
            value = default;
            return false;
        }

        var elementType = genericArguments[0];

        var systemTypeInterfaceForElement = typeof(ISystemTypeInterface<>).MakeGenericType(elementType);

        if (!TryGet(elementType, out var elementTypeInterface))
        {
            value = default;
            return false;
        }

        Guard.IsAssignableToType(elementTypeInterface, systemTypeInterfaceForElement);
        
        var generic = typeof(Core.ListTypeInterface<,,>).MakeGenericType(type, elementType, elementTypeInterface.GetType());
        value = Activator.CreateInstance(generic) as ISystemTypeInterface;
        return true;
    }
}