using System;
using System.IO;
using System.Text;

namespace Poly {
    using Data;

    public static class StringConversion {
        public static byte[] GetBytes(this string This) {
            return GetBytes(This, App.Encoding);
        }

        public static byte[] GetBytes(this string This, Encoding enc) {
            return enc.GetBytes(This);
        }

        public static Stream GetStream(this string This) {
            return GetStream(This, App.Encoding);
        }

        public static Stream GetStream(this string This, Encoding enc) {
            return new MemoryStream(GetBytes(This, enc), false);
        }

        public static void TryEscapeCharacter(StringBuilder output, char character) {
            switch (character) {
                case '\r': output.Append("\\r"); break;
                case '\n': output.Append("\\n"); break;
                case '\t': output.Append("\\t"); break;
                case '\f': output.Append("\\f"); break;
                case '\b': output.Append("\\b"); break;
                case '\"': output.Append("\\\""); break;
                case '\\': output.Append("\\\\"); break;
                default: output.Append(character); break;
            }
        }

        public static void TryDescapeCharacter(StringBuilder output, char character) {
            switch (character) {
                case 'r': output.Append("\r"); break;
                case 'n': output.Append("\n"); break;
                case 't': output.Append("\t"); break;
                case 'f': output.Append("\f"); break;
                case 'b': output.Append("\b"); break;
                case '"': output.Append("\""); break;
                default: output.Append(character); break;
            }
        }

        public static string Escape(this string This) {
            var output = new StringBuilder();

            for (var i = 0; i < This.Length; i++) {
                TryEscapeCharacter(output, This[i]);
            }

            return output.ToString();
        }

        public static string Descape(this string This) {
            var it = new StringIterator(This);
            var output = new StringBuilder();

            while (!it.IsDone) {
                if (it.IsAt('\\')) {
                    it.Consume();
                    TryDescapeCharacter(output, it.Current);
                }
                else {
                    output.Append(it.Current);
                }

                it.Consume();
            }

            return output.ToString();
        }

        public static string HtmlEscape(this string This) {
            return Uri.EscapeDataString(This);
        }

        public static string UriEscape(this string This) {
            return Uri.EscapeDataString(This);
        }

        public static string HtmlDescape(this string This) {
            return System.Text.RegularExpressions.Regex.Replace(This.Replace("+", " "), "%([A-Fa-f\\d]{2})", a => Convert.ToChar(Convert.ToInt32(a.Groups[1].Value, 16)).ToString());
        }

        public static string UriDescape(this string This) {
            return Uri.UnescapeDataString(This);
        }

        public static string MD5(this string This) {
            return Hashing.GetMD5(
                App.Encoding.GetBytes(
                    This
                )
            ).ToHexString();
        }

        public static string SHA1(this string This) {
            return Hashing.GetSHA1(
                App.Encoding.GetBytes(
                    This
                )
            ).ToHexString();
        }

        public static string SHA256(this string This) {
            return Hashing.GetSHA256(
                App.Encoding.GetBytes(
                    This
                )
            ).ToHexString();
        }

        public static string SHA512(this string This) {
            return Hashing.GetSHA512(
                App.Encoding.GetBytes(
                    This
                )
            ).ToHexString();
        }

        public static string Base64Encode(this string This) {
            return Convert.ToBase64String(
                App.Encoding.GetBytes(
                    This
                )
            );
        }

        public static string Base64Decode(this string This) {
            return App.Encoding.GetString(
                Convert.FromBase64String(
                    This
                )
            );
        }
    }
}