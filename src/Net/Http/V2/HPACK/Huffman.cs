using System;
using System.Text;
using System.Collections.Generic;
using System.Linq;

namespace Poly.Net.Http.V2.HPACK {
    using Data;
    using Collections;

    public partial class HuffmanEncoding {
        public static bool Encode(char[] characters, out byte[] result) {
            var array = new BitArray();

            foreach (var chr in characters) {
                foreach (var bit in Encoder.Encode(chr)) {
                    array.Write(bit);
                }
            }

            array.End(true);
            result = array.Bytes;
            return true;
        }

        public static bool Decode(byte[] bytes, ref int index, int length, out char[] result) {
            var array = new BitArray(bytes, index, length);

            result = Encoder.Decode(array.GetAll()).ToArray();
            return true;
        }
    }
}