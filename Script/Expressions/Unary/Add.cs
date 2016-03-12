﻿using System;

namespace Poly.Script.Expressions {
    using Nodes;
    public class Add : Operator {
        public Add(Node Left,  Node Right) {
            this.Left = Left;
            this.Right = Right;
        }

        public override object Execute(dynamic Left, dynamic Right) {
            try { return Left + Right; }
            catch { return null; }
        }

        public static Operator Parse(Engine Engine, string Text, ref int Index, int LastIndex, string Left) {
            if (Text.Compare("+=", Index)) {
                Index += 2;
                ConsumeWhitespace(Text, ref Index);

                var Var = Variable.Parse(Engine, Left, 0);

                return new Assign(
                    Var,
                    new Add(
                        Var,
                        Engine.Parse(Text, ref Index, LastIndex)
                    )
                );
            }
            else if (Text.Compare("++", Index)) {
                Index += 2;
                var Var = Variable.Parse(Engine, Left, 0);

                return new Assign(
                    Var,
                    new Add(
                        Var,
                        new StaticValue(1)
                    )
                );
            }
            else if (Text.Compare("+", Index)) {
                Index += 1;
                ConsumeWhitespace(Text, ref Index);

                return new Add(
                    Engine.Parse(Left, 0),
                    Engine.Parse(Text, ref Index, LastIndex)
                );
            }

            return null;
        }

        public override string ToString() {
            return Left.ToString() + " + " + Right.ToString();
        }
    }
}
