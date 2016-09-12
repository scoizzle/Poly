using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Poly {
    using Data;

    public class StringIterator {
        public string String;
        public int Index, Length;

        public StringIterator(string String) : this(String, String.Length) {
        }

        public StringIterator(string String, int Length) : this(String, 0, Length) {
        }

        public StringIterator(string String, int Index, int Length) {
            this.String = String;
            this.Index = Index;
            this.Length = Length;
        }

        public char this[int Index] {
            get {
                return String[Index];
            }
        }

        public char Current {
            get {
                if (IsDone())
                    return char.MinValue;

                return this[Index];
            }
        }

        public int Find(char C) {
            return String.IndexOf(C, Index, Length - Index);
        }

        public int Find(string Str) {
			var Res = String.Find(Str, Index, Length);

            if (Res > Length)
                return -1;

            return Res;
        }

        public int Find(char Open, char Close) {
            var Start = Index;
            var Stop = Length;

            if (String.FindMatchingBrackets(Open, Close, ref Start, ref Stop, false))
                return Stop;

            return -1;
        }

        public bool IsAt(char C) {
            if (Index > -1 && Index < Length)
                return String[Index] == C;
            return false;
        }

        public bool IsAt(char C, bool IgnoreCase) {
            if (Index > -1 && Index < Length) {
                if (IgnoreCase) {
                    return char.ToLower(String[Index]) == char.ToLower(C);
                }
                else {
                    return String[Index] == C;
                }
            }
            return false;
        }

        public bool IsAt(string str) {
            return string.Compare(String, Index, str, 0, str.Length, StringComparison.Ordinal) == 0;
        }

        public bool IsAfter(char C) {
            return this[Index - 1] == C;
        }

        public bool IsAfter(char C, int Index) {
            return this[Index - 1] == C;
        }

        public bool Consume(char C) {
            if (IsAt(C)) {
                Tick();
                return true;
            }

            return false;
        }

        public bool Consume(string Str) {
            if (string.Compare(String, Index, Str, 0, Str.Length, StringComparison.Ordinal) == 0) {
                Index += Str.Length;
                return true;
            }
            return false;
        }

        public bool Consume(StringIterator It, int Length, bool IgnoreCase) {
            if (IgnoreCase) {
                if (string.Compare(String, Index, It.String, It.Index, Length, StringComparison.OrdinalIgnoreCase) == 0) {
                    Index += Length;
                    return true;
                }
            }
            else {
                if (string.Compare(String, Index, It.String, It.Index, Length, StringComparison.Ordinal) == 0) {
                    Index += Length;
                    return true;
                }
            }
            return false;
        }

		public bool Consume(params Func<char, bool>[] Possible) {
			if (!IsDone ()) {
				if (!Possible.Any (f => f (Current)))
					return false;
				
				do {
					Tick ();
				} 
				while (Possible.Any (f => f (Current)));

				return true;
			}
			return false;
		}

        public T ConsumeAnyKey<T>(KeyValueCollection<T> Collection) {
            foreach (var P in Collection) {
                if (Consume(P.Key)) {
                    return P.Value;
                }
            }
            return default(T);
        }

        public bool ConsumeUntil(string str) {
            var I = Index;
            while (I < Length) {
                if (IsAt(str)) {
                    Index = I;
                    return true;
                }
                I++;
            }

            return false;
        }

        public void ConsumeUntil(Func<char, bool> f) {
            var I = Index;
            while (I < Length) {
                if (f(String[I])) {
                    Index = I;
                    return;
                }
                I++;
            }
        }

        public string ExtractUntil(char Token) {
            var Next = Find(Token);
            if (Next == -1) return null;

            var Str = String.Substring(Index, Next - Index);
            Index = Next;
            return Str;
        }


        public string ExtractUntil(string Token) {
            var Next = Find(Token);
            if (Next == -1) return null;

            var Str = String.Substring(Index, Next - Index);
            Index = Next;
            return Str;
        }

        public bool Goto(char C) {
            var Next = Find(C);

            if (Next == -1)
                return false;

            Index = Next;
            return true;
        }

        public bool Goto(char Open, char Close) {
            var Count = 1;

            for (; !IsDone(); Tick()) {
                if (IsAt('\\'))
                    continue;
                
                if (IsAt(Close)) {
                    Count--;

                    if (Count == 0) {
                        return true;
                    }
                }
                else if (IsAt(Open))
                    Count++;
            }
            return false;
        }

        public bool Goto(string str) {
            var Next = Find(str);

            if (Next == -1)
                return false;

            Index = Next;
            return true;
        }

        public bool Goto(string Open, string Close) {
            var Count = 1;

            for (; !IsDone(); Tick()) {
                if (IsAt('\\'))
                    continue;

                if (IsAt(Close)) {
                    Count--;

                    if (Count == 0) {
                        return true;
                    }
                }
                else if (IsAt(Open))
                    Count++;
            }
            return false;
        }

        public bool Goto(StringIterator It, int Length, bool IgnoreCase) {
            int i = String.IndexOf(It.Current, Index);

            if (i == -1)
                return false;

            do {
                if (It.IsAt('\\'))
                    It.Tick();

                if (String[i] == It[It.Index]) {
                    if (String.Compare(i, It.String, It.Index, Length, IgnoreCase)) {
                        Index = i;
                        return true;
                    }
                }
            }
            while (i++ + Length < String.Length);
            return false;
        }

        public char GotoFirstPossible(params char[] Chars) {
            return GotoFirstPossible(ref Index, String.Length, Chars);
        }

        public char GotoFirstPossible(ref int Index, params char[] Chars) {
            return GotoFirstPossible(ref Index, String.Length, Chars);
        }

        public char GotoFirstPossible(ref int Index, int Length, params char[] Chars) {
            int Location = Length;
            char Lowest = default(char);

            for (int i = 0; i < Chars.Length; i++) {
                int Loc = String.IndexOf(Chars[i], Index, Location - Index);

                if (Loc != -1) {
                    Location = Loc;
                    Lowest = Chars[i];
                }
            }

            Index = Location;
            return Lowest;
        }

        public string GotoFirstPossible(params string[] strings) {
            return GotoFirstPossible(ref Index, String.Length, strings);
        }

        public string GotoFirstPossible(ref int Index, params string[] strings) {
            return GotoFirstPossible(ref Index, String.Length, strings);
        }

        public string GotoFirstPossible(ref int Index, int Length, params string[] Chars) {
            int Location = Length;
            string Lowest = null;

            for (int i = 0; i < Chars.Length; i++) {
                int Loc = String.Find(Chars[i], Index, Location);

                if (Loc != -1) {
                    Location = Loc;
                    Lowest = Chars[i];
                }
            }

            Index = Location;
            return Lowest;
        }

        public bool EndsWith(string Str) {
			if (Str.Length > Length || string .IsNullOrEmpty(Str))
				return false;

			for (int i = 1; i <= Str.Length; i++) {
				if (this [Length - i] != Str [Str.Length - i]) {
					return false;
				}
			}
			return true;
		}

        public bool EndsWith(StringIterator It) {
            for (int i = 1; i <= Length - Index && i <= It.Length - It.Index; i ++) {
                if (this[Length - i] != It[It.Length - i]) {
                    return false;
                }
            }
            return true;
        }

        public bool IsDone() {
            return Index >= Length;
        }

        public string Substring(int Index) {
            if (Index < 0 || Index > Length)
                return null;

            return String.Substring(Index, Length - Index);
        }

        public string Substring(int Index, int Length) {
            if (Index < 0 || Index + Length > this.Length)
                return null;

            return String.Substring(Index, Length);
        }

        public string Extract(char End) {
            var Start = Index;

            if (Goto(End)) {
                var Result = Substring(Start, Index - Start);
                Consume(End);
                return Result;
            }

            return null;
        }

        public string Extract(char Open, char Close) {
            if (Consume(Open)) {
                var Start = Index;

                if (Goto(Open, Close)) {
                    var Result = Substring(Start, Index - Start);
                    Consume(Close);
                    return Result;
                }
            }
            Index -= 1;
            return null;
        }

        public string Extract(string End) {
            var Start = Index;

            if (Goto(End)) {
                var Result = Substring(Start, Index - Start);
                Consume(End);
                return Result;
            }

            return null;
        }

        public string Extract(string Open, string Close) {
            if (Consume(Open)) {
                var Start = Index;

                if (Goto(Open, Close)) {
                    var Result = Substring(Start, Index - Start);
                    Consume(Close);
                    return Result;
                }
            }

            Index -= Open.Length;
            return null;
        }

        public StringIterator Clone() {
			return new StringIterator (String, Index, Length);
		}

		public StringIterator Clone(int Index) {
			return new StringIterator (String, Index, Length);
		}

		public StringIterator Clone(int Index, int Length) {
			return new StringIterator (String, Index, Length);
		}

        public void Tick() {
            Index++;
        }

        public void ConsumeWhitespace() {
            while (!IsDone() && char.IsWhiteSpace(String[Index]))
                Tick();
        }

        public override string ToString() {
            return String.Substring(Index, Length - Index);
        }
    }
}