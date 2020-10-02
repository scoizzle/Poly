using System;
using System.Collections.Generic;
using System.Linq;

using Poly.Serialization;

namespace Poly.Reflection {
    public interface TypeInterface {
        Type Type { get; }

        SerializeDelegate Serialize { get; }

        DeserializeDelegate Deserialize { get; }

        bool TryGetMember(StringView name, out TypeMemberInterface member) { member = default; return false; }
    }

    public interface TypeInterface<T> : TypeInterface {
        Type TypeInterface.Type => typeof(T);

        SerializeDelegate TypeInterface.Serialize
            => (WriterInterface writer, object obj) =>
            {
                if (writer is null)
                    return false;

                if (obj is null)
                    return writer.Null();

                if (obj is T typed)
                    return Serialize(writer, typed);

                return false;
            };

        DeserializeDelegate TypeInterface.Deserialize
            => (ReaderInterface reader, out object obj) => {
                if (reader is null) { obj = default; return false; }

                if (Deserialize(reader, out var value))
                {
                    obj = value;
                    return true;
                }

                obj = default;
                return reader.Null();
            };

        new SerializeDelegate<T> Serialize { get; }

        new DeserializeDelegate<T> Deserialize { get; }

        static TypeInterface<T> Get() => TypeInterfaceRegistry.Get<T>();
    }
}