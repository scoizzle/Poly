﻿using System;
using System.Collections.Generic;
using System.Text;

using Poly;
using Poly.Data;

namespace System {
    public static class StringExtensions {
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

        public static bool Compare(this String This, String ContainsSub, int Index, int ContainsIndex, int ContainsLength, bool IgnoreCase = false) {
            int X = 0;
            for (; Index + X < This.Length && X < ContainsLength; X++) {
                if (IgnoreCase) {
                    if (char.ToLower(This[Index + X]) != char.ToLower(ContainsSub[ContainsIndex + X])) {
                        return false;
                    }
                }
                else if (This[Index + X] != ContainsSub[ContainsIndex + X])
                    return false;
            }

            return X == ContainsLength;
        }

        public static bool ToBool(this String This) {
            bool Out = false;

            if (Boolean.TryParse(This, out Out))
                return Out;

            return false;
        }

        public static bool FindMatchingBrackets(this String This, String Open, String Close, int Index, ref int End, bool IncludeBrackets = false) {
            int X, Y, Z = 1;

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
                return false;

            if (IncludeBrackets) {
                Y += Close.Length;
            }
            else {
                X += Open.Length;
            }

            End = Y;
            return true;
        }

        public static int ToInt(this String This) {
            int Out = 0;

            if (Int32.TryParse(This, out Out))
                return Out;

            return 0;
        }

        public static int Find(this String This, char C, int Index = 0, int Last = int.MaxValue) {
            if (Index > -1) {
                for (int i = Index; i < This.Length && i < Last; i++) {
                    if (i > 0 && This[i - 1] == '\\')
                        continue;

                    if (This[i] == C) {
                        return i;
                    }
                }
            }
            return -1;
        }

        public static int Find(this String This, string C, int Index = 0, int Last = int.MaxValue) {
            if (Index > -1) {
                for (int i = 0; i < This.Length && i < Last; i++) {
                    if (Compare(This, C, i, 0, C.Length)) {
                        return i;
                    }
                }
            }
            return -1;
        }

        public static bool Contains(this String This, char C, int Index = 0) {
            if (Index > -1) {
                for (int i = Index; i < This.Length; i++) {
                    if (This[i] == C) {
                        return true;
                    }
                }
            }
            return false;
        }

        public static bool Contains(this String This, string C, int Index = 0) {
            if (Index > -1) {
                for (int i = Index; i < This.Length; i++) {
                    if (Compare(This, C, i, 0, C.Length)) {
                        return true;
                    }
                }
            }
            return false;
        }

        public static int FirstPossibleIndex(this String This, int Start = 0, params String[] Possible) {
            int Location = This.Length;

            if ((Location - Start) <= 1)
                return -1;

            if (Start > 0 && This[Start - 1] == '\\')
                Start++;

            for (int i = 0; i < Possible.Length; i++) {
                int Maybe = This.IndexOf(Possible[i], Start);

                if (Maybe == -1)
                    continue;

                if (Maybe < Location) {
                    Location = Maybe;
                }
            }

            if (Location == This.Length)
                return -1;

            return Location;
        }

        public static int FindSubString(this String This, String ContainsSub, int Index, int ContainsIndex, int ContainsLenght, bool IgnoreCase = false) {
            if (ContainsLenght == 0)
                return -1;

            while (true) {
                var C = ContainsSub[ContainsIndex];

                Index = This.Find(C, Index);

                if (Index == -1)
                    return -1;

                if (ContainsLenght == 1)
                    return Index;

                if (This.Compare(ContainsSub, Index, ContainsIndex, ContainsLenght, IgnoreCase)) {
                    return Index;
                }

                Index++;
            }
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

        public static string SubString(this String This, int Start, int Length) {
            if (Start > -1 && (Start + Length) <= This.Length && Length > -1) {
                var Array = new char[Length];

                try {
                    for (int Index = Start; Index < Start + Length; Index++) {
                        Array[Index - Start] = This[Index];
                    }

                    return new string(Array);
                }
                catch { }
                finally {
                    Array = null;
                }
            }
            return null;
        }

        public static string Substring(this String This, String Start, String Stop = "", int Index = 0, bool includeStartStop = false, bool LastStop = false) {
            int X = -1, Y = -1;

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

            return This.SubString(X, Y - X);
        }

        public static string FindMatchingBrackets(this String This, String Open, String Close, int Index = 0, bool includeBrackets = false) {
            int X, Y, Z = 1;

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

            return This.SubString(X, Y - X);
        }

        public static string ToString(this String This, params object[] Arguments) {
            if (string.IsNullOrEmpty(This))
                return "";
            return string.Format(This, Arguments);
        }

        public static jsObject ToJsObject(this String This) {
            return (jsObject)(This);
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
                    else if (This.Compare("[", X)) {
                        string Sub = This.FindMatchingBrackets("[", "]", X, true);

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
                    else if (This.IndexOf('\\', X - 1) < X) {
                        X++;
                    }

                    Output.Add(This.SubString(Index, X - Index));
                    Index = X;
                }
            }

            return Output.ToArray();
        }
    }
}