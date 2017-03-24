using System;
using System.Collections.Generic;
using System.Text;
using System.Net.Security;

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
            { '\\', "\\\\" }
        };

        private static Dictionary<char, string> DescapeChars = new Dictionary<char, string>() {
            { 'r', "\r" },
            { 'n', "\n" },
            { 't', "\t" },
            { 'f', "\f" },
            { '"', "\"" },
            { '\\', "\\" }
        };
        
        public static string Escape(this string This) {
            StringBuilder Output = new StringBuilder();

            for (int i = 0; i < This.Length; i++) {
                var c = This[i];

                if (c == '\r' ||
                    c == '\n' ||
                    c == '\t' ||
                    c == '\f' ||
                    c == '\"' ||
                    c == '\\')
                    Output.Append(EscapeChars[c]);
                else Output.Append(c);
            }

            return Output.ToString();
        }

        public static string HtmlEscape(this string This) {
            return Uri.EscapeDataString(This);
        }

        public static string UriEscape(this string This) {
            return Uri.EscapeDataString(This);
        }

        public static string Descape(this string This) {
            StringIterator It = new StringIterator(This);
            StringBuilder Output = new StringBuilder();

            while (!It.IsDone()) {
                var Index = It.Index;

                if (It.Goto('\\')) {
                    Output.Append(This, Index, It.Index - Index);

                    It.Tick();

                    if (It.IsAt('u')) {
                        It.Tick();

                        Output.Append(
                            Convert.ToChar(
                                Int32.Parse(
                                    This.Substring(It.Index, 4), Globalization.NumberStyles.HexNumber
                                )
                            )
                        );

                        It.Index += 4;
                    }
                    else if (DescapeChars.ContainsKey(It.Current)) {
                        Output.Append(DescapeChars[It.Current]);
                        It.Tick();
                    }
                    else {
                        Output.Append(It.Current);
                        It.Tick();
                    }
                }
                else {
                    Output.Append(This, Index, This.Length - Index);
                    break;
                }
            }

            return Output.ToString();
        }

        public static string HtmlDescape(this string This) {
            return System.Text.RegularExpressions.Regex.Replace(This.Replace("+", " "), "%([A-Fa-f\\d]{2})", a => Convert.ToChar(Convert.ToInt32(a.Groups[1].Value, 16)).ToString());
        }

        public static string UriDescape(this string This) {
            return Uri.UnescapeDataString(This);
        }

        public static string MD5(this string This) {
            return Hash.ToMD5(
                Encoding.UTF8.GetBytes(
                    This
                )
            );
        }

        public static string SHA1(this string This) {
            if (string.IsNullOrEmpty(This))
                return "";

            return Hash.ToSHA1(
                Encoding.UTF8.GetBytes(
                    This
                )
            );
        }

        public static string SHA256(this string This) {
            return Hash.ToSHA256(
                Encoding.UTF8.GetBytes(
                    This
                )
            );
        }

        public static string SHA512(this string This) {
            return Hash.ToSHA512(
                Encoding.UTF8.GetBytes(
                    This
                )
            );
        }

        public static string Base64Encode(this string This) {
            return Convert.ToBase64String(
                Encoding.UTF8.GetBytes(
                    This
                )
            );
        }

        public static string Base64Decode(this string This) {
            return Encoding.UTF8.GetString(
                Convert.FromBase64String(
                    This
                )
            );
        }

        public static string ToString(this string This, params object[] Arguments) {
            if (string.IsNullOrEmpty(This))
                return "";
                
            return string.Format(This, Arguments);
        }
    }
}