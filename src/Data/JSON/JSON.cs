using System;
using System.Text;
using System.Collections.Generic;

namespace Poly.Data {
    using Collections;

    public partial class JSON : Dictionary<string, object> {
        public bool IsArray;
        public char KeySeperatorCharacter;

        public JSON() : base(StringComparer.Ordinal) {
            IsArray = false;
            KeySeperatorCharacter = '.';
        }
        
        public void Add(object obj) {
            Add(Count.ToString(), obj);
        }

        public override string ToString() {
            var output = new StringBuilder();
            if (Serializer.Serialize(output, this))
                return output.ToString();
            return null;
        }
        

        public static JSONSerializer Serializer = new JSONSerializer();

        public static implicit operator JSON(string text) =>
            Serializer.Deserialize(text, out JSON value) ? value : default;
    }
}