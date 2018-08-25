using System.Collections.Generic;
using System.Text;

namespace Poly.Net.Http.V2.HPACK {

    public class Header {
        public int Size;

        public string Key;
        public string Value;

        public Header(string key, string value) {
            Key = key;
            Value = value;
            
            Size =
                Encoding.ASCII.GetByteCount(key) +
                Encoding.ASCII.GetByteCount(value) +
                32;
        }

        public override string ToString()  =>
            $"({Size}) {Key}: {Value}";
    }
}