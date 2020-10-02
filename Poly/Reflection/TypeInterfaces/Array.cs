using System;
using System.Collections.Generic;

using Poly.Serialization;

namespace Poly.Reflection.TypeInterfaces
{
    internal class Array<TElement> : TypeInterface<TElement[]>
    {
        static readonly TypeInterface<TElement> ElementInterface = TypeInterface<TElement>.Get();

        public SerializeDelegate<TElement[]> Serialize { get; } = GetSerializer(ElementInterface.Serialize);

        public DeserializeDelegate<TElement[]> Deserialize { get; } = GetDeserializer(ElementInterface.Deserialize);

        static SerializeDelegate<TElement[]> GetSerializer(SerializeDelegate<TElement> serializeElement)
        {
            return (WriterInterface writer, TElement[] array) =>
            {
                if (writer is null) return false;
                if (array is null) return writer.Null();

                if (!writer.BeginArray()) return false;

                foreach (var value in array)
                {
                    if (!serializeElement(writer, value))
                        return false;

                    if (!writer.EndValue())
                        break;
                }

                return writer.EndArray();
            };
        }

        static DeserializeDelegate<TElement[]> GetDeserializer(DeserializeDelegate<TElement> deserializeElement)
        {
            return (ReaderInterface reader, out TElement[] obj) =>
            {
                if (reader != null && reader.BeginArray())
                {
                    var list = new List<TElement>();

                    while (!reader.IsDone)
                    {
                        if (!deserializeElement(reader, out var element))
                        {
                            obj = default;
                            return false;
                        }

                        list.Add(element);

                        if (!reader.EndValue())
                            break;
                    }

                    if (reader.EndArray())
                    {
                        obj = new TElement[list.Count];
                        list.CopyTo(0, obj, 0, list.Count);
                        return true;
                    }
                }

                obj = default;
                return reader.Null();
            };
        }
    }
}