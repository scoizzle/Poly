using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Poly.Data;

namespace Poly.Script.Expressions {
    using Nodes;
    using Types;

    public class Comparative : Operator {
        public Comparative(Node Left, Node Right) {
            this.Left = Left;
            this.Right = Right;
        }

        public override object Evaluate(jsObject Context) {
            var L = Left.Evaluate(Context);

            if (L != null)
                return L;

            return Right.Evaluate(Context);
        }

        public static Operator Parse(Engine Engine, string Text, ref int Index, int LastIndex, string Left) {
            if (Text.Compare("??", Index)) {
                Index += 2;
                ConsumeWhitespace(Text, ref Index);

                return new Comparative(
                    Engine.Parse(Left, 0),
                    Engine.Parse(Text, ref Index, LastIndex)
                );
            }

            return null;
        }

        public override string ToString() {
            return string.Format("{0} ?? {1}", Left, Right);
        }
    }
}
