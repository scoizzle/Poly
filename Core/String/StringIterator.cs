using System;
using System.Collections.Generic;
using System.Linq;

namespace Poly {
    public class StringIterator {
        struct Segment {
			public int Index, LastIndex, Offset;
        }

        Segment Section;
        Stack<Segment> Segments;

        public string String;

        public int Index { 
            get { return Section.Index; }
            set { Section.Index = value; }
        }

        public int LastIndex {
            get { return Section.LastIndex; }
            set { Section.LastIndex = value; }
        }

        public StringIterator(string String) : this(String, String.Length) { }

        public StringIterator(string String, int lastIndex) : this(String, 0, lastIndex) { }

        public StringIterator(string str, int index, int lastIndex) {
            String = str;
            Segments = new Stack<Segment>();
            Section = new Segment {
                Index = index,
                LastIndex = lastIndex
            };
        }

        public char this[int Index] {
            get {
                return String[Index];
            }
        }

        public char Current {
            get {
                if (Index < 0 || IsDone())
                    return char.MinValue;

                return String[Index];
            }
        }

        public int Length { get { return LastIndex - Index; } }

        public int Find(char C) {
            if (IsDone()) return -1;
            return String.IndexOf(C, Index, Length);
        }

        public int Find(string Str) {
			var Res = String.Find(Str, Index, LastIndex);

            if (Res > LastIndex)
                return -1;

            return Res;
        }

        public int Find(char Open, char Close) {
            var Start = Index;
            var Stop = LastIndex;

            if (String.FindMatchingBrackets(Open, Close, ref Start, ref Stop, false))
                return Stop;

            return -1;
        }

        public bool IsAt(char C) {
            return Current == C;
        }

        public bool IsAt(char C, bool IgnoreCase) {
            if (IsDone()) return false;

            if (IgnoreCase)
                return char.ToLower(Current) == char.ToLower(C);
			
            return Current == C;
        }

        public bool IsAt(string str) {
            return string.Compare(String, Index, str, 0, str.Length, StringComparison.Ordinal) == 0;
        }

        public bool IsAt(string str, bool IgnoreCase) {
            if (IgnoreCase)
                return string.Compare(String, Index, str, 0, str.Length, StringComparison.OrdinalIgnoreCase) == 0;
            else
                return string.Compare(String, Index, str, 0, str.Length, StringComparison.Ordinal) == 0;
        }
        

        public bool IsAt(StringIterator It, bool IgnoreCase) {
            if (IgnoreCase)
                return string.Compare(String, Index, It.String, 0, It.Length, StringComparison.OrdinalIgnoreCase) == 0;
            else
                return string.Compare(String, Index, It.String, 0, It.Length, StringComparison.Ordinal) == 0;
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
            if (IsAt(Str)) {
                Index += Str.Length;
                return true;
            }

            return false;
        }

        public bool Consume(StringIterator It, bool IgnoreCase) {
            var le = It.Length;
            var i = Index;

            if (IgnoreCase && string.Compare(String, i, It.String, 0, le, StringComparison.OrdinalIgnoreCase) == 0) {
                Index = i + le;
                return true;             
            }
            else if (string.Compare(String, i, It.String, 0, le, StringComparison.Ordinal) == 0) {
                Index = i + le;
                return true;
            }
            return false;
        }

		public bool Consume(params Func<char, bool>[] Possible) {
            var i = Index;
            var s = Index;
            var l = LastIndex;
            var S = String;

            while (i < l && Possible.Any(S[i]))
                i++;

            if (i > s && i <= l) {
                Index = i;
                return true;
            }

            return false;
		}
        
        public bool ConsumeUntil(char chr) {
            var i = Index;
            var l = LastIndex;

            while (i < l) {
                var c = String[i];
                if (c == chr) {
                    Index = i;
                    return true;
                }
                i++;
            }

            return false;
        }

        public bool ConsumeUntil(string str) {
            var i = Index;
            var l = LastIndex;
            var le = str.Length;
            var S = String;

            while (i < l) {
                i = S.IndexOf(str[0], i);

                if (i == -1) break;

                if (string.Compare(S, i, str, 0, le, StringComparison.Ordinal) == 0) {
                    Index = i;
                    return true;
                }

                i++;
            }

            return false;
        }

        public bool ConsumeUntil(Func<char, bool> f) {
            var i = Index;
            var s = Index;
            var l = LastIndex;
            var S = String;

            while (i < l && !f(S[i]))
                i++;

            if (i > s && i <= l) {
                Index = i;
                return true;
            }

            return false;
        }

        public string ExtractUntil(char Token) {
            var i = Index;
            var f = Find(Token);

            if (f == -1) return null;
            else Index = f;

            return String.Substring(i, f - i);
        }

        public string ExtractUntil(string Token) {
            var i = Index;
            var f = Find(Token);

            if (f == -1) return null;
            else Index = f;

            return String.Substring(i, f - i);
        }

        public string ExtractUntil(params Func<char, bool>[] Possible) {
            var i = Index;
            var s = Index;
            var l = LastIndex;
            var S = String;

            while (i < l && !Possible.Any(S[i]))
                i++;

            if (i > s && i <= l) {
                Index = i;
                return S.Substring(s, i - s);
            }

            return null;
        }

        public string ExtractUntilAndConsume(char Token) {
            var str = ExtractUntil(Token);

            if (str != null) {
                Consume(Token);
                return str;
            }

            return null;
        }

        public string ExtractUntilAndConsume(string Token) {
            var str = ExtractUntil(Token);

            if (str != null) {
                Consume(Token);
                return str;
            }

            return null;
        }

        public bool Goto(char C) {
            var Next = Find(C);

            if (Next == -1)
                return false;

            Index = Next;
            return true;
        }

        public bool Goto(char Open, char Close) {
            var Start = Index;
            var Count = 1;

            do {
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

                Tick();
            }
            while (!IsDone());

            Index = Start;
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
            var Start = Index;
            var Count = 1;

            do {
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

                Tick();
            }
            while (!IsDone());

            Index = Start;
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
            var Start = Index;

            while (!IsDone()) {
                var c = Chars.FirstOrDefault(IsAt);

                if (c != default(char)) {
                    if (!IsAfter('\\'))
                        return c;
                }

                Tick();
            }

            Index = Start;
            return default(char);
        }

        public string GotoFirstPossible(params string[] Chars) {
            var Start = Index;

            while (!IsDone()) {
                var c = Chars.FirstOrDefault(IsAt);

                if (c != null) {
                    if (!IsAfter('\\'))
                        return c;
                }

                Tick();
            }

            Index = Start;
            return null;
        }

        public bool EndsWith(string Str) {
			if (Str.Length > String.Length || string .IsNullOrEmpty(Str))
				return false;

            var l = Str.Length;
            var i = LastIndex - l - 1;

            return string.Compare(String, i, Str, 0, l, StringComparison.Ordinal) == 0;
		}

        public bool EndsWith(StringIterator It) {
            for (int i = 1; i <= LastIndex - Index && i <= It.LastIndex - It.Index; i ++) {
                if (this[LastIndex - i] != It[It.LastIndex - i]) {
                    return false;
                }
            }
            return true;
        }

        public bool IsDone() {
            return Index >= LastIndex;
        }

        public string Substring(int Index) {
            if (Index < 0 || Index > LastIndex)
                return null;

            return String.Substring(Index, LastIndex - Index);
        }

        public string Substring(int Index, int Length) {
            if (Index < 0 || Index + Length > this.LastIndex)
                return null;

            return String.Substring(Index, Length);
        }

        public string Extract(char End) {
			if (SelectSection(End)) {
				var Result = ToString();

				ConsumeSection();

				return Result;
			}
			return null;
        }

		public string Extract(char Open, char Close) {
			if (SelectSection(Open, Close)) {
				var Result = ToString();

				ConsumeSection();

				return Result;
			}
			return null;
		}

		public string Extract(string End) {
			if (SelectSection(End)) {
				var Result = ToString();

				ConsumeSection();

				return Result;
			}
			return null;
		}

		public string Extract(string Open, string Close) {
			if (SelectSection(Open, Close)) {
				var Result = ToString();

				ConsumeSection();

				return Result;
			}
			return null;
		}

		public bool SelectSection(char Next) {
			PushSection(1);

			var Start = Index;
			if (Goto(Next)) {
				LastIndex = Index;
				Index = Start;
				return true;
			}

			PopSection();
			return false;
		}

        public bool SelectSection(char Open, char Close) {
            PushSection(1);

            if (Consume(Open)) {
                var Start = Index;

                if (Goto(Open, Close)) {
                    LastIndex = Index;
                    Index = Start;
                    return true;
                }
            }

            PopSection();
            return false;
		}

		public bool SelectSection(string Next) {
			PushSection(Next.Length);

			var Start = Index;
			if (Goto(Next)) {
				LastIndex = Index;
				Index = Start;
				return true;
			}

			PopSection();
			return false;
		}
        
        public bool SelectSection(string Open, string Close) {
			PushSection(Close.Length);

            if (Consume(Open)) {
                var Start = Index;

                if (Goto(Open, Close)) {
                    LastIndex = Index;
                    Index = Start;
                    return true;
                }
            }

            PopSection();
            return false;
        }

        public bool SelectSection(params Func<char, bool>[] f) {
            PushSection();

            var Start = Index;
            if (Consume(f)) {
                LastIndex = Index;
                Index = Start;
                return true;
            }

            PopSection();
            return false;
        }
        
        public bool SelectSection<T>(Func<T, bool> Consume) where T : StringIterator {
            PushSection();

            var Start = Index;
            if (Consume(this as T)) {
                LastIndex = Index;
                Index = Start;
                return true;
            }

            PopSection();
            return false;
        }
        
        public bool SelectSection<T>(Action<T> Consume) where T : StringIterator {
            PushSection();

            var Start = Index;
            Consume(this as T);

            if (Index > Start) {
                LastIndex = Index;
                Index = Start;
                return true;
            }

            PopSection();
            return false;
        }
        
        public void PushSection() {
            PushSection(Index, LastIndex);
		}

		public void PushSection(int offset) {
			PushSection(Index, LastIndex, offset);
		}

		public void PushSection(int index, int lastIndex, int offset = 0) {
            Segments.Push(Section);

            Section = new Segment { 
                Index = index,
				LastIndex = lastIndex,
				Offset = offset
            };
        }

        public void PopSection() {
            Section = Segments.Pop();
        }

        public void ConsumeSection() {
			var index = LastIndex + Section.Offset;
            PopSection();
            Index = index;
        }

		public StringIterator Clone() {
			return new StringIterator (String, Index, LastIndex);
		}

		public StringIterator Clone(int Index) {
			return new StringIterator (String, Index, LastIndex);
		}

		public StringIterator Clone(int Index, int Length) {
			return new StringIterator (String, Index, Length);
		}

        public void Tick() {
            Index++;
        }

        public bool ConsumeWhitespace() {
            return Consume(char.IsWhiteSpace);
        }

        public override string ToString() {
            return String.Substring(Index, LastIndex - Index);
        }
    }
}