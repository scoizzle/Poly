using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Poly.Script.Node {
    public class NotEqual : Operator {
        public NotEqual(object Left, object Right) {
            this.Left = Left;
            this.Right = Right;
        }

        public override object Evaluate(Data.jsObject Context) {
            var L = GetLeft(Context);
            var R = GetRight(Context);

            var V = DataType.Equal(L, R);

            if (V is bool)
                return !(bool)V;
            return false;
        }

        public static Operator Parse(Engine Engine, string Text, ref int Index, int LastIndex, string Left) {
            if (Text.Compare("!=", Index)) {
                Index += 2;
                ConsumeWhitespace(Text, ref Index);

                return new NotEqual(
                    Engine.Parse(Left, 0),
                    Engine.Parse(Text, ref Index, LastIndex)
                );
            }

            return null;
        }

        public override string ToString() {
            return Left.ToString() + " != " + Right.ToString();
        }
    }
}
