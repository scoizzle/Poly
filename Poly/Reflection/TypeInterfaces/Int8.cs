using System;
using System.Collections.Generic;
using System.Linq;

using Poly.Serialization;

namespace Poly.Reflection.TypeInterfaces
{
    internal class Int8 : TypeInterface<sbyte>
    {
        public SerializeDelegate<sbyte> Serialize { get; } =
            (WriterInterface writer, sbyte value) =>
            {
                return writer.Int8(value);
            };

        public DeserializeDelegate<sbyte> Deserialize { get; } =
            (ReaderInterface reader, out sbyte value) =>
            {
                return reader.Int8(out value);
            };
    }
}