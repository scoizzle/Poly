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

        public int Find(char C) {
            var Res = String.Find(C, Index);

            if (Res > Length)
                return -1;

            return Res;
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

            for (; !IsDone(); Tick()) {
                if (IsAt('\\'))
                    continue;

                if (IsAt(Open))
                    Count++;

                if (IsAt(Close)) {
                    Count--;

                    if (Count == -1) {
                        try { return Index; }
                        finally { Index = Start; }
                    }
                }
            }

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
            if (string.Compare(String, Index, Str, 0, Str.Length) == 0) {
                Index += Str.Length;
                return true;
            }
            return false;
        }

        public bool Consume(StringIterator It, int Length, bool IgnoreCase) {
            if (string.Compare(String, Index, It.String, It.Index, Length, IgnoreCase) == 0) {
                Index += Length;
                return true;
            }
            return false;
        }

        public void ConsumeUntil(Func<char, bool> f) {
            while (!IsDone()) {
                if (f(String[Index]))
                    return;
                Tick();
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

                if (IsAt(Close)) {
                    Count--;

                    if (Count == -1) {
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
            for (int i = this.Index; i + Length < this.Length; i++) {
                if (String[i] == It[It.Index]) {
                    if (string.Compare(this.String, i, It.String, It.Index, Length, IgnoreCase) == 0) {
                        this.Index = i;
                        return true;
                    }
                }
            }
            return false;
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
            while (!IsDone()) {
                if (!char.IsWhiteSpace(String[Index]))
                    break;

                Tick();
            }
        }

        public override string ToString() {
            return String.Substring(Index, Length - Index);
        }
    }
}
