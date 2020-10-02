using System;
using System.Collections.Generic;
using System.Linq;

using Poly.Serialization;

namespace Poly.Reflection.TypeInterfaces
{
    internal class Int32 : TypeInterface<int>
    {
        public SerializeDelegate<int> Serialize { get; } =
            (WriterInterface writer, int value) =>
            {
                return writer.Int32(value);
            };

        public DeserializeDelegate<int> Deserialize { get; } =
            (ReaderInterface reader, out int value) =>
            {
                return reader.Int32(out value);
            };
    }
}