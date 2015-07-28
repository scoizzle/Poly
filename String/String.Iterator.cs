using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Poly {
    public class StringIterator {
        public string String;
        public int Index, Length;

        public StringIterator(string String) {
            this.String = String;
            this.Length = String.Length;
            this.Index = 0;
        }

        public StringIterator(string String, int Length) {
            this.String = String;
            this.Length = Length;
            this.Index = 0;
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
            return String.Find(C, Index, Length);
        }

        public int Find(string Str) {
            var Res = String.Find(Str, Index);

            if (Res > Length)
                return -1;

            return Res;
        }

        public int Find(char Open, char Close) {
            var Count = 0;
            var Start = Index;

            for (; Start < Length; Start++) {
                if (this[Start] == '\\')
                    continue;

                if (this[Start] == Open)
                    Count++;

                if (this[Start] == Close) {
                    Count--;

                    if (Count == 0) {
                        return Start;
                    }
                }
            }

            return -1;
        }

        public char FirstPossible(params char[] Chars) {
            return FirstPossible(ref Index, String.Length, Chars);
        }

        public char FirstPossible(ref int Index, params char[] Chars) {
            return FirstPossible(ref Index, String.Length, Chars);
        }

        public char FirstPossible(ref int Index, int Length, params char[] Chars) {
            int Location = Length;
            char Lowest = default(char);

            for (int i = 0; i < Chars.Length; i++) {
                int Loc = String.Find(Chars[i], this.Index, Location);

                if (Loc != -1) {
                    Location = Loc;
                    Lowest = Chars[i];
                }
            }

            Index = Location;
            return Lowest;
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
            return StringExtensions.Compare(String, Index, str, 0, str.Length);
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
            if (this.String.Compare(Index, Str, 0, Str.Length)) {
                Index += Str.Length;
                return true;
            }
            return false;
        }

        public bool Consume(StringIterator It, int Length, bool IgnoreCase) {
            if (this.String.Compare(Index, It.String, It.Index, Length, IgnoreCase)) {
                Index += Length;
                return true;
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

        public bool Goto(char C) {
            var Next = Find(C);

            if (Next == -1)
                return false;

            Index = Next;
            return true;
        }

        public bool Goto(char Open, char Close) {
            var Count = 0;

            for (; !IsDone(); Tick()) {
                if (IsAt('\\'))
                    continue;

                if (IsAt(Open))
                    Count++;
                else
                if (IsAt(Close)) {
                    Count--;

                    if (Count == 0) {
                        return true;
                    }
                }
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

        public bool Goto(StringIterator It, int Length, bool IgnoreCase) {
            int i = String.Find(It.Current, this.Index);

            if (i == -1)
                return false;

            do {
                if (It.IsAt('\\'))
                    It.Tick();

                if (String[i] == It[It.Index]) {
                    if (this.String.Compare(i, It.String, It.Index, Length, IgnoreCase)) {
                        this.Index = i;
                        return true;
                    }
                }
            }
            while (i++ + Length < String.Length);
            return false;
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
            return Index == Length;
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
