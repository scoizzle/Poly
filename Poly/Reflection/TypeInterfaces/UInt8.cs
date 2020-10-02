using System;
using System.Collections.Generic;
using System.Linq;

using Poly.Serialization;

namespace Poly.Reflection.TypeInterfaces
{
    internal class UInt8 : TypeInterface<byte>
    {
        public SerializeDelegate<byte> Serialize { get; } =
            (WriterInterface writer, byte value) =>
            {
                return writer.UInt8(value);
            };

        public DeserializeDelegate<byte> Deserialize { get; } =
            (ReaderInterface reader, out byte value) =>
            {
                return reader.UInt8(out value);
            };
    }
}