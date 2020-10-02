using System;
using System.Collections.Generic;
using System.Linq;

using Poly.Serialization;

namespace Poly.Reflection.TypeInterfaces
{
    internal class UInt16 : TypeInterface<ushort>
    {
        public SerializeDelegate<ushort> Serialize { get; } =
            (WriterInterface writer, ushort value) =>
            {
                return writer.UInt16(value);
            };

        public DeserializeDelegate<ushort> Deserialize { get; } =
            (ReaderInterface reader, out ushort value) =>
            {
                return reader.UInt16(out value);
            };
    }
}