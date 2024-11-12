using Poly.Serialization;

namespace Poly.Reflection;

public interface ITypeAdapter
{
    public string                             Name                 { get; }
    public string                             FullName             { get; }
    public Delegate<object>.TryCreateInstance TryInstantiateObject { get; }
    public Delegate<object>.TrySerialize      TrySerializeObject   { get; }
    public Delegate<object>.TryDeserialize    TryDeserializeObject { get; }


    public bool Serialize(IDataWriter writer, object? value);
    public bool Deserialize(IDataReader reader, [NotNullWhen(returnValue: true)] out object? value);

    public bool TryCreateInstance([NotNullWhen(returnValue: true)] out object? instance)
    {
        instance = null;
        return false;
    }
    
    public bool TryGetMemberAdapter(ReadOnlySpan<char> name, [NotNullWhen(true)] out IMemberAdapter? member)
    {
        member = default;
        return false;
    }

    public bool TryGetMethodAdapter(ReadOnlySpan<char> name, Type[] parameterTypes, [NotNullWhen(true)] out IMethodAdapter? method)
    {
        method = default;
        return false;
    }

    public bool TryGetMemberValue(ReadOnlySpan<char> name, object instance, out object? value)
    {
        if (!TryGetMemberAdapter(name, out var member))
        {
            value = default;
            return false;
        }

        return member.TryGetValue(instance, out value);
    }

    public bool TrySetMemberValue(ReadOnlySpan<char> name, object instance, object value)
    {
        if (!TryGetMemberAdapter(name, out var member))
            return false;

        return member.TrySetValue(instance, value);
    }
}