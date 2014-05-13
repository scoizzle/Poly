using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Poly.Script.Node {
    public class NotNull : Operator {
        public NotNull(object Right) {
            this.Right = Right;
        }

        public override object Evaluate(Data.jsObject Context) {
            return !Bool.EvaluateNode(Right, Context);
        }

        public static Operator Parse(Engine Engine, string Text, ref int Index, int LastIndex, string Left) {
            if (Text.Compare("!", Index)) {
                Index += 1;
                ConsumeWhitespace(Text, ref Index);

                return new NotNull(
                    Engine.Parse(Text, ref Index, LastIndex)
                );
            }

            return null;
        }

        public override string ToString() {
            return "!" + Right.ToString();
        }
    }
}
