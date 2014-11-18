using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Poly.Script.Expressions {
    using Nodes;
    using Types;

    public class Or : Operator {
        public Or(Node Left,  Node Right) {
            this.Left = Left;
            this.Right = Right;
        }

        public override object Evaluate(Data.jsObject Context) {
            return Bool.EvaluateNode(this.Left, Context) || Bool.EvaluateNode(this.Right, Context);
        }

        public static Operator Parse(Engine Engine, string Text, ref int Index, int LastIndex, string Left) {
            if (Text.Compare("||", Index)) {
                Index += 2;
                ConsumeWhitespace(Text, ref Index);

                return new Or(
                    Engine.Parse(Left, 0),
                    Engine.Parse(Text, ref Index, LastIndex)
                );
            }

            return null;
        }

        public override string ToString() {
            return Left.ToString() + " || " + Right.ToString();
        }
    }
}
