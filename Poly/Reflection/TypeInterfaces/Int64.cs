using System;
using System.Collections.Generic;
using System.Linq;

using Poly.Serialization;

namespace Poly.Reflection.TypeInterfaces
{
    internal class Int64 : TypeInterface<long>
    {
        public SerializeDelegate<long> Serialize { get; } =
            (WriterInterface writer, long value) =>
            {
                return writer.Int64(value);
            };

        public DeserializeDelegate<long> Deserialize { get; } =
            (ReaderInterface reader, out long value) =>
            {
                return reader.Int64(out value);
            };
    }
}