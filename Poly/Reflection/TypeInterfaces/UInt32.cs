using System;
using System.Collections.Generic;
using System.Linq;

using Poly.Serialization;

namespace Poly.Reflection.TypeInterfaces {
    internal class UInt32 : TypeInterface<uint> {
		public SerializeDelegate<uint> Serialize
            => (WriterInterface writer, uint value) => {
                return writer.UInt32(value);
            };
            
        public DeserializeDelegate<uint> Deserialize
            => (ReaderInterface reader, out uint value) => {
                return reader.UInt32(out value);
            };
    }
}