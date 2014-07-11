using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Poly.Script.Node {
    public class Subtract : Operator {
        public Subtract(object Left, object Right) {
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

            return DataType.Subtract(L, R);
        }

        public static Operator Parse(Engine Engine, string Text, ref int Index, int LastIndex, string Left) {
            if (Text.Compare("-=", Index)) {
                Index += 2;
                Text.ConsumeWhitespace(ref Index);

                var Var = Variable.Parse(Engine, Left, 0);

                return new Assign(
                    Var,
                    new Subtract(
                        Var,
                        Engine.Parse(Text, ref Index, LastIndex)
                    )
                );
            }
            else if (Text.Compare("--")) {
                Index += 2;
                var Var = Variable.Parse(Engine, Left, 0);

                return new Assign(
                    Var,
                    new Subtract(
                        Var,
                        1
                    )
                );
            }
            else if (Text.Compare("-", Index)) {
                Index += 1;
                Text.ConsumeWhitespace(ref Index);

                return new Subtract(
                    Engine.Parse(Left, 0),
                    Engine.Parse(Text, ref Index, LastIndex)
                );
            }

            return null;
        }

        public override string ToString() {
            return Left.ToString() + " - " + Right.ToString();
        }
    }
}
