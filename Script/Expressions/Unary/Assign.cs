using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Poly.Script.Node {
    public class Assign : Operator {
        public Assign(object Left, object Right) {
            this.Left = Left;
            this.Right = Right;
        }

        public override object Evaluate(Data.jsObject Context) {
            if (Left is Variable) {
                return (Left as Variable).Assign(Context, GetRight(Context));
            }
            return null;
        }

        public static Assign Parse(Engine Engine, string Text, ref int Index, int LastIndex, string Left) {
            if (Text.Compare("=", Index)) {
                Index += 1;
                ConsumeWhitespace(Text, ref Index);

                var Debug = Text.Substring(Index);

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
