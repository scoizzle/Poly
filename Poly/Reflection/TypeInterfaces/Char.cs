using System;
using System.Collections.Generic;
using System.Linq;

using Poly.Serialization;

namespace Poly.Reflection.TypeInterfaces {
    internal class Char : TypeInterface<char> {
		public SerializeDelegate<char> Serialize
            => (WriterInterface writer, char value) => {
                return writer.Char(value);
            };

        public DeserializeDelegate<char> Deserialize
            => (ReaderInterface reader, out char value) => {
                return reader.Char(out value);
            };
    }
}