using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;

using Poly;
using Poly.Data;

namespace System {
    public static class StringExtensions {
        public static int CountOf(this String This, String ToFind) {
            int Count = 0, X = 0;

            while ((X = This.Find(ToFind, X)) != -1) {
                if ((X > 0) && (This[X - 1] == '\\') && ToFind != "\\")
                    continue;

                Count++;
                X += ToFind.Length;

            }

            return Count;
        }

        public static bool Compare(this String This, char Possible, int Index, bool IgnoreCase = false) {
            if (Index < 0 || Index >= This.Length)
                return false;

            if (IgnoreCase) {
                return char.ToLower(This[Index]) == char.ToLower(Possible);
            }

            return This[Index] == Possible;
        }

        public static bool Compare(this String This, String Possible, int Index, bool IgnoreCase = false) {
            if (Index == -1 || (This.Length - Index) < Possible.Length)
                return false;

            return String.Compare(This, Index, Possible, 0, Possible.Length, IgnoreCase) == 0;
        }

        public static bool Compare(this String This, String ContainsSub, int Index, int ContainsIndex, int ContainsLength, bool IgnoreCase = false) {
            return String.Compare(This, Index, ContainsSub, ContainsIndex, ContainsLength, IgnoreCase) == 0;
        }

        public static bool FindMatchingBrackets(this String This, String Open, String Close, ref int Index, ref int End, bool IncludeBrackets = false) {
            int X, Y, Z = 1;

            X = This.Find(Open, Index);

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
            if (string.IsNullOrEmpty(This) || Index < 0 || Last < Index)
                return -1;

            int i = This.IndexOf(C, Index);

            while (i > 0 && i < This.Length && i < Last && This[i - 1] == '\\')
                i = This.IndexOf(C, i + 1);

            if (i > Last)
                return -1;

            return i;
        }

        public static int Find(this String This, string C, int Index = 0, int Last = int.MaxValue) {
            if (string.IsNullOrEmpty(This) || string.IsNullOrEmpty(C) || Index < 0 || Last < Index)
                return -1;

            int i = This.IndexOf(C, Index);

            while (i > 0 && i < This.Length && This[i - 1] == '\\')
                i = This.IndexOf(C, i + 1);

            return i;
        }

        public static int FindLast(this String This, char C, int Index = 0) {
            if (string.IsNullOrEmpty(This))
                return -1;

            int i = This.LastIndexOf(C, Index);

            while (i > 0 && i < This.Length && This[i - 1] == '\\')
                i = This.LastIndexOf(C, i + 1);

            return i;
        }

        public static int FindLast(this String This, string C, int Index = 0) {
            if (string.IsNullOrEmpty(This))
                return -1;
            
            int i = This.LastIndexOf(C, Index);

            while (i > 0 && i < This.Length && This[i - 1] == '\\')
                i = This.LastIndexOf(C, i + 1);

            return i;
        }

        public static bool Contains(this String This, char C, int Index = 0) {
            if (string.IsNullOrEmpty(This))
                return false;

            return This.IndexOf(C, Index) != -1;
        }

        public static bool Contains(this String This, char C, int Index, int LastIndex) {
            if (string.IsNullOrEmpty(This))
                return false;

            return This.IndexOf(C, Index) < LastIndex;
        }

        public static bool Contains(this String This, string C, int Index = 0) {
            if (string.IsNullOrEmpty(This))
                return false;

            return This.IndexOf(C, Index) != -1;
        }

        public static int FirstPossibleIndex(this String This, int Start = 0, params char[] Possible) {
            if (string.IsNullOrEmpty(This) || Start < 0)
                return -1;

            for (int X = Start; X < This.Length; X++)
                if (Possible.Contains(This[X]))
                    return X;

            return -1;
        }

        public static int FindSubstring(this String This, String ContainsSub, int Index, int ContainsIndex, int ContainsLenght, bool IgnoreCase = false) {
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

        public static char FirstPossible(this String This, int Start = 0, params char[] Possible) {
            if (string.IsNullOrEmpty(This) || Start < 0)
                return char.MinValue;

            for (int X = Start; X < This.Length; X++)
                if (Possible.Contains(This[X]))
                    return This[X];

            return char.MinValue;
        }

        public static string Template(this String This, params object[] Args) {
            return string.Format(This, Args);
        }

        public static string Substring(this String This, int Start, int Length = int.MaxValue) {
            if (Length == int.MaxValue)
                Length = This.Length - Start;

            if (Start > -1 && (Start + Length) <= This.Length && Length > -1) {
                StringBuilder Output = new StringBuilder(Length);

                Output.Append(This, Start, Length);

                return Output.ToString();
            }
            return null;
        }

        public static string Substring(this String This, String Start, String Stop) {
            return Substring(This, Start, Stop, 0, false, false);
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

            return This.Substring(X, Y - X);
        }

        public static string FindMatchingBrackets(this String This, String Open, String Close, int Index = 0, bool includeBrackets = false) {
            int Offset = 0;

            if (FindMatchingBrackets(This, Open, Close, ref Index, ref Offset, includeBrackets)) {
                return Substring(This, Index, Offset - Index);
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

        public static IEnumerable<string> Split(this String This, char Seperator, int Start = 0, int Stop = int.MaxValue) {
            Stop = This.Length;
            Start = This.IndexOf(Seperator, Start);

            if (Start == -1) {
                yield return This.Substring(0, Stop);
            }
            else {
                int Index = 0;

                do {
                    yield return This.Substring(Index, Start);

                    Index = Start;
                    Start = This.IndexOf(Seperator, Start + 1);
                }
                while (Start > 0 && Start < Stop);
            }
        }

        public static string[] Split(this String This, String Seperator, int Start = 0, int Stop = int.MaxValue) {
            int Close = This.Find(Seperator, Start, Stop);

            if (Stop == int.MaxValue)
                Stop = This.Length;

            if (Close == -1)
                return new string[] { This.Substring(Start, Stop - Start) };

            int Open = Close + Seperator.Length;
            List<string> List = new List<string>() {
                This.Substring(Start, Close - Start)
            };

            for (; Open > Start && Close < Stop; ) {
                Close = This.Find(Seperator, Open, Stop);

                if (Close == -1) {
                    List.Add(This.Substring(Open, Stop - Open));
                    break;
                }
                else {
                    List.Add(This.Substring(Open, Close - Open));
                    Open = Close + Seperator.Length;
                }

            }

            return List.ToArray();
        }
    }
}