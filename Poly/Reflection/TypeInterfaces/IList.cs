using System;
using System.Collections.Generic;

using Poly.Serialization;

namespace Poly.Reflection.TypeInterfaces {
    internal class IList<T, TElement> : TypeInterface<T> where T : IList<TElement>
    {
        TypeInterface<TElement> ElementInterface { get; } = TypeInterface<TElement>.Get();

        public SerializeDelegate<T> Serialize
            => (WriterInterface writer, T list) => {
                if (writer is null) return false;
                if (list is null) return writer.Null();
                    
                if (!writer.BeginArray()) return false;

                foreach (var element in list)
                {
                    if (!ElementInterface.Serialize(writer, element))
                        return false;

                    if (!writer.EndValue())
                        break;
                }

                return writer.EndArray();
            };

        public DeserializeDelegate<T> Deserialize
            => (ReaderInterface reader, out T list) => {
                if (reader != null && reader.BeginArray())
                {
                    list = (T)Activator.CreateInstance(typeof(T));

                    while (!reader.IsDone)
                    {
                        if (!ElementInterface.Deserialize(reader, out var value))
                            return false;

                        list.Add(value);

                        if (!reader.EndValue())
                            break;
                    }

                    return reader.EndArray();
                }

                list = default;
                return reader.Null();
            };
    }
}