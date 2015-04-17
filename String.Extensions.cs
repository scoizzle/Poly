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

            return String.Compare(This, Index, Possible, 0, Possible.Length, false) == 0;
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
            if (Index == -1 || (This.Length - Index) < Possible.Length)
                return false;

            return String.Compare(This, Index, Possible, 0, Possible.Length, IgnoreCase) == 0;
        }

        public static bool Compare(this String This, String ContainsSub, int Index, int ContainsIndex, int ContainsLength) {
            for (int Offset = 0; (Offset + Index) < This.Length && (ContainsIndex + Offset + ContainsLength) <= ContainsSub.Length; Offset++) {
                for (int Cmp = Offset; Cmp < ContainsLength; Cmp++) {
                    if (ContainsSub[ContainsIndex + Cmp] == '\\') continue;

                    if (This[Index + Cmp] == ContainsSub[ContainsIndex + Cmp]) {
                        if ((ContainsLength - Cmp) == 1)
                            return true;
                    }
                }
            }

            return false;
        }

        public static bool Compare(this String This, String ContainsSub, int Index, int ContainsIndex, int ContainsLength, bool IgnoreCase) {
            for (int Offset = 0; (Offset + Index) < This.Length && (ContainsIndex + Offset + ContainsLength) <= ContainsSub.Length; Offset++) {
                for (int Cmp = Offset; Cmp < ContainsLength; Cmp++) {
                    if (ContainsSub[ContainsIndex + Cmp] == '\\')
                        continue;

                    if (!Compare(This, ContainsSub[ContainsIndex + Cmp], Index + Cmp, IgnoreCase))
                        break;

                    if ((ContainsLength - Cmp) == 1)
                        return true;
                }
            }

            return false;
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
            if (string.IsNullOrEmpty(This))
                return -1;

            int i = This.IndexOf(C);

            while (i > 0 && i < This.Length && This[i - 1] == '\\')
                i = This.IndexOf(C, i + 1);
            
            return i;
        }

        public static int Find(this String This, char C, int Index) {
            if (string.IsNullOrEmpty(This) || Index < 0 || Index > This.Length)
                return -1;

            int i = This.IndexOf(C, Index);

            while (i > 0 && i < This.Length && This[i - 1] == '\\')
                i = This.IndexOf(C, i + 1);

            return i;
        }

        public static int Find(this String This, char C, int Index, int Last) {
            if (string.IsNullOrEmpty(This) || Index < 0 || Last < Index || Index > This.Length)
                return -1;

            int i = This.IndexOf(C, Index);

            while (i > 0 && i < This.Length && i < Last && This[i - 1] == '\\')
                i = This.IndexOf(C, i + 1);

            if (i > Last)
                return -1;

            return i;
        }

        public static int Find(this String This, string C) {
            if (string.IsNullOrEmpty(This))
                return -1;

            int i = This.IndexOf(C);

            while (i > 0 && i < This.Length && This[i - 1] == '\\')
                i = This.IndexOf(C, i + 1);

            return i;
        }

        public static int Find(this String This, string C, int Index) {
            if (string.IsNullOrEmpty(This) || Index < 0 || Index > This.Length)
                return -1;

            int i = This.IndexOf(C, Index);

            while (i > 0 && i < This.Length && This[i - 1] == '\\')
                i = This.IndexOf(C, i + 1);

            return i;
        }

        public static int Find(this String This, string C, int Index, int Last) {
            if (string.IsNullOrEmpty(This) || Index < 0 || Last < Index || Index > This.Length)
                return -1;

            int i = This.IndexOf(C, Index);

            while (i > 0 && i < This.Length && i < Last && This[i - 1] == '\\')
                i = This.IndexOf(C, i + 1);

            if (i > Last)
                return -1;

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