using System;
using System.Collections.Generic;
using System.Linq;

using Poly.Serialization;

namespace Poly.Reflection.TypeInterfaces
{
    internal class TimeSpan : TypeInterface<System.TimeSpan>
    {
        public SerializeDelegate<System.TimeSpan> Serialize { get; } =
            (WriterInterface writer, System.TimeSpan value) =>
            {
                return writer.TimeSpan(value);
            };

        public DeserializeDelegate<System.TimeSpan> Deserialize { get; } =
            (ReaderInterface reader, out System.TimeSpan value) =>
            {
                return reader.TimeSpan(out value);
            };
    }
}