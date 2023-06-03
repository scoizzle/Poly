using Poly.Serialization;

namespace Poly.Reflection;    

public interface ITypeInterface 
{
    SerializeObjectDelegate SerializeObject { get; }

    DeserializeObjectDelegate DeserializeObject { get; }

    bool TryGetMemberInterface(
        ReadOnlySpan<char> name, 
        [NotNullWhen(true)] out IMemberInterface? member) 
    {
        member = default; 
        return false; 
    }

    bool TryGetMethodInterface(
        ReadOnlySpan<char> name, 
        Type[] parameterTypes, 
        [NotNullWhen(true)] out IMethodInterface? method) 
    { 
        method = default; 
        return false; 
    }

    bool TryGetMemberValue(
        ReadOnlySpan<char> name, 
        object instance, 
        out object? value) 
    {
        if (!TryGetMemberInterface(name, out var member)) {
            value = default; 
            return false; 
        }

        return member.TryGetValue(instance, out value);
    }

    bool TrySetMemberValue(
        string name, 
        object instance, 
        object value) 
    { 
        if (!TryGetMemberInterface(name, out var member))
            return false; 

        return member.TrySetValue(instance, value);
    }
}