using System;
using System.Collections.Generic;
using System.Linq;

using Poly.Serialization;

namespace Poly.Reflection.TypeInterfaces
{
    internal class UInt64 : TypeInterface<ulong>
    {
        public SerializeDelegate<ulong> Serialize { get; } =
            (WriterInterface writer, ulong value) =>
            {
                return writer.UInt64(value);
            };

        public DeserializeDelegate<ulong> Deserialize { get; } =
            (ReaderInterface reader, out ulong value) =>
            {
                return reader.UInt64(out value);
            };
    }
}