using Poly.Serialization;

namespace Poly.Reflection.Core;

internal class CoreType<T> : ISystemTypeInterface<T> where T : class
{
    static readonly IMemberInterface[] _memberInterfaces;
    static readonly Dictionary<StringView, IMemberInterface> _memberDictionary;

    static CoreType()
    {
        _memberInterfaces = CoreTypeMember.GetMemberInterfacesForType(typeof(T)).ToArray();
        _memberDictionary = new(StringViewEqualityComparer.Ordinal);

        foreach (var member in _memberInterfaces)
            _memberDictionary.Add(member.Name, member);
    }

    public CoreType()
    {
        Type = typeof(T);
        Serialize = GetSerializationDelegate<IDataWriter>().ToGenericDelegate<IDataWriter, T>();
        Deserialize = GetDeserializationDelegate<IDataReader>().ToGenericDelegate<IDataReader, T>();
        SerializeObject = Serialize.ToObjectDelegate();
        DeserializeObject = Deserialize.ToObjectDelegate();
    }

    public Type Type { get; }

    public SerializeObjectDelegate SerializeObject { get; }

    public DeserializeObjectDelegate DeserializeObject { get; }

    public SerializeDelegate<T> Serialize { get; }

    public DeserializeDelegate<T> Deserialize { get; }

    public static bool TryGetMemberInterface(
            StringView name, 
        out IMemberInterface? member) 
    {
        return _memberDictionary.TryGetValue(name, out member);
    }

    public static SerializeDelegate<TWriter, T> GetSerializationDelegate<TWriter>() where TWriter : class, IDataWriter
        => (TWriter writer, T obj) =>
        {
            using var _ = Activities.Source.StartActivity();

            if (writer is null) return false;
            if (obj is null) return writer.Null();

            if (!writer.BeginObject()) return false;

            foreach (var member in _memberInterfaces)
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
                    if (!member.Serialize(writer, value))
                        return false;
                }

                if (!writer.EndValue())
                    break;
            }

            return writer.EndObject();
        };

    public static DeserializeDelegate<TReader, T> GetDeserializationDelegate<TReader>() where TReader : class, IDataReader
        => (TReader reader, [NotNullWhen(true)] out T? obj) =>
        {
            using var _ = Activities.Source.StartActivity();

            Guard.IsNotNull(reader);
            
            if (reader is null) { obj = default; return false; }

            if (reader.BeginObject())
            {
                obj = Activator.CreateInstance(typeof(T)) as T;

                Guard.IsNotNull(obj);

                while (!reader.IsDone)
                {
                    if (!reader.BeginMember(out var name))
                        return false;

                    if (!_memberDictionary.TryGetValue(name, out IMemberInterface? member))
                        return false;

                    if (!member.Deserialize(reader, out object? value))
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
        };
}