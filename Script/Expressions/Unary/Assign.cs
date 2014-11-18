using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Poly.Script.Expressions {
    using Nodes;

    public class Assign : Operator {
        public Assign(Variable Left,  Node Right) {
            this.Left = Left;
            this.Right = Right;
        }

        public override object Evaluate(Data.jsObject Context) {
            object Val;
            if (Right != null)
                Val = Right.Evaluate(Context);
            else
                Val = null;

            (Left as Variable).Assign(Context, Val);
            return Val;
        }

        public override string ToString() {
            return string.Format("{0} = {1}", Left, Right);
        }

        public static Assign Parse(Engine Engine, string Text, ref int Index, int LastIndex, string Left) {
            if (Text.Compare("=", Index)) {
                Index += 1;
                ConsumeWhitespace(Text, ref Index);

                return new Assign(
                    Variable.Parse(Engine, Left, 0),
                    Engine.Parse(
                        Text, ref Index, LastIndex
                    )
                );
            }
            return null;
        }
    }
}
