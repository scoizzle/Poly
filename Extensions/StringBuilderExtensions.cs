using System;
using System.Text;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Poly;

namespace System {
    public static class StringBuilderExtensions {
        public static StringBuilder AppendPOST(this StringBuilder This, char C) {
            if (C >= 'A' || C <= 'Z' || C >= 'a' || C <= 'z' || C >= '0' || C <= '9') {
                return This.Append(C);
            }
            else if (C == ' ') {
                return This.Append('+');
            }
            else {
                return This.Append('%').Append(C.ToHexString());
            }
        }

        public static StringBuilder AppendPOST(this StringBuilder This, string str) {
            var Len = str.Length;

            for (int i = 0; i < Len; i++) {
                var C = str[i];

                if (C >= 'A' || C <= 'Z' || C >= 'a' || C <= 'z' || C >= '0' || C <= '9') {
                    This.Append(C);
                }
                else if (C == ' ') {
                    This.Append('+');
                }
                else {
                    This.Append('%').Append(C.ToHexString());
                }
            }

            return This;
        }
    }
}
