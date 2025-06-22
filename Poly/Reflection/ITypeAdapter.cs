using System.Reflection;
using Poly.Serialization;

namespace Poly.Reflection;

public interface ITypeInterface
{
    public string Name { get; }
    public string GloballyUniqueName { get; }
    public IEnumerable<IMethodInterface> Constructors { get; }
    public IEnumerable<IMemberInterface> Members { get; }
    public IEnumerable<IMethodInterface> Methods { get; }
}

public interface IMemberInterface
{
    public string Name { get; }
    public ITypeInterface Type { get; }
    public IMethodInterface? GetMethod { get; }
    public IMethodInterface? SetMethod { get; }
}

public interface IMethodInterface
{
    public string Name { get; }
    public IEnumerable<ITypeInterface> ParameterTypes { get; }
    public ITypeInterface ReturnType { get; }
    public T GetDelegate<T>();
}

public sealed record ClrTypeInterface(Type Type) : ITypeInterface
{
    public string Name => Type.Name;
    public string GloballyUniqueName => Type.FullName ?? Name;

    public IEnumerable<IMemberInterface> Members { get; } = GetMemberInterfaces(Type);
    public IEnumerable<IMethodInterface> Constructors { get; } = GetConstructorInterfaces(Type);
    public IEnumerable<IMethodInterface> Methods => GetMethodInterfaces(Type);

    static IEnumerable<IMemberInterface> GetMemberInterfaces(Type type)
    {
        return type.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
            .Where(m => m is PropertyInfo or FieldInfo)
            .Select(m => new ClrMemberInterface(m));
    }

    static IEnumerable<IMethodInterface> GetConstructorInterfaces(Type type)
    {
        return type.GetConstructors()
            .Select(c => new ClrMethodInterface(c));
    }
    
    static IEnumerable<IMethodInterface> GetMethodInterfaces(Type type)
    {
        return type.GetMethods()
            .Select(m => new ClrMethodInterface(m));
    }
}

public sealed record ClrMemberInterface(MemberInfo Member) : IMemberInterface
{
    public string Name => Member.Name;
    public ITypeInterface Type => new ClrTypeInterface(Member.DeclaringType!);

    public IMethodInterface? GetMethod => Member switch
    {
        PropertyInfo property => property.GetMethod is not null ? new ClrMethodInterface(property.GetMethod) : null,
        // FieldInfo field => new ClrMethodInterface(field.), TODO: FIX!
        _ => null
    };

    public IMethodInterface? SetMethod => Member switch
    {
        PropertyInfo property => property.SetMethod is not null ? new ClrMethodInterface(property.SetMethod) : null,
        _ => null
    };
}

public sealed record ClrMethodInterface(MethodInfo Method) : IMethodInterface
{
    public string Name => Method.Name;
    public IEnumerable<ITypeInterface> ParameterTypes => Method.GetParameters()
        .Select(p => new ClrTypeInterface(p.ParameterType));

    public ITypeInterface ReturnType => new ClrTypeInterface(Method.ReturnType);

    public T GetDelegate<T>()
    {
        return (T)(object)Method.CreateDelegate(typeof(T));
    }
}


public sealed record MemberAdapter(string Name)
{
    public TypeAdapter Type { get; init; } = default!;
    public MethodAdapter GetMethod { get; init; } = default!;
    public MethodAdapter? SetMethod { get; init; } = default!;
}

public abstract record MethodAdapter(string Name, IEnumerable<TypeAdapter> ParameterTypes, TypeAdapter ReturnType)
{
    public abstract T GetDelegate<T>();
}

public sealed record ClrMethodAdapter : MethodAdapter
{
    public ClrMethodAdapter(MethodInfo method)
        : base(method.Name, method.GetParameters().Cast<TypeAdapter>(), new TypeAdapter(method.ReturnType))
    {
        Method = method;
    }

    public MethodInfo Method { get; }

    public override T GetDelegate<T>()
    {
        return (T)(object)Method.CreateDelegate(typeof(T));
    }
}

public class TypeAdapter
{
    public TypeAdapter(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        Name = type.Name;
    }

    public TypeAdapter(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        Name = name;
    }

    public string Name { get; init; } = string.Empty;

    public IEnumerable<MemberAdapter> Members { get; init; } = Enumerable.Empty<MemberAdapter>();
}

public interface IMethodCollection
{
    public bool TryGetMethodAdapter(StringView name, IEnumerable<Type> parameterTypes, [NotNullWhen(true)] out IMethodAdapter? method);
}

class MethodCollection : IMethodCollection
{
    record struct MethodLookupKey(StringView Name, IEnumerable<Type> ParameterTypes);
    private readonly Dictionary<MethodLookupKey, IMethodAdapter> m_Methods;

    public bool TryGetMethodAdapter(StringView name, IEnumerable<Type> parameterTypes, [NotNullWhen(true)] out IMethodAdapter? method)
    {
        MethodLookupKey key = new(name, parameterTypes);
        if (m_Methods.TryGetValue(key, out var adapter))
        {
            method = adapter;
            return true;
        }

        method = default;
        return false;
    }
}

public interface IMemberCollection : IEnumerable<IMemberAdapter>
{
    public bool TryGetMemberAdapter(ReadOnlySpan<char> name, [NotNullWhen(true)] out IMemberAdapter? member);
}

public interface _ITypeAdapter
{
    public string Name { get; }
    public IMemberCollection Members { get; }
    public IMethodCollection Constructors { get; }
    public IMethodCollection Methods { get; }
}

public interface ITypeAdapter
{
    public string Name { get; }
    public string FullName { get; }
    public Delegate<object>.TryCreateInstance TryInstantiateObject { get; }
    public Delegate<object>.TrySerialize TrySerializeObject { get; }
    public Delegate<object>.TryDeserialize TryDeserializeObject { get; }

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