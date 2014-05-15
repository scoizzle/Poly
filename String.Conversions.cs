using System;
using System.Collections.Generic;
using System.Text;

using Poly;
using Poly.Data;

namespace System {
    public static class StringConversions {
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
            StringBuilder Output = new StringBuilder();

            for (int Index = 0; Index < This.Length; Index++) {
                switch (This[Index]) {
                    default:
                        Output.Append(This[Index]);
                        continue;

                    case '\r':
                        Output.Append("\\r");
                        break;
                    case '\n':
                        Output.Append("\\n");
                        break;
                    case '\t':
                        Output.Append("\\t");
                        break;
                    case '\f':
                        Output.Append("\\f");
                        break;
                    case '"':
                        Output.Append("\\\"");
                        break;
                    case '\'':
                        Output.Append("\\'");
                        break;
                    case '\\':
                        Output.Append("\\\\");
                        break;
                    case '/':
                        Output.Append("\\/");
                        break;
                    case '.':
                        Output.Append("\\.");
                        break;
                }
            }

            return Output.ToString();
        }

        public static string Descape(this String This) {
            if (This.Find('\\') == -1)
                return This;

            StringBuilder Output = new StringBuilder();

            for (int Index = 0; Index < This.Length; ) {
                if (This.Length - (Index + 2) < 0) {
                    Output.Append(This[Index]);
                    break;
                }

                switch (This.SubString(Index, 2)) {
                    default:
                        Output.Append(This[Index]);
                        Index++;
                        continue;
                    case "\\r":
                        Output.Append('\r');
                        break;
                    case "\\n":
                        Output.Append('\n');
                        break;
                    case "\\t":
                        Output.Append("\t");
                        break;
                    case "\\f":
                        Output.Append("\f");
                        break;
                    case "\\\"":
                        Output.Append("\"");
                        break;
                    case "\\\'":
                        Output.Append("\'");
                        break;
                    case "\\\\":
                        Output.Append("\\");
                        break;
                    case "\\/":
                        Output.Append("/");
                        break;
                    case "\\.":
                        Output.Append(".");
                        break;
                    case "\\u":
                        string Code = This.SubString(Index + 2, 4);

                        char Character = Convert.ToChar(Int32.Parse(Code, Globalization.NumberStyles.HexNumber));

                        Output.Append(Character);

                        Index += 5;
                        continue;
                }
                Index += 2;
            }

            return Output.ToString();
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