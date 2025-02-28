using System.Collections;
using System.Collections.Concurrent;
using Poly.Introspection.Core;

namespace Poly.Introspection;

public sealed record TypeInfoRegistry : ITypeInfoProvider, IEnumerable<ITypeInfo>
{
    private readonly ClrTypeInfoProvider clrTypeInfoProvider = new();
    private readonly ConcurrentDictionary<string, ITypeInfo> customTypeInfoCache = new();

    public ITypeInfo GetTypeInfo(Type type) => GetTypeInfo(type.FullName);

    public ITypeInfo GetTypeInfo<T>() => GetTypeInfo(typeof(T).FullName);

    public ITypeInfo GetTypeInfo(string typeName)
    {
        if (string.IsNullOrEmpty(typeName))
            throw new ArgumentNullException(nameof(typeName));

        if (customTypeInfoCache.TryGetValue(typeName, out var typeInfo))
            return typeInfo;

        return clrTypeInfoProvider.GetTypeInfo(typeName);
    }

    public void Add(ITypeInfo typeInfo)
    {
        if (typeInfo is null)
            throw new ArgumentNullException(nameof(typeInfo));

        customTypeInfoCache.AddOrUpdate(
            key: typeInfo.FullName,
            addValueFactory: _ => typeInfo,
            updateValueFactory: (_, _) => typeInfo);
    }

    public IEnumerator<ITypeInfo> GetEnumerator()
    {
        foreach (var info in customTypeInfoCache.Values)
        {
            yield return info;
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}