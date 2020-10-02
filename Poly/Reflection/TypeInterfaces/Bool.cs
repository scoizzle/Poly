using System;
using System.Collections.Generic;
using System.Linq;

using Poly.Serialization;

namespace Poly.Reflection.TypeInterfaces {
    internal class Boolean : TypeInterface<bool> {
		public SerializeDelegate<bool> Serialize
            => (WriterInterface writer, bool value) => {
                return writer.Boolean(value);
            };

        public DeserializeDelegate<bool> Deserialize
            => (ReaderInterface reader, out bool value) => {
                return reader.Boolean(out value);
            };
    }
}