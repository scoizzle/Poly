using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace System {
    static class CharExtensions {
        public static string ToHexString(this char C) {
            return ToHexString(Encoding.UTF8, C);
        }

        public static string ToHexString(this char C, Encoding Enc) {
            return ToHexString(Enc, C);
        }

        public static string ToHexString(Encoding Encoding, params char[] Chars) {
            return Encoding.GetBytes(Chars).ToHexString();
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
