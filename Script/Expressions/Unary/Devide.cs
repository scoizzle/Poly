using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Poly.Script.Expressions {
    using Nodes;
    using Types;

    public class Devide : Operator {
        public Devide(Node Left,  Node Right) {
            this.Left = Left;
            this.Right = Right;
        }

        public override object Execute(object Left, object Right) {
            if (Left == null || Right == null) {
                if (Left != null)
                    return Left;
                if (Right != null)
                    return Right;
                return null;
            }

            return DataType.Devide(Left, Right);
        }

        public static Operator Parse(Engine Engine, string Text, ref int Index, int LastIndex, string Left) {
            if (Text.Compare("/=", Index)) {
                Index += 2;
                ConsumeWhitespace(Text, ref Index);

                var Var = Variable.Parse(Engine, Left, 0);

                return new Assign(
                    Var,
                    new Devide(
                        Var,
                        Engine.Parse(Text, ref Index, LastIndex)
                    )
                );
            }
            else if (Text.Compare("/", Index)) {
                Index += 1;
                ConsumeWhitespace(Text, ref Index);

                return new Devide(
                    Engine.Parse(Left, 0),
                    Engine.Parse(Text, ref Index, LastIndex)
                );
            }

            return null;
        }

        public override string ToString() {
            return Left.ToString() + " / " + Right.ToString();
        }
    }
}
