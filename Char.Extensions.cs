using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Poly {
    static class CharExtensions {
        private static string HexAlph = "0123456789ABCDEF";
        public static string ToHexString(this char C) {
            char[] Buf = new char[2];
            byte CB = Convert.ToByte(C);

            Buf[0] = HexAlph[CB >> 4];
            Buf[1] = HexAlph[CB & 0xF];

            return new string(Buf);
        }
    }
}
