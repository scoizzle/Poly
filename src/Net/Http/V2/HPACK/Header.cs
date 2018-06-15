using System.Collections.Generic;

namespace Poly.Net.Http.V2.HPACK {

    public class Header {
        public int Size;

        public string Key;
        public string Value;

        public Header(string key, string value) {
            Key = key;
            Value = value;
            
            Size =
                App.Encoding.GetByteCount(key) +
                App.Encoding.GetByteCount(value) +
                32;
        }

        public override string ToString()  =>
            $"({Size}) {Key}: {Value}";
    }
}