using System;
using System.Collections.Generic;
using System.Linq;

using Poly.Serialization;

namespace Poly.Reflection.TypeInterfaces
{
    internal class UserDefinedType<T> : TypeInterface<T>
    {
        readonly TypeMemberInterface[] _memberInterfaces;
        readonly Dictionary<StringView, TypeMemberInterface> _memberDictionary;

        public UserDefinedType()
        {
            _memberInterfaces = TypeInterfaceExtensions.GetTypeMembers(this, typeof(T)).ToArray();
            _memberDictionary = new Dictionary<StringView, TypeMemberInterface>(StringViewEqualityComparer.Ordinal);

            foreach (var member in _memberInterfaces)
            {
                _memberDictionary.Add(member.Name, member);
            }

            Serialize = GetSerializeDelegate(_memberInterfaces);
            Deserialize = GetDeserializeDelegate(_memberDictionary);
        }

        public SerializeDelegate<T> Serialize { get; }

        public DeserializeDelegate<T> Deserialize { get; }

        public bool TryGetMember(string name, out TypeMemberInterface member)
            => _memberDictionary.TryGetValue(name, out member);

        static SerializeDelegate<T> GetSerializeDelegate(TypeMemberInterface[] memberInterfaces)
            => (WriterInterface writer, T obj) =>
            {
                if (writer is null) return false;
                if (obj is null) return writer.Null();

                if (!writer.BeginObject()) return false;

                foreach (var member in memberInterfaces)
                {
                    if (!writer.BeginMember(member.Name))
                        return false;

                    object value = member.Get(obj);

                    if (value is null)
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

        static DeserializeDelegate<T> GetDeserializeDelegate(Dictionary<StringView, TypeMemberInterface> memberDictionary)
            => (ReaderInterface reader, out T obj) =>
            {
                if (reader is null) { obj = default; return false; }

                if (reader.BeginObject())
                {
                    obj = (T)Activator.CreateInstance(typeof(T));

                    while (!reader.IsDone)
                    {
                        if (!reader.BeginMember(out var name))
                            return false;

                        if (!memberDictionary.TryGetValue(name, out TypeMemberInterface member))
                            return false;

                        if (!member.Deserialize(reader, out object value))
                            return false;

                        member.Set(obj, value);

                        if (!reader.EndValue())
                            break;
                    }

                    return reader.EndObject();
                }

                obj = default;
                return reader.Null();
            };

    }
}