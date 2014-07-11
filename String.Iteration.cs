using System;
using System.Collections.Generic;
using System.Text;

using Poly;
using Poly.Data;

namespace System {
    public static class StringIteration {
        public static void ConsumeBetween(this String Text, ref int Index, String Open, String Close) {
            int X, Y, Z = 1;

            X = Text.IndexOf(Open, Index);

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
            while (Index < Text.Length && (char.IsWhiteSpace(Text[Index]) || Text[Index] == ';' || Text[Index] == ','))
                Index++;
        }
    }
}
