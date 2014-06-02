﻿using System;
using System.Collections.Generic;
using System.Text;

using Poly;
using Poly.Data;

namespace System {
    public static class StringConversions {
        private static Dictionary<char, string> EscapeChars = new Dictionary<char, string>() {
            { '\r', "\\r" },
            { '\n', "\\n" },
            { '\t', "\\t" },
            { '\f', "\\f" },
            { '\"', "\\\"" },
            { '\'', "\\'" },
            { '\\', "\\\\" },
            { '/', "\\/" },
            { '.', "\\." }
        };

        public static bool ToBool(this String This) {
            bool Out = false;

            if (Boolean.TryParse(This, out Out))
                return Out;

            return false;
        }

        public static int ToInt(this String This) {
            int Out = 0;

            if (Int32.TryParse(This, out Out))
                return Out;

            return 0;
        }

        public static double ToDouble(this String This) {
            double Out = double.NaN;

            if (double.TryParse(This, out Out))
                return Out;

            return double.NaN;
        }

        public static string Escape(this String This) {
            int NewLength = This.Length;

            for (int i = 0; i < This.Length; i++) {
                switch (This[i]) {
                    case '\r':
                    case '\n':
                    case '\t':
                    case '\f':
                    case '\"':
                    case '\'':
                    case '\\':
                    case '/':
                    case '.':
                        NewLength++;
                        break;
                }
            }

            char[] Array = new char[NewLength];


            for (int i = 0, o = 0; o < This.Length && i < NewLength; i++, o++) {
                if (EscapeChars.ContainsKey(This[i])) {
                    var Esc = EscapeChars[This[i]];

                    Array[o] = Esc[0];
                    Array[o + 1] = Esc[1];
                    o++;
                }
                else {
                    Array[o] = This[i];
                }
            }

            return new string(Array);
        }

        private static char FindDescapeChar(string Val) {
            foreach (var Pair in EscapeChars) {
                if (Pair.Value == Val)
                    return Pair.Key;
            }
            return '\x00';
        }

        public static string Descape(this String This) {
            if (This.Find('\\') == -1)
                return This;

            int NewLength = This.Length;

            NewLength -= This.CountOf("\\u") * 5;
            NewLength -= (This.CountOf("\\") - This.CountOf("\\\\"));

            char[] Array = new char[NewLength];

            for (int i = 0, o = 0; o < This.Length && i < NewLength; i++, o++) {
                if (This[o] == '\\') {
                    if (This[o + 1] == 'u') {
                        Array[i] = Convert.ToChar(
                            Int32.Parse(This.SubString(o + 2, 4), Globalization.NumberStyles.HexNumber)
                        );
                    }
                    else {
                        var Desc = This.SubString(o, 2);
                        var C = FindDescapeChar(Desc);

                        if (C != '\x00') {
                            Array[i] = C;
                        }
                        else {
                            Array[i] = This[o + 1];
                        }
                    }
                    o++;
                }
                else {
                    Array[i] = This[o];
                }
            }

            return new string(Array);
        }

        public static string MD5(this String This) {
            return Hash.MD5(
                Encoding.Default.GetBytes(
                    This
                )
            );
        }

        public static string SHA1(this String This) {
            if (string.IsNullOrEmpty(This))
                return "";

            return Hash.SHA1(
                Encoding.Default.GetBytes(
                    This
                )
            );
        }

        public static string SHA256(this String This) {
            return Hash.SHA256(
                Encoding.Default.GetBytes(
                    This
                )
            );
        }

        public static string SHA512(this String This) {
            return Hash.SHA512(
                Encoding.Default.GetBytes(
                    This
                )
            );
        }

        public static string Base64Encode(this String This) {
            return Convert.ToBase64String(
                Encoding.Default.GetBytes(
                    This
                )
            );
        }

        public static string Base64Decode(this String This) {
            return Encoding.Default.GetString(
                Convert.FromBase64String(
                    This
                )
            );
        }

        public static string x(this String This, int Count) {
            var Output = new StringBuilder();

            for (; Count > 0; Count--)
                Output.Append(This);

            return Output.ToString();
        }

        public static string ToString(this String This, params object[] Arguments) {
            if (string.IsNullOrEmpty(This))
                return "";
            return string.Format(This, Arguments);
        }

        public static jsObject ToJsObject(this String This) {
            return (jsObject)(This);
        }
    }
}