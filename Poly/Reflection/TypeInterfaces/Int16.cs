using System;
using System.Collections.Generic;
using System.Linq;

using Poly.Serialization;

namespace Poly.Reflection.TypeInterfaces
{
    internal class Int16 : TypeInterface<short>
    {
        public SerializeDelegate<short> Serialize { get; } =
            (WriterInterface writer, short value) =>
            {
                return writer.Int16(value);
            };

        public DeserializeDelegate<short> Deserialize { get; } =
            (ReaderInterface reader, out short value) =>
            {
                return reader.Int16(out value);
            };
    }
}