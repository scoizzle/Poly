using Poly.Serialization;

namespace Poly.Reflection;

public interface IMemberInterface 
{
    string Name { get; }

    ITypeInterface TypeInterface { get; }

    SerializeObjectDelegate Serialize { get; }

    DeserializeObjectDelegate Deserialize { get; }

    bool TryGetValue(object instance, out object? value) { value = default; return false; }

    bool TrySetValue(object instance, object value) { return false; }
}