using Poly.Serialization;

namespace Poly.Reflection;

public interface IMemberAdapter
{
    StringView Name { get; }

    ITypeAdapter TypeInterface { get; }

    bool TryGetValue(object instance, out object? value) { value = default; return false; }

    bool TrySetValue(object instance, object value) { return false; }
}