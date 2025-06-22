using System.Collections;
using System.Collections.Concurrent;
using Poly.Introspection.Core;

namespace Poly.Introspection;

public sealed record TypeAdapterRegistry : ITypeAdapterProvider, IEnumerable<ITypeAdapter>
{
    private readonly ClrTypeAdapterProvider clrTypeInfoProvider = new();
    private readonly ConcurrentDictionary<string, ITypeAdapter> customTypeInfoCache = new();

    public ITypeAdapter GetTypeInfo(Type type) => GetTypeInfo(type.FullName);

    public ITypeAdapter GetTypeInfo<T>() => GetTypeInfo(typeof(T).FullName);

    public ITypeAdapter GetTypeInfo(string typeName)
    {
        if (string.IsNullOrEmpty(typeName))
            throw new ArgumentNullException(nameof(typeName));

        if (customTypeInfoCache.TryGetValue(typeName, out var typeInfo))
            return typeInfo;

        return clrTypeInfoProvider.GetTypeInfo(typeName);
    }

    public void Add(ITypeAdapter typeInfo)
    {
        if (typeInfo is null)
            throw new ArgumentNullException(nameof(typeInfo));

        customTypeInfoCache.AddOrUpdate(
            key: typeInfo.GloballyUniqueName,
            addValueFactory: _ => typeInfo,
            updateValueFactory: (_, _) => typeInfo);
    }

    public IEnumerator<ITypeAdapter> GetEnumerator()
    {
        foreach (var info in customTypeInfoCache.Values)
        {
            yield return info;
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}