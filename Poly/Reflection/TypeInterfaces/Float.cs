using System;
using System.Collections.Generic;
using System.Linq;

using Poly.Serialization;

namespace Poly.Reflection.TypeInterfaces {
    internal class Float : TypeInterface<float> {
		public SerializeDelegate<float> Serialize
            => (WriterInterface writer, float value) => {
                return writer.Float32(value);
            };

        public DeserializeDelegate<float> Deserialize
            => (ReaderInterface reader, out float value) => {
                return reader.Float32(out value);
            };
    }
}