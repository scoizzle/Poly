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

        public static bool Compare(this String This, char Possible, int Index, bool IgnoreCase) {
            if (Index < 0 || Index >= This.Length)
                return false;

            if (IgnoreCase) {
                return char.ToLower(This[Index]) == char.ToLower(Possible);
            }

            return This[Index] == Possible;
        }

        public static bool Compare(this String This, String Possible, int Index) {
            if (Index == -1 || (This.Length - Index) < Possible.Length)
                return false;

            return Compare(This, Index, Possible, 0, Possible.Length);
        }

        public static bool Compare(this String This, String Possible, int Index, bool IgnoreCase) {
            return Compare(This, Index, Possible, 0, Possible.Length, IgnoreCase);
        }

        public static bool Compare(this String This, int Index, String ContainsSub, int ContainsIndex, int Length) {
            return String.Compare(This, Index, ContainsSub, ContainsIndex, Length, StringComparison.Ordinal) == 0;
        }

        public static bool Compare(this String This, int Index, String ContainsSub, int ContainsIndex, int Length, bool IgnoreCase) {
            if (IgnoreCase) {
                return String.Compare(This, Index, ContainsSub, ContainsIndex, Length, StringComparison.OrdinalIgnoreCase) == 0;
            }
            else {
                return Compare(This, Index, ContainsSub, ContainsIndex, Length);
            }
        }

        public static bool FindMatchingBrackets(this String This, char Open, char Close, ref int Index, ref int End, bool IncludeBrackets = false) {
            int X, Y, Z = 1;

            X = This.IndexOf(Open, Index);

            if (X > Index)
            while (X > 0 && This[X - 1] == '\\')
                X = This.IndexOf(Open, X);

            if (X == -1)
                return false;
            
            for (Y = X + 1; Y < End; Y++) {
                if (This[Y] == '\\') {
                    Y++;
                    continue;
                }

                if (This[Y] == Close) {
                    Z--;
                }
                else if (This[Y] == Open) {
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
            return FindMatchingBrackets(This, Open, Close, ref Index, ref End, false);
        }

        public static bool FindMatchingBrackets(this String This, String Open, String Close, ref int Index, ref int End, bool IncludeBrackets) {
            int X, Y, Z = 1;

            X = Find(This, Open, Index, End);

            if (X > Index)
                while (X > 0 && This[X - 1] == '\\')
                    X = Find(This, Open, X, End);

            if (X == -1)
                return false;

            for (Y = X + Open.Length; Y < End; Y++) {
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
        
        public static int Find(this String This, string C) {
            if (This == null || C == null)
                return -1;

            return Find(This, C, 0, This.Length);
        }

        public static int Find(this String This, string C, int Index) {
            if (This == null || C == null)
                return -1;

            return Find(This, C, Index, This.Length);
        }

        public static int Find(this String This, string C, int Index, int Last) {
            if (This == null || C == null || Index < 0 || Index >= This.Length || Last > This.Length)
                return -1;

            var First = C[0];
            var Len = C.Length;

            Last -= Len - 1;

            for (; Index < Last; Index++) {
                if (This[Index] == First) {
                    if (string.Compare(This, Index, C, 0, Len, StringComparison.Ordinal) == 0)
                        return Index;
                }
            }

            return -1;
        }

        public static int FindLast(this String This, char C, int Index = 0) {
            if (string.IsNullOrEmpty(This))
                return -1;

            for (int i = This.Length - 1; i >= Index; i--) {
                if (This[i] == C)
                    return i;
            }

            return -1;
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

            return This.Find(C, Index) != -1;
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

        public static int FindSubstring(this String This, String ContainsSub, int Index, int ContainsIndex, int ContainsLenght) {
            if (ContainsLenght == 0)
                return -1;

            while (true) {
                var C = ContainsSub[ContainsIndex];

                Index = This.IndexOf(C, Index);

                if (Index == -1)
                    return -1;

                if (ContainsLenght == 1)
                    return Index;

                if (string.Compare(This, Index, ContainsSub, ContainsIndex, ContainsLenght, StringComparison.Ordinal) == 0) {
                    return Index;
                }

                Index++;
            }
        }

        public static int FindSubstring(this String This, String ContainsSub, int Index, int ContainsIndex, int ContainsLenght, bool IgnoreCase) {
            if (IgnoreCase) {
                if (ContainsLenght == 0)
                    return -1;

                while (true) {
                    var C = ContainsSub[ContainsIndex];

                    Index = This.IndexOf(C, Index);

                    if (Index == -1)
                        return -1;

                    if (ContainsLenght == 1)
                        return Index;

                    if (string.Compare(This, Index, ContainsSub, ContainsIndex, ContainsLenght, StringComparison.OrdinalIgnoreCase) == 0) {
                        return Index;
                    }

                    Index++;
                }
            }
            else {
                return FindSubstring(This, ContainsSub, Index, ContainsIndex, ContainsLenght);
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

        public static string Format(this String This, params object[] Args) {
            return string.Format(This, Args);
        }

        public static string GetFileExtension(this String This) {
            var lastPeriod = This.LastIndexOf('.');

            if (lastPeriod == -1)
                return string.Empty;

            return This.Substring(lastPeriod);
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
                Start += Beginning.Length;

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
            if (This == null)
                return null;

            int Offset = This.Length;

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

		public static void SplitAndHandle(this String This, string Sep, Action<string, string> Handler) {
			var i = This.Find(Sep);

			if (i == -1)
				return;

			Handler(This.Substring(0, i), This.Substring(i + Sep.Length));
		}

        public static bool Extract(this String This, string seperator, out string _1, out string _2) {
            var i = This.Find(seperator);

            if (i == -1) {
                _1 = null;
                _2 = null;
                return false;
            }

            _1 = This.Substring(0, i);
            _2 = This.Substring(i + seperator.Length);
            return true;
        }
    }
}