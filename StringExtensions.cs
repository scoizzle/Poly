using System;
using System.Collections.Generic;
using System.Text;

using Poly;
using Poly.Data;

namespace System {
    public static class StringExtensions {
        private static readonly string[] WildChars = new string[] {
            "{", "*", "?", "^"
        };

        public static int CountOf(this String This, String ToFind) {
            int Count = 0, X = 0;

            while ((X = This.IndexOf(ToFind, X)) != -1) {
                Count++;
                X += ToFind.Length;
            }

            return Count;
        }

        public static bool IsBoolean(this String This) {
            bool Value = false;
            return bool.TryParse(This, out Value);
        }

        public static bool IsNumeric(this String This) {
            double Value = 0;
            return double.TryParse(This, out Value);
        }

        public static bool Compare(this String This, String Possible, int Index, bool IgnoreCase = false) {
            int X = 0;
            for (; Index + X < This.Length && X < Possible.Length; X++) {
                if (IgnoreCase) {
                    if (char.ToLower(This[Index + X]) != char.ToLower(Possible[X])) {
                        return false;
                    }
                }
                else if (This[Index + X] != Possible[X])
                    return false;
            }

            return X == Possible.Length;
        }

        public static bool Compare(this String This, String Wild, bool IgnoreCase = true, int Index = 0) {
            if (string.IsNullOrEmpty(This) || string.IsNullOrEmpty(Wild)) {
                return false;
            }

            if (IgnoreCase) {
                This = This.ToLower();
                Wild = Wild.ToLower();
            }

            int x = Index, y = 0,
                X = This.Length,
                Y = Wild.Length;

            if (Wild == "*")
                return true;

            while (x < X && y < Y) {
                if (Wild[y] == This[x] || Wild[y] == '?') {
                    x++;
                    y++;
                }
                else if (Wild[y] == '*') {
                    y++;

                    if (y == Y) {
                        break;
                    }
                    else if (y + 1 == Y) {
                        if (This[X - 1] == Wild[y]) {
                            return true;
                        }
                        return false;
                    }

                    var rawValue = string.Empty;
                    int tmp = -1, z = -1;

                    rawValue = Wild.FirstPossible(y, WildChars);

                    if (string.IsNullOrEmpty(rawValue)) {
                        rawValue = Wild.Substring(y, Wild.Length - y);
                        z = This.IndexOf(rawValue, x);
                    }
                    else {
                        tmp = Wild.IndexOf(rawValue, y);
                        if (tmp - y > 0) {
                            rawValue = Wild.Substring(y, tmp - y);
                        }
                        z = This.IndexOf(rawValue, x);
                    }

                    if (z == -1)
                        return false;

                    y = Wild.IndexOf(rawValue, y);
                    x = z;
                }
                else if (Wild[y] == '^') {
                    while (x < X && char.IsWhiteSpace(This[x])) {
                        x++;
                    }
                    if (Wild[y + 1] == '\\') {
                        y++;
                    }
                    if (This[x] != Wild[y + 1]) {
                        return false;
                    }
                    y++;
                }
                else {
                    return false;
                }
            }

            if (x == X && (Y - y ) < 3) {
                if (Wild[Y - 1] == '*')
                    return true;
            }

            return x == X && y == Y;
        }

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
            StringBuilder Output = new StringBuilder();

            for (int Index = 0; Index < This.Length; ) {
                if (This.Length - (Index + 2) < 0) {
                    Output.Append(This[Index]);
                    break;
                }

                switch (This.Substring(Index, 2)) {
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
                        string Code = This.Substring(Index + 2, 4);

                        char Character = Convert.ToChar(Int16.Parse(Code));

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

        public static string FirstPossible(this String This, int Start = 0, params String[] Possible) {
            int Index = int.MaxValue, Old = int.MaxValue;

            if (This != null && This != "") {
                for (int X = 0, Y = int.MaxValue; X < Possible.Length; X++) {
                    if ((Y = This.IndexOf(Possible[X], Start)) != -1) {
                        if (Y < Old) {
                            Index = X;
                            Old = Y;
                        }
                    }
                }
            }

            if (Index == -1 || Index > Possible.Length)
                return "";

            return Possible[Index];
        }

        public static string Substring(this String This, String Start, String Stop = "", int Index = 0, bool includeStartStop = false, bool LastStop = false) {
            int X = -1, Y = -1;
            StringBuilder Output = new StringBuilder();

            if (Start == "") {
                X = Index;
            }
            else {
                X = This.IndexOf(Start, Index);
            }

            if (X == -1) {
                return "";
            }
            else {
                while (X > 0 && This[X - 1] == '\\') {
                    X = This.IndexOf(Start, X + Start.Length);
                }
            }

            if (Stop == "") {
                Y = This.Length;
            }
            else if (LastStop) {
                Y = This.LastIndexOf(Stop);
            }
            else {
                Y = This.IndexOf(Stop, X + Start.Length);
            }

            if (X == -1 || Y == -1)
                return string.Empty;

            if (includeStartStop) {
                Y += Stop.Length;
            }
            else {
                X += Start.Length;
            }

            for (; X < Y && X < This.Length; X++) {
                Output.Append(This[X]);
            }

            return Output.ToString();
        }

        public static string FindMatchingBrackets(this String This, String Open, String Close, int Index = 0, bool includeBrackets = false) {
            int X, Y, Z = 1;
            StringBuilder Output = new StringBuilder();

            X = This.IndexOf(Open, Index);

            for (Y = X + Open.Length; Y < This.Length; Y++) {
                if (This[Y] == '\\') {
                    Y++;
                    continue;
                }

                if (This.Compare(Open, Y)) {
                    if (Open == Close) {
                        break;
                    }
                    else {
                        Z++;
                        continue;
                    }
                }

                if (This.Compare(Close, Y)) {
                    Z--;

                    if (Z == 0) {
                        break;
                    }
                }
            }

            if (X == -1 || Y == -1)
                return "";

            if (includeBrackets) {
                Y += Close.Length;
            }
            else {
                X += Open.Length;
            }

            for (; X < Y && X < This.Length; X++) {
                Output.Append(This[X]);
            }

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

        public static jsObject Match(this String Data, String MatchString, bool IgnoreCase = false, jsObject Storage = null, int ThisIndex = 0, int WildIndex = 0) {
            if (string.IsNullOrEmpty(Data) || string.IsNullOrEmpty(MatchString)) {
                return null;
            }

            String This = null, Wild = null;

            if (IgnoreCase) {
                This = Data.ToLower();
                Wild = MatchString.ToLower();
            }
            else {
                This = Data;
                Wild = MatchString;
            }

            jsObject Value = Storage == null ?
                new jsObject() :
                Storage;

            int x = ThisIndex, y = WildIndex,
                X = This.Length,
                Y = Wild.Length;

            while (x < X && y < Y) {
                if (Wild[y] == '\\') {
                    y++;
                }

                if (Wild[y] == '{' && (y > 0 ? Wild[y - 1] != '\\' : true)) {
                    string rawName, rawValue = "";

                    rawName = MatchString.FindMatchingBrackets("{", "}", y);

                    if (string.IsNullOrEmpty(rawName))
                        break;

                    y += rawName.Length + 2;

                    int tmp = -1, z = -1;

                    if (y == Y) {
                        z = This.Length;
                    }
                    else {
                        if (Wild[y] == '\\')
                            y++;

                        rawValue = Wild.FirstPossible(y, WildChars);

                        if (string.IsNullOrEmpty(rawValue)) {
                            rawValue = Wild.Substring(y, Wild.Length - y);
                            z = This.IndexOf(rawValue, x);
                        }
                        else {
                            tmp = Wild.IndexOf(rawValue, y);
                            if (tmp - y > 0) {
                                rawValue = Wild.Substring(y, tmp - y);
                            }
                            z = This.IndexOf(rawValue, x);
                        }
                    }

                    if (z == -1)
                        return null;

                    y = Wild.IndexOf(rawValue, y);

                    rawValue = Data.Substring(x, z - x);
                    Value[rawName] = rawValue;

                    x = z;
                }
                else if (Wild[y] == '(' && This[x] == '(') {
                    var SubThis = Data.FindMatchingBrackets("(", ")", x);
                    var SubWild = MatchString.FindMatchingBrackets("(", ")", y);

                    if (SubThis.Match(SubWild, IgnoreCase, Value, 0, 0) == null)
                        return null;

                    x += SubThis.Length + 2;
                    y += SubWild.Length + 2;
                }
                else if (Wild[y] == This[x] || Wild[y] == '?') {
                    x++;
                    y++;
                }
                else if (Wild[y] == '*') {
                    y++;

                    if (y == Y) {
                        break;
                    }
                    else if (y + 1 == Y) {
                        if (This[X - 1] == Wild[y]) {
                            break;
                        }
                        return null;
                    }
                    else if (Wild[y] == '\\') {
                        y++;
                    }

                    var rawValue = string.Empty;
                    int tmp = -1, z = -1;

                    rawValue = Wild.FirstPossible(y, WildChars);

                    if (string.IsNullOrEmpty(rawValue)) {
                        rawValue = Wild.Substring(y, Wild.Length - y);
                        z = This.IndexOf(rawValue, x);
                    }
                    else {
                        tmp = Wild.IndexOf(rawValue, y);
                        if (tmp - y > 0) {
                            rawValue = Wild.Substring(y, tmp - y);
                        }
                        z = This.IndexOf(rawValue, x);
                    }

                    if (z == -1)
                        return null;

                    y = Wild.IndexOf(rawValue, y);
                    x = z;
                }
                else if (Wild[y] == '^') {
                    while (x < X && char.IsWhiteSpace(This[x])) {
                        x++;
                    }
                    if (Wild[y + 1] == '\\') {
                        y++;
                    }
                    if (This[x] != Wild[y + 1]) {
                        return null;
                    }
                    y++;
                }
                else {
                    return null;
                }
            }

            return Value;
        }

        public static string[] ParseCParams(this String This, String Open = "(", String Close = ")") {
            int X;
            List<string> Output = new List<string>();

            for (X = 0; X < This.Length; X++) {
                while (X < This.Length && char.IsWhiteSpace(This[X]))
                    X++;

                char Next = '\x00';
                StringBuilder Arg = new StringBuilder();

                while (X < This.Length && (Next = This[X]) != ',') {
                    if (This.Compare(Open, X)) {
                        string Sub = This.FindMatchingBrackets(Open, Close, X, true);

                        Arg.Append(Sub);
                        X += Sub.Length;
                    }
                    else if (This.Compare("\"", X)) {
                        string Sub = This.FindMatchingBrackets("\"", "\"", X, true);

                        Arg.Append(Sub);
                        X += Sub.Length;
                    }
                    else if (This.Compare("'", X)) {
                        string Sub = This.FindMatchingBrackets("'", "'", X, true);

                        Arg.Append(Sub);
                        X += Sub.Length;
                    }
                    else if (This.Compare("(", X)) {
                        string Sub = This.FindMatchingBrackets("(", ")", X, true);

                        Arg.Append(Sub);
                        X += Sub.Length;
                    }
                    else if (This.Compare("{", X)) {
                        string Sub = This.FindMatchingBrackets("{", "}", X, true);

                        Arg.Append(Sub);
                        X += Sub.Length;
                    }
                    else {
                        Arg.Append(Next);
                        X++;
                    }
                }

                Output.Add(Arg.ToString());
            }

            return Output.ToArray();
        }

        public static string[] Split(this String This, String Seperator) {
            List<string> Output = new List<string>();

            if (This.IndexOf(Seperator) == -1) {
                Output.Add(This);
            }
            else {
                int Index, X;
                for (Index = 0; Index < This.Length; Index += Seperator.Length) {
                    if ((X = This.IndexOf(Seperator, Index)) == -1) {
                        X = This.Length;
                    }
                    else if (This.IndexOf('\\', X - 1) == 0) {
                        X++;
                    }

                    Output.Add(This.Substring(Index, X - Index));
                    Index = X;
                }
            }

            return Output.ToArray();
        }
    }
}
