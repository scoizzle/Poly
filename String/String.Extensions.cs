using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;

using Poly;
using Poly.Data;

namespace System {
    public static class StringExtensions {
        public static int CountOf(this String This, String ToFind) {
            int Count = 0;

            for (int Index = 0; Index != -1; Index = Find(This, ToFind, Index))
                Count++;

            return Count;
        }

        public static bool Compare(this String This, char Possible, int Index) {
            if (Index < 0 || Index >= This.Length)
                return false;

            return This[Index] == Possible;
        }

        public static bool Compare(this String This, String Possible, int Index) {
            if (Index == -1 || (This.Length - Index) < Possible.Length)
                return false;

            return Compare(This, Index, Possible, 0, Possible.Length);
        }

        public static bool Compare(this String This, char Possible, int Index, bool IgnoreCase) {
            if (Index < 0 || Index >= This.Length)
                return false;

            if (IgnoreCase) {
                return char.ToLower(This[Index]) == char.ToLower(Possible);
            }

            return This[Index] == Possible;
        }

        public static bool Compare(this String This, String Possible, int Index, bool IgnoreCase) {
            return Compare(This, Index, Possible, 0, Possible.Length, IgnoreCase);
        }

        public static bool Compare(this String This, int Index, String ContainsSub, int ContainsIndex, int Length) {
            if (This == null || ContainsSub == null)
                return false;

            if ((This.Length - Index) < Length)
                return false;

            if ((ContainsSub.Length - ContainsIndex) < Length)
                return false;

            for (int Offset = 0; Offset < Length; Offset++) {
                if (This[Index + Offset] != ContainsSub[ContainsIndex + Offset])
                    return false;
            }

            return true;
        }

        public static bool Compare(this String This, int Index, String ContainsSub, int ContainsIndex, int Length, bool IgnoreCase) {
            if (IgnoreCase) {
                if (This == null || ContainsSub == null)
                    return false;

                if ((This.Length - Index) < Length)
                    return false;

                if ((ContainsSub.Length - ContainsIndex) < Length)
                    return false;

                for (int Offset = 0; Offset < Length; Offset++) {
                    if (!This[Index + Offset].CompareWithoutCase(ContainsSub[ContainsIndex + Offset]))
                        return false;
                }
            }
            else {
                return Compare(This, Index, ContainsSub, ContainsIndex, Length);
            }

            return true;
        }

        public static bool FindMatchingBrackets(this String This, char Open, char Close, ref int Index, ref int End, bool IncludeBrackets = false) {
            int X, Y, Z = 1;

            X = This.Find(Open, Index);

            for (Y = X + 1; Y < This.Length; Y++) {
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
                Y ++;
            }
            else {
                X ++;
            }

            Index = X;
            End = Y;
            return true;
        }

        public static bool FindMatchingBrackets(this String This, String Open, String Close, ref int Index, ref int End) {
            int X, Y, Z = 1;

            X = This.Find(Open, Index);

            if (X == -1)
                return false;

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

            if (Y == -1)
                return false;

            Index = X + Open.Length;
            End = Y;
            return true;
        }

        public static bool FindMatchingBrackets(this String This, String Open, String Close, ref int Index, ref int End, bool IncludeBrackets) {
            int X, Y, Z = 1;

            X = This.Find(Open, Index);

            if (X == -1)
                return false;

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

            if (Y == -1)
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

        public static int Find(this String This, char C) {
            if (This == null)
                return -1;

            for (int i = 0; i < This.Length; i++) {
                if (This[i] == '\\')
                    i++;
                else 
                if (This[i] == C)
                    return i;
            }

            return -1;
        }

        public static int Find(this String This, char C, int Index) {
            if (This == null || Index < 0 || Index > This.Length)
                return -1;

            for (int i = Index; i < This.Length; i++) {
                var S = This[i];

                if (S == '\\')
                    i++;
                else
                if (S == C)
                    return i;
            }

            return -1;
        }

        public static int Find(this String This, char C, int Index, int Last) {
            if (This == null || Index < 0 || Last < Index || Index > This.Length)
                return -1;

            for (int i = Index; i < Last; i++) {
                if (This[i] == C)
                    return i;
                else
                if (This[i] == '\\')
                    i++;
            }

            return -1;
        }

        public static int Find(this String This, string C) {
            if (This == null || C == null)
                return -1;

            int Index = Find(This, C[0]);

            while (Index > -1 && Index <= This.Length - C.Length) {
                if (Compare(This, Index + 1, C, 1, C.Length - 1, false))
                    return Index;

                Index = Find(This, C[0], Index + 1);
            }

            return -1;
        }

        public static int Find(this String This, string C, int Index) {
            if (This == null || C == null || Index < 0 || Index >= This.Length)
                return -1;

            Index = Find(This, C[0], Index);

            while (Index > -1 && Index <= This.Length - C.Length) {
                if (Compare(This, Index + 1, C, 1, C.Length - 1, false))
                    return Index;

                Index = Find(This, C[0], Index + 1);
            }

            return -1;
        }

        public static int Find(this String This, string C, int Index, int Last) {
            if (This == null || C == null || Index < 0 || Index >= This.Length || Last > This.Length)
                return -1;

            Index = Find(This, C[0], Index);

            while (Index > -1 && Index <= Last - C.Length) {
                if (Compare(This, Index + 1, C, 1, C.Length - 1, false))
                    return Index;

                Index = Find(This, C[0], Index + 1);
            }

            return -1;
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
        
        public static int FirstPossibleIndex(this String This, int Start, params string[] Possible) {
            if (string.IsNullOrEmpty(This) || Start < 0)
                return -1;

            for (; Start < This.Length; Start++) {
                for (int i = 0; i < Possible.Length; i++) {
                    if (This[Start] == Possible[i][0]) {
                        if (Compare(This, Start, Possible[i], 0, Possible[i].Length, false)) {
                            return Start;
                        }
                    }
                }
            }

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

                if (This.Compare(Index, ContainsSub, ContainsIndex, ContainsLenght, IgnoreCase)) {
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

        public static string Substring(this String This, int Index) {
            return This.Substring(Index, This.Length - Index);
        }

        public static string Substring(this String This, int Index, int Length) {
            return This.Substring(Index, Length);
        }

        public static string Substring(this String This, String Beginning) {
            int Start = Find(This, Beginning);

            if (Start == -1)
                return "";
            else
                Start++;

            return This.Substring(Start, This.Length - Start);
        }

        public static string Substring(this String This, String Beginning, String End) {
            int Start = string.IsNullOrEmpty(Beginning) ? 0 : Find(This, Beginning);

            if (Start == -1)
                return "";
            else
                Start += Beginning.Length;

            int Stop = string.IsNullOrEmpty(End) ? This.Length : Find(This, End, Start);

            if (Stop == -1)
                return "";

            return This.Substring(Start, Stop - Start);
        }

        public static string Substring(this String This, String Beginning, String End, int Index) {
            int Start = Find(This, Beginning, Index);

            if (Start == -1)
                return "";
            else
                Start += Beginning.Length;

            int Stop = Find(This, End, Start);

            if (Stop == -1)
                return "";

            return This.Substring(Start, Stop - Start);
        }

        public static string Substring(this String This, String Beginning, String End, int Index, bool IncludeStartStop) {
            int Start = Find(This, Beginning, Index);

            if (Start == -1)
                return "";

            int Stop = Find(This, End, Start);

            if (Stop == -1)
                return "";

            if (IncludeStartStop)
                Stop += End.Length;
            else
                Start += Beginning.Length;

            return This.Substring(Start, Stop - Start);
        }

        public static string FindMatchingBrackets(this String This, String Open, String Close, int Index = 0, bool includeBrackets = false) {
            int Offset = 0;

            if (FindMatchingBrackets(This, Open, Close, ref Index, ref Offset, includeBrackets)) {
                return Substring(This, Index, Offset - Index);
            }

            return "";
        }

        public static string FindMatchingBrackets(this String This, char Open, char Close, int Index = 0, bool includeBrackets = false) {
            int Offset = 0;

            if (FindMatchingBrackets(This, Open, Close, ref Index, ref Offset, includeBrackets)) {
                return Substring(This, Index, Offset - Index);
            }

            return "";
        }
        
        public static char[] CParamsTokens = new char[] {
            '"', '\'', '{', '[', '(', 
            '"', '\'', '}', ']', ')'
        };

        public static string[] ParseCParams(this String This) {
            if (string.IsNullOrEmpty(This))
                return new string[0];

            int x, z;
            List<string> Output = new List<string>();

            for (x = 0; x < This.Length; x++) {
                while (x < This.Length && char.IsWhiteSpace(This[x]))
                    x++;

                char Next;
                StringBuilder Arg = new StringBuilder();

                while (x < This.Length && (Next = This[x]) != ',') {
                    for (z = 0; z < 5; z++) {
                        if (This[x] == CParamsTokens[z]) {
                            var Sub = FindMatchingBrackets(
                                This,
                                CParamsTokens[z],
                                CParamsTokens[z + 5],
                                x,
                                true
                            );

                            if (Sub != null) {
                                Arg.Append(Sub);
                                x += Sub.Length;
                            }
                            else {
                                Arg.Append(Next);
                                x++;
                            }
                            break;
                        }
                    }
                    
                    if (z == 5) {
                        Arg.Append(Next);
                        x++;
                    }
                }

                Output.Add(Arg.ToString());
            }

            return Output.ToArray();
        }

        public static IEnumerable<string> Split(this String This, char Seperator) {
            int Start = 0,
                Stop = This.Length,
                Index = Find(This, Seperator);

            if (Index == -1) {
                yield return This;
            }
            else while (Index < Stop && Index != -1) {
                yield return Substring(This, Start, Index - Start);

                Index = Find(This, Seperator, Index + 1);
            }
        }

        public static IEnumerable<string> Split(this String This, char Seperator, int Start) {
            int Stop = This.Length,
                Index = Find(This, Seperator);

            if (Index == -1) {
                yield return This;
            }
            else while (Index < Stop && Index != -1) {
                yield return Substring(This, Start, Index - Start);

                Index = Find(This, Seperator, Index + 1);
            }
        }

        public static IEnumerable<string> Split(this String This, char Seperator, int Start, int Stop) {
            int Index = Find(This, Seperator);

            if (Index == -1) {
                yield return This;
            }
            else while (Index < Stop && Index != -1) {
                yield return Substring(This, Start, Index - Start);

                Index = Find(This, Seperator, Index + 1);
            }
        }

        public static IEnumerable<string> Split(this String This, string Seperator) {
            int Start = 0,
                Stop = This.Length,
                Index = Find(This, Seperator);

            if (Index == -1) {
                yield return This.Descape();
            }
            else do {
                yield return Substring(This, Start, Index - Start).Descape();

                Start = Index + Seperator.Length;
                Index = Find(This, Seperator, Start);

                if (Index == -1) {
                    yield return Substring(This, Start, Stop - Start);
                    break;
                }
            }
            while (Index < Stop);
        }

        public static IEnumerable<string> Split(this String This, string Seperator, int Start) {
            int Stop = This.Length,
                Index = Find(This, Seperator);

            if (Index == -1) {
                yield return This;
            }
            else do {
                    yield return Substring(This, Start, Index - Start);

                    Start = Index + Seperator.Length;
                    Index = Find(This, Seperator, Start);

                    if (Index == -1) {
                        yield return Substring(This, Start, Stop - Start);
                        break;
                    }
                }
                while (Index < Stop);
        }

        public static IEnumerable<string> Split(this String This, string Seperator, int Start, int Stop) {
            int Index = Find(This, Seperator);

            if (Index == -1) {
                yield return This;
            }
            else do {
                    yield return Substring(This, Start, Index - Start);

                    Start = Index + Seperator.Length;
                    Index = Find(This, Seperator, Start);

                    if (Index == -1) {
                        yield return Substring(This, Start, Stop - Start);
                        break;
                    }
                }
                while (Index < Stop);
        }
    }
}