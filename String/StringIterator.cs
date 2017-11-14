using System;
using System.Collections.Generic;

namespace Poly {
    public class StringIterator {
        struct Segment {
			public int Index, LastIndex, Offset;
        }

        Segment Section;
        Stack<Segment> Segments;

        public string String;

        public StringIterator(string str) {
            String = str;
            Segments = new Stack<Segment>();
            Section = new Segment
            {
                Index = 0,
                LastIndex = str?.Length ?? 0
            };
        }

        public StringIterator(string str, int lastIndex) {
            String = str;
            Segments = new Stack<Segment>();
            Section = new Segment {
                Index = 0,
                LastIndex = lastIndex
            };
        }

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
                if (Index < 0 || IsDone)
                    return default(char);

                return String[Index];
            }
        }

        public char Previous {
            get {
                if (Index <= 0)
                    return default(char);

                return String[Index - 1];
            }
        }

        public char Last {
            get {
                return String[LastIndex - 1];
            }
        }

        public int Length { 
            get { return LastIndex - Index; } 
        }

        public int Index { 
            get { return Section.Index; }
            set { Section.Index = value; }
        }

        public int LastIndex {
            get { return Section.LastIndex; }
            set { Section.LastIndex = value; }
        }

        public int Offset {
            get { return Section.Offset; }
            set { Section.Offset = value; }
        }

        public bool IsDone {
            get { return Index >= LastIndex; }
            set { if (value) Index = LastIndex; }
        }

        public void Consume() {
            Index++;
        }

        public void Consume(int count) {
            Index += count;
        }

        public bool Consume(char character) {
            return StringIteration.Consume(String, ref Section.Index, character);
        }

        public bool Consume(string text) {
            return StringIteration.Consume(String, ref Section.Index, text, 0, text.Length);
        }

        public bool Consume(StringIterator It) {
            return StringIteration.Consume(String, ref Section.Index, It.String, It.Index, It.Length);
        }

        public bool Consume(Func<char, bool> possible) {
            return StringIteration.Consume(String, ref Section.Index, Section.LastIndex, possible);
        }

		public bool Consume(params Func<char, bool>[] possible) {
            return StringIteration.Consume(String, ref Section.Index, Section.LastIndex, possible);
		}

        public bool ConsumeUntil(Func<char, bool> possible) {
            return StringIteration.ConsumeUntil(String, ref Section.Index, Section.LastIndex, possible);
        }

        public bool ConsumeUntil(params Func<char, bool>[] possible) {
            return StringIteration.ConsumeUntil(String, ref Section.Index, Section.LastIndex, possible);
        }

        public bool IsAt(char C) {
            return Current == C;
        }

        public bool IsAt(string str) {
            if (Length < str.Length) return false;
            return String.Compare(Index, str, 0, str.Length);
        }
        
        public bool IsAt(StringIterator It) {
            if (Length < It.Length) return false;
            return String.Compare(Index, It.String, It.Index, It.Length);
        }

        public bool IsAt(Func<char, bool> possible) {
            if (IsDone) return false;
            return possible(Current);
        }

        public bool IsAt(params Func<char, bool>[] possible) {
            if (IsDone) return false;
            var current = Current;

            for (var i = 0; i < possible.Length; i++) {
                if (possible[i](current)) {
                    return true;
                }
            }

            return false;
        }

        public bool IsAfter(char C) {
            return Previous == C;
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

        public string Extract(char character) {
            var start = Index;

            if (Goto(character)) {
                var sub_string = Substring(start, Index - start);
                Consume();
                return sub_string;
            }
            
            return null;
        }

        public string Extract(string text) {
            var start = Index;

            if (Goto(text)) {
                var sub_string = Substring(start, Index - start);
                Consume(text.Length);
                return sub_string;
            }
            
            return null;
        }

        public string ExtractUntil(char character) {
            var start = Index;

            if (Goto(character))
                return Substring(start, Index - start);
            
            return null;
        }

        public string ExtractUntil(string text) {
            var start = Index;

            if (Goto(text))
                return Substring(start, Index - start);
            
            return null;
        }

        public string ExtractUntil(params Func<char, bool>[] possible) {
            var start = Index;

            if (ConsumeUntil(possible))
                return Substring(start, Index - start);
            
            return null;
        }

        public int Find(char C) {
            if (IsDone) return -1;
            return String.IndexOf(C, Index, Length);
        }

        public int Find(string Str) {
            return String.Find(Index, LastIndex, Str, 0, Str.Length);
        }

        public bool Goto(char C) {
            var Next = Find(C);

            if (Next == -1)
                return false;

            Index = Next;
            return true;
        }

        public bool Goto(string str) {
            var Next = Find(str);

            if (Next == -1)
                return false;

            Index = Next;
            return true;
        }

        public bool GotoFirstPossible(out char found, params char[] characters) {
            var start = Index;

            while (!IsDone) {
                if (IsAt('\\')) {
                    Consume();
                    continue;
                }

                for (var i = 0; i < characters.Length; i++) {
                    found = characters[i];

                    if (IsAt(found))
                        return true;
                }

                Consume();
            }

            found = default(char);
            Index = start;
            return false;
        }

        public bool GotoFirstPossible(out string found, params string[] strings) {
            var start = Index;

            while (!IsDone) {
                if (IsAt('\\')) {
                    Consume();
                    continue;
                }

                for (var i = 0; i < strings.Length; i++) {
                    found = strings[i];

                    if (IsAt(found))
                        return true;
                }

                Consume();
            }

            found = default(string);
            Index = start;
            return false;
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

        public bool SelectSection(char Open, char Close) {
            if (!IsAt(Open))
                return false;
                
            PushSection(1);

            var start = Index;
            var stop = LastIndex;

            if (String.FindMatchingBrackets(Open, Close, ref start, ref stop, false)) {
                Index = start;
                LastIndex = stop;
                return true;
            }

            PopSection();
            return false;
        }
        
        public bool SelectSection(string Open, string Close) {
            if (!IsAt(Open))
                return false;
                
			PushSection(Close.Length);

            var start = Index;
            var stop = LastIndex;

            if (String.FindMatchingBrackets(Open, Close, ref start, ref stop, false)) {
                Index = start;
                LastIndex = stop;
                return true;
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
        
        public bool SelectSection(Func<StringIterator, bool> Consume) {
            PushSection();

            var Start = Index;
            if (Consume(this)) {
                LastIndex = Index;
                Index = Start;
                return true;
            }

            PopSection();
            return false;
        }
        
        public bool SelectSection(Action<StringIterator> Consume) {
            PushSection();

            var Start = Index;
            Consume(this);

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
            if (Segments.Count > 0)
                Section = Segments.Pop();
            else 
                Index = LastIndex;
        }

        public void ConsumeSection() {
			var index = LastIndex + Section.Offset;
            PopSection();
            Index = index;
        }

        public bool ConsumeWhitespace() {
            return Consume(char.IsWhiteSpace);
        }

        public bool ConsumeIntegerNumeric() {
            Consume('+');
            Consume('-');

            if (Consume(char.IsDigit))
                return !IsAt('.');

            return false;
        }

        public bool ConsumeFloatingNumeric() {
            Consume("-");

            if (!Consume(char.IsDigit)) {
                return false;
            }

            if (Consume('.')) {
                if (!Consume(char.IsDigit))
                    return false;
            }

            if (Consume('e') || Consume('E')) {
                Consume('+');
                Consume('-');

                if (!Consume(char.IsDigit))
                    return false;
            }

            return true;
        }

        public string Substring(int Index) {
            if (Index < 0 || Index > LastIndex)
                return null;

            return String.Substring(Index, LastIndex - Index);
        }

        public string Substring(int Index, int Length) {
            if (Index < 0 || Index + Length > String.Length)
                return null;

            return String.Substring(Index, Length);
        }

        public StringIterator Clone(int index, int lastIndex) {
            return new StringIterator(String, index, lastIndex);
        }

        public override string ToString() {
            if (String == null)
                return string.Empty;

            if (Index == 0 && LastIndex == String.Length)
                return String;

            if (IsDone)
                return string.Empty;

            return String.Substring(Index, LastIndex - Index);
        }

        public static implicit operator StringIterator(string str) {
            return new StringIterator(str, 0, str?.Length ?? 0);
        }

        public static implicit operator string(StringIterator it) {
            return it.ToString();
        }
    }
}