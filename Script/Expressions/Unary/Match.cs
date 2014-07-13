using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Poly.Script.Node {
    public class Match : Operator {
        public Match(object Left, object Right) {
            this.Left = Left;
            this.Right = Right;
        }

        public override object Evaluate(Data.jsObject Context) {
            var L = GetLeft(Context);
            var R = GetRight(Context);

            if (L == null || R == null) {
                return null;
            }

            var LS = L.ToString();
            var RS = R.ToString();

            var Data = LS.Match(RS);

            if (Data != null) {
                Data.CopyTo(Context);
                return Data;
            }

            return null;
        }

        public static Operator Parse(Engine Engine, string Text, ref int Index, int LastIndex, string Left) {
            if (Text.Compare("~", Index)) {
                Index += 1;
                ConsumeWhitespace(Text, ref Index);

                var Var = Variable.Parse(Engine, Left, 0);

                return new Match(
                    Var,
                    Engine.Parse(Text, ref Index, LastIndex)   
                );
            }
            return null;
        }

        public override string ToString() {
            return Left.ToString() + " ~ " + Right.ToString();
        }
    }
}
