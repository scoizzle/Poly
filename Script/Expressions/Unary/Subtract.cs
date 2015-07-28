using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Poly.Script.Expressions {
    using Nodes;
    using Types;

    public class Subtract : Operator {        
        public Subtract(Node Left,  Node Right) {
            this.Left = Left;
            this.Right = Right;
        }

        public override object Execute(dynamic Left, dynamic Right) {
            try { return Left - Right; }
            catch { return null; }
        }

        public static Operator Parse(Engine Engine, string Text, ref int Index, int LastIndex, string Left) {
            if (Text.Compare("-=", Index)) {
                Index += 2;
                ConsumeWhitespace(Text, ref Index);

                var Var = Variable.Parse(Engine, Left, 0);

                return new Assign(
                    Var,
                    new Subtract(
                        Var,
                        Engine.Parse(Text, ref Index, LastIndex)
                    )
                );
            }
            else if (Text.Compare("--", Index)) {
                Index += 2;
                var Var = Variable.Parse(Engine, Left, 0);

                return new Assign(
                    Var,
                    new Subtract(
                        Var,
                        new Integer(1)
                    )
                );
            }
            else if (Text.Compare("-", Index)) {
                Index += 1;
                ConsumeWhitespace(Text, ref Index);

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
