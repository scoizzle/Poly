using System;
using System.Collections.Generic;
using System.Linq;

using Poly.Serialization;

namespace Poly.Reflection.TypeInterfaces {
    internal class Decimal : TypeInterface<decimal> {
		public SerializeDelegate<decimal> Serialize
            => (WriterInterface writer, decimal value) => {
                return writer.Decimal(value);
            };

        public DeserializeDelegate<decimal> Deserialize
            => (ReaderInterface reader, out decimal value) => {
                return reader.Decimal(out value);
            };
    }
}