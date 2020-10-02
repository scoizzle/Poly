using System;
using System.Collections.Generic;
using System.Linq;

using Poly.Serialization;

namespace Poly.Reflection.TypeInterfaces {
    internal class Double : TypeInterface<double> {
		public SerializeDelegate<double> Serialize
            => (WriterInterface writer, double value) => {
                return writer.Float64(value);
            };

        public DeserializeDelegate<double> Deserialize
            => (ReaderInterface reader, out double value) => {
                return reader.Float64(out value);
            };
    }
}