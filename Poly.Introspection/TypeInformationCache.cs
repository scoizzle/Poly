using System.Collections.Concurrent;

namespace Poly.Introspection;

public sealed class TypeInformationFactoryCache
{
    private readonly ConcurrentDictionary<Type, Lazy<TypeInformation>> _cache = new();

    public Lazy<TypeInformation> GetOrAdd(Type type, Func<Type, Lazy<TypeInformation>> createInfo) => _cache.GetOrAdd(type, createInfo);
}
