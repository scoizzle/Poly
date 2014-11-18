using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Poly.Script.Expressions {
    using Nodes;
    using Types;
    public class LessThanEqual : Operator {
        public LessThanEqual(Node Left,  Node Right) {
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

            var V = DataType.LessThan(Left, Right);

            if (!V) {
                V = DataType.Equal(Left, Right);
            }

            return V;
        }

        public static Operator Parse(Engine Engine, string Text, ref int Index, int LastIndex, string Left) {
            if (Text.Compare("<=", Index)) {
                Index += 2;
                ConsumeWhitespace(Text, ref Index);

                return new LessThanEqual(
                    Engine.Parse(Left, 0),
                    Engine.Parse(Text, ref Index, LastIndex)
                );
            }

            return null;
        }

        public override string ToString() {
            return Left.ToString() + " <= " + Right.ToString();
        }
    }
}
