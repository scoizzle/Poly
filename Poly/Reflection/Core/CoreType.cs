using Poly.Serialization;
namespace Poly.Reflection.Core;

internal sealed class CoreType<T> : GenericReferenceTypeAdapterBase<T> where T : class, new()
{
    static readonly IMemberAdapter[] s_MemberInterfaces;
    static readonly Dictionary<StringView, IMemberAdapter> s_MemberDictionary;

    public override Delegate<T>.TryCreateInstance TryInstantiate { get; }
    public override Delegate<T>.TrySerialize TrySerialize { get; }
    public override Delegate<T>.TryDeserialize TryDeserialize { get; }

    static CoreType()
    {
        s_MemberInterfaces = CoreTypeMember.GetMemberInterfacesForType(typeof(T)).ToArray();
        s_MemberDictionary = new Dictionary<StringView, IMemberAdapter>(StringViewEqualityComparer.OrdinalIgnoreCase);

        foreach (var member in s_MemberInterfaces)
            s_MemberDictionary.Add(member.Name, member);
    }

    public CoreType()
    {
        TryInstantiate = TryCreateInstance;
        TryDeserialize = Deserialize;
        TrySerialize = Serialize;
    }

    public override bool TryCreateInstance([NotNullWhen(true)] out T? instance)
    {
        instance = new();
        return true;
    }

    public override bool Serialize<TWriter>(TWriter writer, T? value)
    {
        Guard.IsNotNull(writer);

        if (value is null) return writer.Null();

        if (!writer.BeginObject()) return false;

        foreach (var member in s_MemberInterfaces)
        {

            if (!member.TryGetValue(value, out var memberValue))
                return false;

            if (!writer.BeginMember(member.Name))
                return false;

            if (memberValue is null)
                return writer.Null();

            if (!member.TypeInterface.Serialize(writer, memberValue))
                return false;

            if (!writer.EndValue())
                break;
        }

        return writer.EndObject();
    }

    public override bool Deserialize<TReader>(TReader reader, [NotNullWhen(true)] out T? obj)
    {
        if (!reader.BeginObject())
            goto failure;

        if (!TryCreateInstance(out obj))
            return false;

        while (!reader.IsDone)
        {
            if (!reader.BeginMember(out var name))
                break;

            if (!s_MemberDictionary.TryGetValue(name, out IMemberAdapter? member))
                goto failure;

            if (member.TypeInterface.Deserialize(reader, out object? value))
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
    }
}