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

        public static void ConsumeBetween(this String Text, ref int Index, String Open, String Close) {
            int X, Y, Z = 1;

            X = Text.Find(Open, Index);

            for (Y = X + Open.Length; Y < Text.Length; Y++) {
                if (Text[Y] == '\\') {
                    Y++;
                    continue;
                }

                if (Text.Compare(Open, Y)) {
                    if (Open == Close) {
                        break;
                    }
                    else {
                        Z++;
                        continue;
                    }
                }

                if (Text.Compare(Close, Y)) {
                    Z--;

                    if (Z == 0) {
                        break;
                    }
                }
            }

            Index = Y + Close.Length;
        }

        public static void ConsumeWhitespace(this String Text, ref int Index) {
            if (Index < 0 || string.IsNullOrEmpty(Text))
                return;

            while (Index < Text.Length && char.IsWhiteSpace(Text[Index]))
                Index++;
        }
    }
}
