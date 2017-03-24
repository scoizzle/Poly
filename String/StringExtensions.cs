using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;

using Poly;
using Poly.Data;

namespace System {
    public static class StringExtensions {
        public static bool Compare(this string This, char Possible, int Index) {
            if (Index < 0 || Index >= This.Length)
                return false;

            return This[Index] == Possible;
        }

        public static bool Compare(this string This, char Possible, int Index, bool IgnoreCase) {
            if (Index < 0 || Index >= This.Length)
                return false;

            if (IgnoreCase) {
                return char.ToLower(This[Index]) == char.ToLower(Possible);
            }

            return This[Index] == Possible;
        }

        public static bool Compare(this string This, string Possible, int Index) {
            if (This == null || Possible == null || Index == -1 || (This.Length - Index) < Possible.Length)
                return false;

            return string.Compare(This, Index, Possible, 0, Possible.Length, StringComparison.Ordinal) == 0;
        }

        public static bool Compare(this string This, string Possible, int Index, bool IgnoreCase) {
            if (IgnoreCase) {
                return string.Compare(This, Index, Possible, 0, Possible.Length, StringComparison.OrdinalIgnoreCase) == 0;
            }
            else {
                return string.Compare(This, Index, Possible, 0, Possible.Length, StringComparison.Ordinal) == 0;
            }
        }

        public static bool Compare(this string This, int Index, string ContainsSub, int ContainsIndex, int Length) {
            return string.Compare(This, Index, ContainsSub, ContainsIndex, Length, StringComparison.Ordinal) == 0;
        }

        public static bool Compare(this string This, int Index, string ContainsSub, int ContainsIndex, int Length, bool IgnoreCase) {
            if (IgnoreCase) {
                return string.Compare(This, Index, ContainsSub, ContainsIndex, Length, StringComparison.OrdinalIgnoreCase) == 0;
            }
            else {
                return string.Compare(This, Index, ContainsSub, ContainsIndex, Length, StringComparison.Ordinal) == 0;
            }
        }

        public static bool FindMatchingBrackets(this string This, char Open, char Close, ref int Index, ref int End, bool IncludeBrackets = false) {
            Index = This.IndexOf(Open, Index);

            if (Index == -1)
                return false;

            var Count = 1;
            var i = Index;

            for (; i < End; i++) {
                var C = This[i];
                if (C == '\\')
                    continue;

                if (C == Close) {
                    Count--;

                    if (Count == 0)
                        goto found;
                }
                else if (C == Open)
                    Count++;
            }
            return false;

        found:

            if (IncludeBrackets)
                End = i + 1;
            else {
                End = i;
                Index++;
            }
            return true;
        }

        public static bool FindMatchingBrackets(this string This, string Open, string Close, ref int Index, ref int End) {
            return FindMatchingBrackets(This, Open, Close, ref Index, ref End, false);
        }

        public static bool FindMatchingBrackets(this string This, string Open, string Close, ref int Index, ref int End, bool IncludeBrackets) {
            Index = This.IndexOf(Open, Index);

            if (Index == -1)
                return false;

            var Count = 1;
            var i = Index;

            for (; i < End; i++) {
                var C = This[i];
                if (C == '\\')
                    continue;

                if (C == Close[0] && Compare(This, Close, i)) {
                    Count--;

                    if (Count == 0)
                        goto found;
                }
                else if (C == Open[0] && Compare(This, Open, i))
                    Count++;
            }
            return false;

        found:

            if (IncludeBrackets)
                End = i + Close.Length;
            else {
                End = i;
                Index += Open.Length;
            }

            return true;
        }
        
        public static int Find(this string This, string C) {
            return Find(This, C, 0, This.Length);
        }

        public static int Find(this string This, string C, int Index) {
            return Find(This, C, Index, This.Length);
        }

        public static int Find(this string This, string C, int Index, int Last) {
            if (This == null || C == null || Index < 0 || Index >= This.Length || Last > This.Length)
                return -1;

            var Len = C.Length;
            var FirstChar = C[0];
            var LastPossible = Last - C.Length;

            Index = This.IndexOf(FirstChar, Index, Last - Index);

            while (Index < LastPossible && Index != -1) {
                if (string.Compare(This, Index, C, 0, Len, StringComparison.Ordinal) == 0)
                    break;

                Index = This.IndexOf(FirstChar, ++Index, Last - Index);
            }

            return Index;
        }

        public static int FindLast(this string This, char C, int Index = 0) {
            if (string.IsNullOrEmpty(This))
                return -1;

            for (int i = This.Length - 1; i >= Index; i--) {
                if (This[i] == C)
                    return i;
            }

            return -1;
        }

        public static int FindLast(this string This, string C, int Index = 0) {
            if (string.IsNullOrEmpty(This))
                return -1;
            
            int i = This.LastIndexOf(C, Index);

            while (i > 0 && i < This.Length && This[i - 1] == '\\')
                i = This.LastIndexOf(C, i + 1);

            return i;
        }

        public static bool Contains(this string This, char C, int Index = 0) {
            if (string.IsNullOrEmpty(This))
                return false;

            return This.IndexOf(C, Index) != -1;
        }

        public static bool Contains(this string This, char C, int Index, int LastIndex) {
            if (string.IsNullOrEmpty(This))
                return false;

            return This.IndexOf(C, Index) < LastIndex;
        }

        public static bool Contains(this string This, string C, int Index = 0) {
            if (string.IsNullOrEmpty(This))
                return false;

            return This.Find(C, Index) != -1;
        }

        public static int FirstPossibleIndex(this string This, int Start = 0, params char[] Possible) {
            if (string.IsNullOrEmpty(This) || Start < 0)
                return -1;

            for (int X = Start; X < This.Length; X++)
                if (Possible.Contains(This[X]))
                    return X;

            return -1;
        }
        
        public static int FirstPossibleIndex(this string This, int Start, params string[] Possible) {
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

        public static int FindSubstring(this string This, string ContainsSub, int Index, int ContainsIndex, int ContainsLenght) {
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

        public static int FindSubstring(this string This, string ContainsSub, int Index, int ContainsIndex, int ContainsLenght, bool IgnoreCase) {
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

        public static char FirstPossible(this string This, int Start = 0, params char[] Possible) {
            if (string.IsNullOrEmpty(This) || Start < 0)
                return char.MinValue;

            for (int X = Start; X < This.Length; X++)
                if (Possible.Contains(This[X]))
                    return This[X];

            return char.MinValue;
        }

        public static string Format(this string This, params object[] Args) {
            return string.Format(This, Args);
        }

        public static string GetFileExtension(this string This) {
            var lastPeriod = This.LastIndexOf('.');

            if (lastPeriod == -1)
                return string.Empty;

            return This.Substring(lastPeriod);
        }

        public static string Substring(this string This, string Beginning) {
            int Start = Find(This, Beginning);

            if (Start == -1)
                return "";
            else
                Start += Beginning.Length;

            return This.Substring(Start, This.Length - Start);
        }

        public static string Substring(this string This, string Beginning, string End) {
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

        public static string Substring(this string This, string Beginning, string End, int Index) {
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

        public static string Substring(this string This, string Beginning, string End, int Index, bool IncludeStartStop) {
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

        public static string FindMatchingBrackets(this string This, string Open, string Close, int Index = 0, bool includeBrackets = false) {
            if (This == null)
                return null;

            int Offset = This.Length;

            if (FindMatchingBrackets(This, Open, Close, ref Index, ref Offset, includeBrackets)) {
                return This.Substring(Index, Offset - Index);
            }

            return "";
        }

        public static string FindMatchingBrackets(this string This, char Open, char Close, int Index = 0, bool includeBrackets = false) {
            int Offset = 0;

            if (FindMatchingBrackets(This, Open, Close, ref Index, ref Offset, includeBrackets)) {
                return This.Substring(Index, Offset - Index);
            }

            return "";
        }
        
        public static char[] CParamsTokens = new char[] {
            '"', '\'', '{', '[', '(', 
            '"', '\'', '}', ']', ')'
        };

        public static string[] ParseCParams(this string This) {
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

		public static void SplitAndHandle(this string This, string Sep, Action<string, string> Handler) {
			var i = This.Find(Sep);

			if (i == -1)
				return;

			Handler(This.Substring(0, i), This.Substring(i + Sep.Length));
		}

        public static bool Extract(this string This, string seperator, out string _1, out string _2) {
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