using Poly.Serialization;
namespace Poly.Reflection.Core;

internal sealed class CoreType<T> : GenericReferenceTypeAdapterBase<T> where T : class
{
    static readonly IMemberAdapter[] s_MemberInterfaces;
    static readonly Dictionary<StringView, IMemberAdapter> s_MemberDictionary;

    static CoreType()
    {
        s_MemberInterfaces = CoreTypeMember.GetMemberInterfacesForType(typeof(T)).ToArray();
        s_MemberDictionary = new Dictionary<StringView, IMemberAdapter>(StringViewEqualityComparer.Ordinal);

        foreach (var member in s_MemberInterfaces)
            s_MemberDictionary.Add(member.Name, member);
    }

    public override bool Serialize(IDataWriter writer, T? obj)
    {
        Guard.IsNotNull(writer);

        if (obj is null) return writer.Null();

        if (!writer.BeginObject()) return false;

        foreach (var member in s_MemberInterfaces)
        {
            if (!writer.BeginMember(member.Name))
                return false;

            if (!member.TryGetValue(obj, out var value) || value is null)
            {
                if (!writer.Null())
                    return false;
            }
            else
            {
                if (!member.TypeInterface.Serialize(writer, value))
                    return false;
            }

            if (!writer.EndValue())
                break;
        }

        return writer.EndObject();
    }

    public override bool Deserialize(IDataReader reader, [NotNullWhen(true)] out T? obj)
    {
        Guard.IsNotNull(reader);

        if (reader.BeginObject())
        {
            obj = Activator.CreateInstance(typeof(T)) as T;

            Guard.IsNotNull(obj);

            while (!reader.IsDone)
            {
                if (!reader.BeginMember(out var name))
                    return false;

                if (!s_MemberDictionary.TryGetValue(name, out IMemberAdapter? member))
                    return false;

                if (!member.TypeInterface.Deserialize(reader, out object? value))
                    return false;

                if (!member.TrySetValue(obj, value))
                    return false;

                if (!reader.EndValue())
                    break;
            }

            return reader.EndObject();
        }

        obj = default;
        return reader.Null();
    }
}