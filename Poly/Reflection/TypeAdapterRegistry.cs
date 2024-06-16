using System.Reflection;

namespace Poly.Reflection;

public static class TypeAdapterRegistry
{
    static readonly ConcurrentDictionary<Type, ISystemTypeAdapter> s_RegisteredAdapters = new();

    static TypeAdapterRegistry() => AppDomain
        .CurrentDomain
        .GetAssemblies()
        .ForEach(RegisterTypesFromAssembly);

    public static void RegisterTypesFromAssembly(Assembly asm) => asm
        .GetTypesImplementing(typeof(ISystemTypeAdapter))
        .Where(t => !t.IsGenericType && !t.IsAbstract)
        .Where(t => t.DeclaredConstructors.Any(c => c.GetParameters().Length == 0))
        .Select(t => Activator.CreateInstance(t) as ISystemTypeAdapter)
        .ForEach(Register!);

    public static void Register(ISystemTypeAdapter value)
    {
        Guard.IsNotNull(value);
        Guard.IsNotNull(value.Type);

        s_RegisteredAdapters[value.Type] = value ?? throw new ArgumentNullException(nameof(value));
    }

    public static ISystemTypeAdapter<T>? Get<T>() =>
        TryGet<T>(out var value)
            ? value
            : default;

    public static ITypeAdapter? Get(Type type) =>
        TryGet(type, out var value)
            ? value
            : default;

    public static bool TryGet(Type type, out ISystemTypeAdapter value)
    {
        static ISystemTypeAdapter? GetInterface(Type t)
        {
            return TryGetInterface(t, out var v)
                ? v
                : default;
        };

        value = s_RegisteredAdapters.GetOrAdd(
            type,
            GetInterface!
        );

        return value is not null;
    }

    public static bool TryGet<T>(out ISystemTypeAdapter<T>? value)
    {
        var type = typeof(T);

        if (TryGet(type, out var iface) && iface is ISystemTypeAdapter<T> typed)
        {
            value = typed;
            return true;
        }

        value = default;
        return false;
    }

    private static bool TryGetInterface(
        Type type,
        [NotNullWhen(true)] out ISystemTypeAdapter? value)
    {
        return
            TryGetArrayInterface(type, out value) ||
            TryGetDictionaryInterface(type, out value) ||
            TryGetListInterface(type, out value) ||
            TryGetGenericObjectInterface(type, out value);
    }

    private static bool TryGetGenericObjectInterface(Type type, out ISystemTypeAdapter? value)
    {
        var generic = typeof(Core.CoreType<>).MakeGenericType(type);
        value = Activator.CreateInstance(generic) as ISystemTypeAdapter;
        return true;
    }

    private static bool TryGetArrayInterface(Type type, out ISystemTypeAdapter? value)
    {
        if (!type.IsArray)
        {
            value = default;
            return false;
        }

        var elementType = type.GetElementType();
        Guard.IsNotNull(elementType);

        var generic = typeof(Core.Array<>).MakeGenericType(elementType);
        value = Activator.CreateInstance(generic) as ISystemTypeAdapter;
        return true;
    }

    private static bool TryGetDictionaryInterface(Type type, out ISystemTypeAdapter? value)
    {
        if (!type.ImplementsGenericInterface(typeof(IDictionary<,>), out var genericArguments))
        {
            value = default;
            return false;
        }

        Guard.HasSizeEqualTo(genericArguments, 2);

        var keyType = genericArguments[0];
        var valueType = genericArguments[1];

        var generic = typeof(Core.DictionaryTypeInterface<,,>).MakeGenericType(type, keyType, valueType);
        value = Activator.CreateInstance(generic) as ISystemTypeAdapter;
        return true;
    }

    private static bool TryGetListInterface(Type type, out ISystemTypeAdapter? value)
    {
        if (!type.ImplementsGenericInterface(typeof(IList<>), out var genericArguments))
        {
            value = default;
            return false;
        }

        var elementType = genericArguments[0];

        var generic = typeof(Core.ListTypeInterface<,>).MakeGenericType(type, elementType);
        value = Activator.CreateInstance(generic) as ISystemTypeAdapter;
        return true;
    }
}