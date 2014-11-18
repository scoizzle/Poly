using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Poly.Script.Expressions {
    using Types;
    using Nodes;

    public class NotEqual : Operator {
        public NotEqual(Node Left,  Node Right) {
            this.Left = Left;
            this.Right = Right;
        }

        public override object Execute(object Left, object Right) {
            return !DataType.Equal(Left, Right);
        }

        public static Operator Parse(Engine Engine, string Text, ref int Index, int LastIndex, string Left) {
            if (Text.Compare("!=", Index)) {
                Index += 2;
                ConsumeWhitespace(Text, ref Index);

                return new NotEqual(
                    Engine.Parse(Left, 0),
                    Engine.Parse(Text, ref Index, LastIndex)
                );
            }

            return null;
        }

        public override string ToString() {
            return Left.ToString() + " != " + Right.ToString();
        }
    }
}
