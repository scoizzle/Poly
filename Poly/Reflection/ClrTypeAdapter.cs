using System.Collections.Frozen;
using System.Reflection;
using Poly.Serialization;

namespace Poly.Reflection;

public sealed class ClrTypeMemberAdapter<TDeclaringType, TMember> : IMemberAdapter
{
    public required StringView Name { get; init; }
    public required ITypeAdapter TypeInterface { get; init; }
    public required Delegate<TDeclaringType>.TryGetValue<TMember> TryGetValue { get; init; }
    public required Delegate<TDeclaringType>.TrySetValue<TMember> TrySetValue { get; init; }
}

public sealed class ClrTypeAdapter<T> : ITypeAdapter
{
    private Lazy<List<IMemberAdapter>> m_Members;
    private Lazy<Delegate<T>.TryCreateInstance> m_TryInstantiate;
    private Lazy<Delegate<T>.TrySerialize> m_Serializer;
    private Lazy<Delegate<T>.TryDeserialize> m_Deserializer;

    public readonly Type ClrType = typeof(T);

    public required string Name { get; init; }
    public required string FullName { get; init; }
    public Delegate<T>.TryCreateInstance TryInstantiate => m_TryInstantiate.Value;
    public Delegate<T>.TrySerialize TrySerialize => m_Serializer.Value;
    public Delegate<T>.TryDeserialize TryDeserialize => m_Deserializer.Value;

    public required Delegate<object>.TryCreateInstance TryInstantiateObject { get; init; }
    public required Delegate<object>.TrySerialize TrySerializeObject { get; init; }
    public required Delegate<object>.TryDeserialize TryDeserializeObject { get; init; }

    public ClrTypeAdapter()
    {
        Name = ClrType.Name;
        FullName = ClrType.FullName ?? Name;
        m_Members = new(GetMembers);
        m_TryInstantiate = new(GetInstantiator);
        m_Serializer = new(GetSerializer);
        m_Deserializer = new(GetDeserializer);
    }

    private List<IMemberAdapter> GetMembers()
    {
        return CoreTypeMember
            .GetMemberInterfacesForType(typeof(T))
            .ToList();
    }

    private Delegate<T>.TryCreateInstance GetInstantiator()
    {
        return ([NotNullWhen(returnValue: true)] out T? instance) =>
        {
            instance = Activator.CreateInstance<T>();
            return instance is not null;
        };
    }

    private Delegate<T>.TrySerialize GetSerializer() => GetSerializer(m_Members.Value);
    private Delegate<T>.TryDeserialize GetDeserializer() => GetDeserializer(m_TryInstantiate.Value, m_Members.Value);

    private record struct MemberObjectSerializerMap(
        StringView Name,
        Delegate<object>.TryGetValue<object?> TryGetValue,
        Delegate<object>.TrySerialize TrySerialize);

    private static Delegate<T>.TrySerialize GetSerializer(IEnumerable<IMemberAdapter> members)
    {
        MemberObjectSerializerMap[] memberSerializers = members
            .Select(e => new MemberObjectSerializerMap(
                e.Name,
                e.TryGetValue!,
                e.TypeInterface.TrySerializeObject
            ))
            .ToArray();

        return (IDataWriter writer, T? value) =>
        {
            ArgumentNullException.ThrowIfNull(writer);

            if (value is null) return writer.Null();

            if (!writer.BeginObject()) return false;

            foreach (var member in memberSerializers)
            {
                var (memberName, getMemberValue, serializeValue) = member;

                if (!getMemberValue(value, out object? memberValue))
                    return false;

                if (!writer.BeginMember(memberName))
                    return false;

                if (!serializeValue(writer, memberValue))
                    return false;

                if (!writer.EndValue())
                    break;
            }

            return writer.EndObject();
        };
    }

    private record struct MemberObjectDeserializerMap(
        StringView Name,
        Delegate<object>.TrySetValue<object?> TrySetValue,
        Delegate<object>.TryDeserialize TryDeserialize);

    private static Delegate<T>.TryDeserialize GetDeserializer(Delegate<T>.TryCreateInstance tryCreateInstance, IEnumerable<IMemberAdapter> members)
    {
        FrozenDictionary<StringView, MemberObjectDeserializerMap> memberDictionary = members
            .Select(e => new MemberObjectDeserializerMap(
                e.Name,
                e.TrySetValue!,
                e.TypeInterface.TryDeserializeObject
            ))
            .ToFrozenDictionary(e => e.Name, StringViewEqualityComparer.OrdinalIgnoreCase);

        return (IDataReader reader, [NotNullWhen(returnValue: true)] out T? obj) =>
        {
            ArgumentNullException.ThrowIfNull(reader);

            if (!reader.BeginObject())
                goto failure;

            if (!tryCreateInstance(out obj))
                return false;

            while (!reader.IsDone)
            {
                if (!reader.BeginMember(out var name))
                    break;

                if (!memberDictionary.TryGetValue(name, out var member))
                    goto failure;

                if (member.TryDeserialize(reader, out object? value))
                {
                    if (!member.TrySetValue(obj, value))
                        goto failure;
                }
                else
                if (!reader.Null())
                {
                    goto failure;
                }

                if (!reader.EndValue())
                    break;
            }

            if (!reader.EndObject())
                goto failure;

            return true;

        failure:
            obj = default;
            return false;
        };
    }

    public bool Serialize(IDataWriter writer, object? value)
    {
        throw new NotImplementedException();
    }

    public bool Deserialize(IDataReader reader, [NotNullWhen(true)] out object? value)
    {
        throw new NotImplementedException();
    }
}
