using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Poly.Script.Expressions {
    using Nodes;
    using Types;
    public class Multiply : Operator {
        public Multiply(Node Left,  Node Right) {
            this.Left = Left;
            this.Right = Right;
        }

        public override object Execute(dynamic Left, dynamic Right) {
            try { return Left * Right; }
            catch { return null; }
        }

        public static Node Assignment(Engine Engine, StringIterator It, Node Left) {
            return new Assign(Left as Variable,
                new Multiply(
                    Left,
                    Engine.ParseOperation(Engine, It)
                )
            );
        }

        public static Node Parse(Engine Engine, StringIterator It, Node Left) {
            return new Multiply(Left, Engine.ParseValue(It));
        }

        public override string ToString() {
            return Left.ToString() + " * " + Right.ToString();
        }
    }
}
