using System;
using System.Collections.Generic;
using System.Linq;

using Poly.Serialization;

namespace Poly.Reflection.TypeInterfaces {
    internal class String : TypeInterface<string>
    {
        TypeMemberInterface _lengthInterface;

        public String() {
            _lengthInterface = new TypeMember(this, Type.GetProperty("Length"));
        }

        public Type Type { get; } = typeof(string);

		public SerializeDelegate<string> Serialize { get; } =
            (WriterInterface writer, string value) => {
                if (writer is null) return false;
                if (value is null) return writer.Null();

                return writer.String(value);
            };

        public DeserializeDelegate<string> Deserialize { get; } =
            (ReaderInterface reader, out string value) => {
                if (reader is null) { value = default; return false; }

                return reader.String(out value) || reader.Null();
            };

        public bool TryGetMember(string name, out TypeMemberInterface member) {
            switch (name) {
                case "Length": {
                    member = _lengthInterface;
                    return true;
                }
            }

            member = default;
            return false;
        }
    }
}