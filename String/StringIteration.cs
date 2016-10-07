using System;
using System.Collections.Generic;
using System.Text;

using Poly;
using Poly.Data;

namespace System {
    public static class StringIteration {
        public static bool Consume(this String Text, String Part, ref int Index) {
            if (!Text.Compare(Part, Index, false)) {
                return false;
            }

            Index += Part.Length;
            return true;
        }

        public static void Consume(this String Text, ref int Index, Func<char, bool> F) {
            if (Text == null || Index < 0 || Index > Text.Length)
                return;

            while (Index < Text.Length && F(Text[Index]))
                Index++;
        }

        public static void ConsumeUntil(this String Text, ref int Index, Func<char, bool> f) {
            if (Text == null || Index < 0 || Index > Text.Length)
                return;

            while (Index < Text.Length) {
                if (f(Text[Index])) {
                    return;
                }
                Index++;
            }
        }

        public static void ConsumeWhitespace(this String Text, ref int Index) {
            Consume(Text, ref Index, char.IsWhiteSpace);
        }

        public static void ConsumeAlpha(this String Text, ref int Index) {
            Consume(Text, ref Index, char.IsLetter);
        }

        public static void ConsumeNumeric(this String Text, ref int Index) {
            Consume(Text, ref Index, char.IsNumber);
        }

        public static void ConsumeAlphaNumeric(this String Text, ref int Index) {
            Consume(Text, ref Index, char.IsLetterOrDigit);
        }

        public static void ConsumeBetween(this String Text, ref int Index, String Open, String Close) {
            int X, Y, Z = 1;

            X = Text.Find(Open, Index);

            for (Y = X + Open.Length; Y < Text.Length; Y++) {
                if (Text[Y] == '\\') {
                    Y++;
                    continue;
                }

                if (Text.Compare(Close, Y)) {
                    Z--;

                    if (Z == 0) {
                        break;
                    }
                }

                if (Text.Compare(Open, Y)) {
                    Z++;
                }
            }

            Index = Y + Close.Length;
        }

        public static bool All(this String Text, int Start, int Stop, Func<char, bool> f) {
            if (string.IsNullOrEmpty(Text) || Start >= Stop || Start < 0 || Stop > Text.Length)
                return false;

            do {
                if (!f(Text[Start]))
                    return false;

                Start++;
            }
            while (Start < Stop);

            return true;
        }
    }
}
