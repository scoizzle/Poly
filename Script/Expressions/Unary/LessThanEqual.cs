using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Poly.Script.Node {
    public class LessThanEqual : Operator {
        public LessThanEqual(object Left, object Right) {
            this.Left = Left;
            this.Right = Right;
        }

        public override object Evaluate(Data.jsObject Context) {
            var L = GetLeft(Context);
            var R = GetRight(Context);

            if (L == null || R == null) {
                if (L != null)
                    return L;
                if (R != null)
                    return R;
                return null;
            }

            var V = DataType.LessThan(L, R);

            if (V is bool && (bool)V) {
                return true;
            }
            
            V = DataType.Equal(L, R);

            if (V is bool && (bool)V) {
                return true;
            }

            return false;
        }

        public static Operator Parse(Engine Engine, string Text, ref int Index, int LastIndex, string Left) {
            if (Text.Compare("<=", Index)) {
                Index += 2;
                ConsumeWhitespace(Text, ref Index);

                return new LessThanEqual(
                    Engine.Parse(Left, 0),
                    Engine.Parse(Text, ref Index, LastIndex)
                );
            }

            return null;
        }

        public override string ToString() {
            return Left.ToString() + " <= " + Right.ToString();
        }
    }
}
