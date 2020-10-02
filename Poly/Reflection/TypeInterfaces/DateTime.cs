using System;
using System.Collections.Generic;
using System.Linq;

using Poly.Serialization;

namespace Poly.Reflection.TypeInterfaces
{
    internal class DateTime : TypeInterface<System.DateTime>
    {
        public SerializeDelegate<System.DateTime> Serialize { get; } =
            (WriterInterface writer, System.DateTime value) =>
            {
                return writer.DateTime(value);
            };

        public DeserializeDelegate<System.DateTime> Deserialize { get; } =
            (ReaderInterface reader, out System.DateTime value) =>
            {
                return reader.DateTime(out value);
            };
    }
}