using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace System {
    static class CharExtensions {
        private static string HexAlph = "0123456789ABCDEF";
        public static string ToHexString(this char C) {
            char[] Buf = new char[2];
            byte CB = Convert.ToByte(C);

            Buf[0] = HexAlph[CB >> 4];
            Buf[1] = HexAlph[CB & 0xF];

            return new string(Buf);
        }

        public static bool CompareWithoutCase(this char C, char S) {
            if (C == S)
                return true;

            if (C >= 'A' && C <= 'Z') {
                if (S >= 'a' && S <= 'z')
                    return C - 'A' == S - 'a';
            }
            else {
                if (S >= 'A' && S <= 'Z')
                    return C - 'a' == S - 'A';
            }

            return false;
        }
    }
}
