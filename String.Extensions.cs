using System;
using System.Collections.Generic;
using System.Text;

using Poly;
using Poly.Data;

namespace System {
    public static class StringExtensions {
        public static int CountOf(this String This, String ToFind) {
            int Count = 0, X = 0;

            while ((X = This.IndexOf(ToFind, X)) != -1) {
                if ((X > 0) && (This[X - 1] == '\\'))
                    continue;

                Count++;
                X += ToFind.Length;

            }

            return Count;
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

        public static bool FindMatchingBrackets(this String This, String Open, String Close, ref int Index, ref int End, bool IncludeBrackets = false) {
            int X, Y, Z = 1;

            X = This.IndexOf(Open, Index);

            for (Y = X + Open.Length; Y < This.Length; Y++) {
                if (This[Y] == '\\') {
                    Y++;
                    continue;
                }

                if (This.Compare(Close, Y)) {
                    Z--;
                }
                else if (This.Compare(Open, Y)) {
                    Z++;
                }

                if (Z == 0)
                    break;
            }

            if (X == -1 || Y == -1)
                return false;

            if (IncludeBrackets) {
                Y += Close.Length;
            }
            else {
                X += Open.Length;
            }

            Index = X;
            End = Y;
            return true;
        }

        public static int Find(this String This, char C, int Index = 0, int Last = int.MaxValue) {
            if (Index > -1) {
                if (Last == int.MaxValue)
                    Last = This.Length;

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
                if (C.Length == 1)
                    return Find(This, C[0], Index, Last);

                if (Last == int.MaxValue)
                    Last = This.Length;

                for (int i = Index; i < This.Length && i < Last; i++) {
                    if (This.Compare(C, i, 0, C.Length)) {
                        return i;
                    }
                }
            }
            return -1;
        }

        public static int FindLast(this String This, char C, int Index = 0) {
            if (Index > -1) {
                int Last = -1;
                for (int i = Index; i < This.Length; i++) {
                    if (i > 0 && This[i - 1] == '\\')
                        continue;

                    if (This[i] == C) {
                        Last = i;
                    }
                }
                return Last;
            }
            return -1;
        }

        public static int FindLast(this String This, string C, int Index = 0) {
            if (Index > -1) {
                if (C.Length == 1)
                    return FindLast(This, C[0], Index);

                int Last = -1;
                for (int i = Index; i < This.Length; i++) {
                    if (i > 0 && This[i - 1] == '\\')
                        continue;

                    if (Compare(This, C, i, 0, C.Length)) {
                        Last = i;
                    }
                }
                return Last;
            }
            return -1;
        }

        public static bool Contains(this String This, char C, int Index = 0) {
            if (string.IsNullOrEmpty(This))
                return false;

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
                int Maybe = This.Find(Possible[i], Start);

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

        public static string FirstPossible(this String This, int Start = 0, params String[] Possible) {
            int Index = int.MaxValue, Old = int.MaxValue;

            if (This != null && This != "") {
                for (int X = 0, Y = int.MaxValue; X < Possible.Length; X++) {
                    if ((Y = This.Find(Possible[X], Start)) != -1) {
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

                for (int Index = 0; Index < Length; Index++) {
                    Array[Index] = This[Index + Start];
                }
                return new string(Array);
            }
            return null;
        }

        public static string Substring(this String This, String Start, String Stop = "", int Index = 0, bool includeStartStop = false, bool LastStop = false) {
            int X = -1, Y = -1;

            if (Start == "") {
                X = Index;
            }
            else {
                X = This.Find(Start, Index);
            }

            if (X == -1) {
                return "";
            }
            else {
                while (X > 0 && This[X - 1] == '\\') {
                    X = This.Find(Start, X + Start.Length);
                }
            }

            if (Stop == "") {
                Y = This.Length;
            }
            else if (LastStop) {
                Y = This.FindLast(Stop);
            }
            else {
                Y = This.Find(Stop, X + Start.Length);
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
            int Offset = 0;

            if (FindMatchingBrackets(This, Open, Close, ref Index, ref Offset, includeBrackets)) {
                return SubString(This, Index, Offset - Index);
            }

            return "";
        }
        
        public static string[] ParseCParams(this String This, String Open = "(", String Close = ")") {
            int X;
            List<string> Output = new List<string>();

            for (X = 0; X < This.Length; X++) {
                while (X < This.Length && char.IsWhiteSpace(This[X]))
                    X++;

                char Next;
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
            int Count = CountOf(This, Seperator);

            if (Count == 0) {
                return new string[] { This };
            }
            else {
                int Open = 0, 
                    Close = This.Find(Seperator);

                string[] List = new string[Count + 1];

                for (int Index = 0; Index < List.Length && Open != -1 && Close != -1; Index ++) {
                    List[Index] = This.SubString(Open, Close - Open);

                    Open = Close + Seperator.Length;
                    Close = This.Find(Seperator, Open);

                    if (Close == -1) {
                        Close = This.Length;
                    }
                }
                return List;
            }
        }
    }
}