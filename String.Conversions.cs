using System;
using System.Collections.Generic;
using System.Text;
using System.Security;

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
            { '\\', "\\\\" },
            { '/', "\\/" }
        };

        private static Dictionary<char, string> DescapeChars = new Dictionary<char, string>() {
            { 'r', "\r" },
            { 'n', "\n" },
            { 't', "\t" },
            { 'f', "\f" },
            { '"', "\"" },
            { '\\', "\\" },
            { '/', "/" }
        };

        public static bool ToBool(this String This) {
            if (This.Compare("true", 0, true)) {
                return true;
            }
            else if (This.Compare("false", 0, true)) {
                return false;
            }
            return false;
        }

        public static bool ToBool(this String This, ref int Index, ref bool Value) {
            if (This.Compare("true", Index, true)) {
                Index += 4;
                Value = true;
                return true;
            }
            else if (This.Compare("false", Index, true)) {
                Index += 5;
                Value = false;
                return true;
            }
            return false;
        }

        public static int ToInt(this String This) {
            int Out = 0;

            if (Int32.TryParse(This, out Out))
                return Out;

            return 0;
        }

        public static bool ToInt(this String This, ref int Index, int LastIndex, ref int Value) {
            if (string.IsNullOrEmpty(This) || Index >= LastIndex)
                return false;

            int Offset = Index;
            This.ConsumeWhitespace(ref Offset);

            if (This[Offset] == '-' || This[Offset] == '+')
                Offset++;

            while (Offset < LastIndex) {
                if (char.IsDigit(This[Offset]))
                    Offset++;
                else if (This[Offset] == '.' || This[Offset] == 'e' || This[Offset] == 'E')
                    return false;
                else break;
            }

            if ((Offset - Index) == 0)
                return false;

            Value = int.Parse(This.Substring(Index, Offset - Index));
            Index = Offset;
            return true;
        }

        public static long ToLong(this String This) {
            long Out = 0;

            if (long.TryParse(This, out Out))
                return Out;

            return 0;
        }

        public static bool ToLong(this String This, ref int Index, int LastIndex, ref long Value) {
            if (string.IsNullOrEmpty(This) || Index >= LastIndex)
                return false;

            int Offset = Index;
            This.ConsumeWhitespace(ref Offset);

            if (This[Offset] == '-' || This[Offset] == '+')
                Offset++;

            while (Offset < LastIndex) {
                if (char.IsDigit(This[Offset]))
                    Offset++;
                else if (This[Offset] == '.')
                    return false;
            }

            if ((Offset - Index) == 0)
                return false;

            if (long.TryParse(This.Substring(Index, Offset - Index), out Value)) {
                Index = Offset;
                return true;
            }

            return false;
        }

        public static float ToFloat(this String This) {
            float Out = float.NaN;

            if (float.TryParse(This, out Out))
                return Out;

            return float.NaN;
        }

        public static bool ToFloat(this String This, ref int Index, int LastIndex, ref float Value) {
            if (string.IsNullOrEmpty(This) || Index >= LastIndex)
                return false;

            int Offset = Index;
            This.ConsumeWhitespace(ref Offset);

            if (This[Offset] == '-' || This[Offset] == '+')
                Offset++;

            while (Offset < LastIndex) {
                if (char.IsDigit(This[Offset]) || This[Offset] == ',')
                    Offset++;
                else if (This[Offset] == '.') {
                    Offset++;
                    break;
                }
                else return false;
            }

            while (Offset < LastIndex) {
                if (char.IsDigit(This[Offset]))
                    Offset++;
                else if (This[Offset] == 'e' || This[Offset] == 'E') {
                    Offset++;
                    break;
                }
                else return false;
            }

            if (Offset < LastIndex && (This[Offset] == '-' || This[Offset] == '+'))
                Offset++;

            while (Offset < LastIndex) {
                if (char.IsDigit(This[Offset]))
                    Offset++;
                else return false;
            }

            if ((Offset - Index) == 0)
                return false;

            Value = float.Parse(This.Substring(Index, Offset - Index));
            Index = Offset;
            return true;
        }

        public static double ToDouble(this String This) {
            double Out = double.NaN;

            if (double.TryParse(This, out Out))
                return Out;

            return double.NaN;
        }

        public static bool ToDouble(this String This, ref int Index, int LastIndex, ref double Value) {
            if (string.IsNullOrEmpty(This) || Index >= LastIndex)
                return false;

            int Offset = Index;
            This.ConsumeWhitespace(ref Offset);

            if (This[Offset] == '-' || This[Offset] == '+')
                Offset++;

            while (Offset < LastIndex) {
                if (char.IsDigit(This[Offset]) || This[Offset] == ',')
                    Offset++;
                else if (This[Offset] == '.') {
                    Offset++;
                    break;
                }
                else return false;
            }

            while (Offset < LastIndex) {
                if (char.IsDigit(This[Offset]))
                    Offset++;
                else if (This[Offset] == 'e' || This[Offset] == 'E') {
                    Offset++;
                    break;
                }
                else if (This[Offset] == ';')
                    break;
                else return false;
            }

            if (Offset < LastIndex && (This[Offset] == '-' || This[Offset] == '+')) { 
                    Offset++;

                while (Offset < LastIndex) {
                    if (char.IsDigit(This[Offset]))
                        Offset++;
                    else return false;
                }
            }

            if ((Offset - Index) == 0)
                return false;

            if (double.TryParse(This.Substring(Index, Offset - Index), out Value)) {
                Index = Offset;
                return true;
            }

            return false;
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
                    case '\\':
                    case '/':
                        NewLength++;
                        break;
                }
            }

            char[] Array = new char[NewLength];


            for (int i = 0, o = 0; o < This.Length && i < NewLength; i++, o++) {
                if (EscapeChars.ContainsKey(This[o])) {
                    var Esc = EscapeChars[This[o]];

                    Array[i] = Esc[0];
                    Array[i + 1] = Esc[1];
                    i++;
                }
                else {
                    Array[i] = This[o];
                }
            }

            return new string(Array);
        }

        public static string HtmlEscape(this String This) {
            return SecurityElement.Escape(This);
        }

        public static string UriEscape(this String This) {
            return Uri.EscapeDataString(This);
        }

        private static char FindDescapeChar(string Val) {
            foreach (var Pair in EscapeChars) {
                if (Pair.Value == Val)
                    return Pair.Key;
            }
            return '\x00';
        }

        public static string Descape(this String This) {
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

        public static string HtmlDescape(this String This) {
            return System.Text.RegularExpressions.Regex.Replace(This.Replace("+", " "), "%([A-Fa-f\\d]{2})", a => "" + Convert.ToChar(Convert.ToInt32(a.Groups[1].Value, 16)));
        }

        public static string UriDescape(this String This) {
            return Uri.UnescapeDataString(This);
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